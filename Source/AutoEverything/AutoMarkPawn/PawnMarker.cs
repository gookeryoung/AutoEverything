using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;
using AutoEverything.Core;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.AutoMarkPawn
{
    /// <summary>
    /// 高价值单位标记模块：为 S+ 档次（S/SS/SSS）的人类单位在殖民者栏固定位置绘制深红色星标 "★"。
    ///
    /// 设计目的：
    /// - 玩家一眼可辨高价值单位（S/SS/SSS 档），便于优先俘虏、招募、警惕或培养
    /// - 范围覆盖所有人类单位：殖民者、奴隶、囚犯、敌对、中立/盟友、野生人类/难民
    /// - 星标统一使用深红色：与殖民者栏头像（多为浅色/皮肤色）形成强对比，避免按类别变色时与头像对比不足
    /// - 不修改任何 Pawn 的 Nick/Name，纯前端绘制（Harmony Postfix），安全可逆，无存档副作用
    ///
    /// 显示方式（参考 UsefulMarks MOD 设计）：
    /// - 殖民者栏固定标签：HarmonyPatches.ColonistBarDrawer_DrawColonist_Patch 在
    ///   ColonistBarColonistDrawer.DrawColonist Postfix 中绘制 ★ 到殖民者栏 Rect 右上角
    /// - 与相机缩放完全解耦：殖民者栏是固定 UI 元素，星标位置不受世界坐标缩放影响
    /// - 可视范围限制：仅殖民者栏中的 Pawn（殖民者/奴隶/食尸鬼）有星标；
    ///   非殖民者栏中的高价值单位（囚犯/敌对/中立/野生）无可视星标，
    ///   但 ScanAndMark 通知消息逻辑仍覆盖所有人类单位，玩家通过消息仍可知晓
    ///
    /// 类别用途：MarkerCategory 仅用于消息展示中的类别名翻译（如"殖民者"/"敌对"），
    /// 不再用于星标取色——星标颜色统一为深红色常量（见 HarmonyPatches.StarColor）。
    ///
    /// 触发方式：
    /// - 殖民者栏绘制：Harmony Postfix 每次殖民者栏绘制单个 Pawn 时调用（OnGUI 路径）
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
        /// 标记类别：按单位所属派系/状态区分，用于消息展示中的类别名翻译。
        /// 不再用于星标取色——星标统一深红色（见 HarmonyPatches.StarColor）。
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

        /// <summary>
        /// 类别判定输入参数（从 Pawn 收集，用于纯逻辑核心 GetMarkerCategoryCore）。
        /// 提取为 struct 便于单元测试：测试只需构造输入，无需 mock Pawn。
        /// 所有字段默认 false（与"未设置/无派系"语义一致），避免默认值产生歧义。
        /// </summary>
        internal struct CategoryInput
        {
            public bool IsPrisonerOfColony;  // pawn.IsPrisonerOfColony
            public bool IsSlaveOfColony;      // pawn.IsSlaveOfColony
            public bool IsColonistFaction;   // pawn.Faction == Faction.OfPlayer
            public bool IsHostileTo;         // pawn.HostileTo(Faction.OfPlayer)
            public bool HasFaction;           // pawn.Faction != null
        }

        /// <summary>
        /// 消息展示条目（从 Pawn 提取，用于纯逻辑核心 FormatMessageCore）。
        /// 测试只需构造 List&lt;MessageEntry&gt;，无需 mock Pawn.LabelShort / TierCacheService。
        /// </summary>
        internal struct MessageEntry
        {
            public string CategoryLabel;  // 类别翻译文本（如"殖民者"/"Slave"）
            public string Name;           // pawn.LabelShort
            public CombatTier Tier;       // TierCacheService.GetTier(pawn)
        }

        // 已通知玩家的高价值 Pawn thingIDNumber 集合
        // 仅在首次发现某 Pawn 为高价值时通知，避免重复弹消息
        // toggle on 时（resetTracking=true）清空，触发全局重扫描
        // 重建机制：每次扫描后用当前 ID 集合替换 notifiedMarkedIds，自动移除已不在场的 ID
        private static readonly HashSet<int> notifiedMarkedIds = new HashSet<int>();

        // 复用缓冲区：避免每次扫描分配新集合（单线程主线程使用，无需并发保护）
        private static readonly List<Pawn> scanBuffer = new List<Pawn>();
        private static readonly HashSet<int> currentIdsBuffer = new HashSet<int>();

        // FormatMessage 用的 MessageEntry 复用缓冲区（避免每次调用分配新 List）
        private static readonly List<MessageEntry> entryBuffer = new List<MessageEntry>();

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
            return GetMarkerCategoryCore(new CategoryInput
            {
                IsPrisonerOfColony = pawn.IsPrisonerOfColony,
                IsSlaveOfColony = pawn.IsSlaveOfColony,
                IsColonistFaction = pawn.Faction == Faction.OfPlayer,
                IsHostileTo = pawn.HostileTo(Faction.OfPlayer),
                HasFaction = pawn.Faction != null
            });
        }

        /// <summary>
        /// 类别判定纯逻辑核心：根据 Pawn 派系/状态标志位返回类别。
        /// 优先级：囚犯 > 奴隶 > 殖民者 > 敌对 > 中立/盟友 > 野生人类。
        /// 单元测试入口：构造 CategoryInput 即可验证全部 6 类别判定。
        /// 默认值（全 false）：HasFaction=false 表示无派系 → WildHuman，与"未设置"语义一致。
        /// </summary>
        internal static MarkerCategory GetMarkerCategoryCore(CategoryInput input)
        {
            if (input.IsPrisonerOfColony) return MarkerCategory.Prisoner;
            if (input.IsSlaveOfColony) return MarkerCategory.Slave;
            if (input.IsColonistFaction) return MarkerCategory.Colonist;
            if (input.IsHostileTo) return MarkerCategory.Enemy;
            if (input.HasFaction) return MarkerCategory.Neutral;
            return MarkerCategory.WildHuman;
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
            // 空列表：直接返回"未发现"
            if (pawns == null || pawns.Count == 0)
            {
                return "AE_AutoMarkPawn_None".Translate();
            }

            // 从 Pawn 列表提取 MessageEntry 列表（与 RimWorld 运行时耦合）
            // 使用静态复用缓冲区避免每次分配
            entryBuffer.Clear();
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                MarkerCategory cat = GetMarkerCategory(p);
                entryBuffer.Add(new MessageEntry
                {
                    CategoryLabel = ("AE_MarkCat_" + cat).Translate(),
                    Name = p.LabelShort,
                    Tier = TierCacheService.GetTier(p)
                });
            }

            // 翻译 header/more 文本（含 count 占位符替换）
            string headerText = headerKey.Translate(pawns.Count);
            string moreText = pawns.Count > maxListed
                ? "AE_AutoMarkPawn_More".Translate(pawns.Count - maxListed)
                : null;
            string noneText = "AE_AutoMarkPawn_None".Translate();

            return FormatMessageCore(entryBuffer, headerText, moreText, noneText, maxListed);
        }

        /// <summary>
        /// 消息格式化纯逻辑核心：根据条目列表与翻译后的文本拼装最终消息。
        /// 列表项格式："- 类别名 单位名 (档位)"，多于 maxListed 个时显示前 maxListed 个加 moreText。
        /// 空列表返回 noneText。
        ///
        /// 单元测试入口：测试构造 List&lt;MessageEntry&gt; 与 3 个文本参数即可验证：
        /// - 空列表 → noneText
        /// - 1 到 maxListed 条 → 完整列表
        /// - 超过 maxListed 条 → 前 maxListed + moreText 摘要
        /// </summary>
        internal static string FormatMessageCore(
            List<MessageEntry> entries, string headerText, string moreText, string noneText, int maxListed = 8)
        {
            if (entries == null || entries.Count == 0)
            {
                return noneText ?? string.Empty;
            }

            StringBuilder sb = new StringBuilder();
            sb.Append(headerText);

            int listed = entries.Count < maxListed ? entries.Count : maxListed;
            for (int i = 0; i < listed; i++)
            {
                MessageEntry e = entries[i];
                sb.AppendLine();
                sb.Append("- ");
                sb.Append(e.CategoryLabel);
                sb.Append(' ');
                sb.Append(e.Name);
                sb.Append(" (");
                sb.Append(e.Tier);
                sb.Append(')');
            }
            if (entries.Count > maxListed && moreText != null)
            {
                sb.AppendLine();
                sb.Append(moreText);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 计算新发现的高价值 ID 列表（dedup 纯逻辑核心）。
        /// - resetTracking=true：所有 currentIds 都视为"新发现"（toggle on 全局重扫描场景）
        /// - resetTracking=false：仅 currentIds 中不在 previousNotified 的 ID 视为"新发现"
        ///
        /// 单元测试入口：构造 previousNotified/currentIds 即可验证 dedup 行为与边界条件。
        /// 生产代码 <see cref="ScanAndMark"/> 用内联 HashSet.Contains 路径（O(1) 查询），
        /// 此处 List 版本仅供测试，避免在 ScanAndMark 中引入额外遍历与分配。
        /// </summary>
        internal static List<int> ComputeNewlyMarkedIds(
            HashSet<int> previousNotified, List<int> currentIds, bool resetTracking)
        {
            List<int> newlyMarked = new List<int>();
            for (int i = 0; i < currentIds.Count; i++)
            {
                int id = currentIds[i];
                if (resetTracking || !previousNotified.Contains(id))
                {
                    newlyMarked.Add(id);
                }
            }
            return newlyMarked;
        }
    }
}
