using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using AutoEverything.Core;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.AutoMarkPawn
{
    /// <summary>
    /// 高价值单位标记模块：为 S+ 档次（S/SS/SSS）的人类单位头顶实时绘制彩色星标 "★"。
    ///
    /// 设计目的：
    /// - 玩家一眼可辨高价值单位（S/SS/SSS 档），便于优先俘虏、招募、警惕或培养
    /// - 范围覆盖所有人类单位：殖民者、奴隶、囚犯、敌对、中立/盟友、野生人类/难民
    /// - 按单位类别用不同颜色星标区分（殖民者=金、奴隶=橙、囚犯=黄、敌对=红、中立/盟友=青、野生=白）
    /// - 不修改任何 Pawn 的 Nick/Name，纯前端绘制（Harmony Postfix），安全可逆，无存档副作用
    ///
    /// 触发方式：
    /// - 实时绘制：HarmonyPatches.PawnUIOverlay_DrawPawnGUIOverlay_Patch 在 DrawPawnGUIOverlay Postfix 中绘制
    /// - ITab 勾选时：全局重扫描（resetTracking=true），弹出消息列出所有当前高价值单位
    /// - 自动模式：地图人员变动时（新增人类like 单位），扫描新增高价值单位并弹消息提示
    ///
    /// 缓存：评级查询走 <see cref="TierCacheService"/>（2500 tick TTL，自动 cleanup）。
    /// 通知去重：<see cref="notifiedMarkedIds"/> 跟踪已通知玩家的 Pawn thingIDNumber，
    /// 仅在首次发现某 Pawn 为高价值时通知，避免重复弹消息。
    /// </summary>
    public static class PawnMarker
    {
        /// <summary>
        /// 标记类别：按单位所属派系/状态区分，用于星标颜色与消息展示。
        /// 优先级顺序：Prisoner > Slave > Colonist > Enemy > Neutral > WildHuman
        /// </summary>
        public enum MarkerCategory : byte
        {
            Colonist,    // 殖民者（玩家阵营自由人员，含食尸鬼）
            Slave,       // 奴隶（玩家阵营奴隶）
            Prisoner,    // 囚犯（被玩家关押）
            Enemy,       // 敌对单位
            Neutral,     // 中立/盟友单位
            WildHuman    // 野生人类/难民（无派系）
        }

        // 已通知玩家的高价值 Pawn thingIDNumber 集合
        // 仅在首次发现某 Pawn 为高价值时通知，避免重复弹消息
        // toggle on 时（resetTracking=true）清空，触发全局重扫描
        // 重建机制：每次扫描后用当前 ID 集合替换 notifiedMarkedIds，自动移除已不在场的 ID
        private static readonly HashSet<int> notifiedMarkedIds = new HashSet<int>();

        // 复用缓冲区：避免每次扫描分配新集合（单线程主线程使用，无需并发保护）
        private static readonly List<Pawn> scanBuffer = new List<Pawn>();
        private static readonly HashSet<int> currentIdsBuffer = new HashSet<int>();

        /// <summary>
        /// 判断 Pawn 是否为高价值（S+ 档次，含自定义评级覆盖）。
        /// CombatEvaluator.GetCombatTier 优先返回自定义评级，未命中则返回自动判定。
        /// 评级查询走 TierCacheService（2500 tick 缓存），避免每帧重算。
        /// </summary>
        public static bool IsHighValue(Pawn pawn)
        {
            if (pawn == null) return false;
            return TierCacheService.GetTier(pawn) >= CombatTier.S;
        }

        /// <summary>
        /// 判断 Pawn 是否为可标记的目标：
        /// - 已生成在地图上（Spawned）
        /// - 未死亡（Dead）
        /// - 人类like 种族（CanManageGear），排除动物/机械族/昆虫/异常实体
        ///
        /// 范围覆盖：殖民者、奴隶、囚犯、敌对、中立/盟友、野生人类。
        /// 倒下（Downed）的仍标记：便于优先俘虏高价值敌人。
        /// </summary>
        public static bool IsMarkableTarget(Pawn pawn)
        {
            if (pawn == null) return false;
            if (!pawn.Spawned) return false;
            if (pawn.Dead) return false;
            if (!PawnSuitabilityChecker.CanManageGear(pawn)) return false;
            return true;
        }

        /// <summary>
        /// 获取 Pawn 的标记类别（用于星标颜色与消息展示）。
        /// 优先级：囚犯 > 奴隶 > 殖民者 > 敌对 > 中立/盟友 > 野生人类。
        /// </summary>
        public static MarkerCategory GetMarkerCategory(Pawn pawn)
        {
            if (pawn.IsPrisonerOfColony) return MarkerCategory.Prisoner;
            if (pawn.IsSlaveOfColony) return MarkerCategory.Slave;
            if (pawn.Faction == Faction.OfPlayer) return MarkerCategory.Colonist;
            if (pawn.HostileTo(Faction.OfPlayer)) return MarkerCategory.Enemy;
            if (pawn.Faction != null) return MarkerCategory.Neutral;
            return MarkerCategory.WildHuman;
        }

        /// <summary>
        /// 获取类别对应的星标颜色。
        /// 殖民者=金、奴隶=橙、囚犯=黄、敌对=红、中立/盟友=青、野生=白
        /// </summary>
        public static Color GetMarkerColor(MarkerCategory category)
        {
            switch (category)
            {
                case MarkerCategory.Colonist: return new Color(1.0f, 0.84f, 0.0f);    // 金
                case MarkerCategory.Slave: return new Color(0.95f, 0.55f, 0.06f);  // 橙
                case MarkerCategory.Prisoner: return new Color(0.95f, 0.75f, 0.06f); // 黄
                case MarkerCategory.Enemy: return new Color(1.0f, 0.15f, 0.15f);   // 红
                case MarkerCategory.Neutral: return new Color(0.20f, 0.85f, 0.95f);  // 青
                default: return new Color(0.95f, 0.95f, 0.95f); // 白（野生人类）
            }
        }

        /// <summary>
        /// 收集当前所有地图上的高价值单位，填入 result 列表。
        /// 调用方需提供复用缓冲区，避免每次分配新 List。
        /// </summary>
        public static void CollectMarkedPawns(List<Pawn> result)
        {
            if (result == null) return;
            result.Clear();
            foreach (Map map in Find.Maps)
            {
                if (map == null) continue;
                IReadOnlyList<Pawn> all = map.mapPawns.AllPawnsSpawned;
                for (int i = 0; i < all.Count; i++)
                {
                    Pawn pawn = all[i];
                    if (!IsMarkableTarget(pawn)) continue;
                    if (IsHighValue(pawn)) result.Add(pawn);
                }
            }
        }

        /// <summary>
        /// 扫描所有地图上的高价值单位并更新内部跟踪集合。
        ///
        /// 输出：
        /// - <paramref name="allMarked"/>：当前所有高价值单位（按出现顺序）
        /// - <paramref name="newlyMarked"/>：本次新发现的高价值单位（allMarked 中此前未通知过的）
        ///
        /// 副作用：重建 <see cref="notifiedMarkedIds"/> 为当前所有高价值单位 ID 集合，
        /// 自动移除已不在场/不再高价值的 ID，避免内存泄漏与重复通知。
        ///
        /// <paramref name="resetTracking"/>=true 时先清空 notifiedMarkedIds（toggle on 全局重扫描使用），
        /// 所有当前高价值单位都视为"新发现"。
        /// </summary>
        public static void ScanAndMark(List<Pawn> allMarked, List<Pawn> newlyMarked, bool resetTracking)
        {
            if (allMarked != null) allMarked.Clear();
            if (newlyMarked != null) newlyMarked.Clear();

            if (resetTracking) notifiedMarkedIds.Clear();

            // 用 scanBuffer 收集当前所有高价值 Pawn
            scanBuffer.Clear();
            CollectMarkedPawns(scanBuffer);

            // 构建 currentIdsBuffer 并分发到 allMarked/newlyMarked
            currentIdsBuffer.Clear();
            for (int i = 0; i < scanBuffer.Count; i++)
            {
                Pawn p = scanBuffer[i];
                int id = p.thingIDNumber;
                currentIdsBuffer.Add(id);
                if (allMarked != null) allMarked.Add(p);
                if (newlyMarked != null && !notifiedMarkedIds.Contains(id))
                {
                    newlyMarked.Add(p);
                }
            }

            // 重建 notifiedMarkedIds：用当前 ID 集合替换，自动移除已不在场的 ID
            notifiedMarkedIds.Clear();
            foreach (int id in currentIdsBuffer) notifiedMarkedIds.Add(id);
        }

        /// <summary>
        /// 格式化高价值单位列表为消息文本。
        /// 列表项格式："- 类别名 单位名 (档位)"，多于 maxListed 个时显示前 maxListed 个加"... 等 N 个"。
        /// 空列表返回"未发现高价值单位"提示文本。
        /// </summary>
        public static string FormatMessage(List<Pawn> pawns, string headerKey, int maxListed = 8)
        {
            if (pawns == null || pawns.Count == 0)
            {
                return "AE_AutoMarkPawn_None".Translate();
            }

            StringBuilder sb = new StringBuilder();
            sb.Append(headerKey.Translate(pawns.Count));

            int listed = pawns.Count < maxListed ? pawns.Count : maxListed;
            for (int i = 0; i < listed; i++)
            {
                Pawn p = pawns[i];
                MarkerCategory cat = GetMarkerCategory(p);
                CombatTier tier = TierCacheService.GetTier(p);
                sb.AppendLine();
                sb.Append("- ");
                sb.Append(("AE_MarkCat_" + cat).Translate());
                sb.Append(' ');
                sb.Append(p.LabelShort);
                sb.Append(" (");
                sb.Append(tier);
                sb.Append(')');
            }
            if (pawns.Count > maxListed)
            {
                sb.AppendLine();
                sb.Append("AE_AutoMarkPawn_More".Translate(pawns.Count - maxListed));
            }
            return sb.ToString();
        }
    }
}
