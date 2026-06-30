using System.Collections.Generic;
using RimWorld;
using Verse;
using AutoEverything.Core;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.AutoMarkPawn
{
    /// <summary>
    /// 高价值殖民者自动标记模块：为 S+ 档次殖民者 Nick 追加鲜艳红色星标 "★"。
    ///
    /// 设计目的：
    /// - 玩家一眼可辨高价值殖民者（S/SS/SSS 档），便于重点保护与培养
    /// - 星标追加在评级前缀之后（如 "S#王五★"），与评级标签系统协同
    /// - 自定义评级覆盖也生效：玩家指定 S 档的殖民者同样标记
    ///
    /// 与评级标签系统的关系：
    /// - 评级标签（autoTierTag）：管理 "S#" 前缀
    /// - 高价值标记（autoMarkPawn）：管理 "★" 后缀
    /// - 两者独立开关，互不依赖
    /// - TierTagHelper.Strip 同时剥离前缀与星标，保证自定义评级查询命中
    /// - ApplyTierTagsToAllPawns 会保留已有星标，避免同一 tick 内 Name 重复设置
    ///
    /// 触发方式（与 AutoExecutor 一致）：
    /// - 周期触发：每 3000 tick（约 50 秒）
    /// - 新增殖民者：殖民者数量增加时立即触发
    /// - ITab 勾选：玩家勾选时立即触发
    /// </summary>
    public static class PawnMarker
    {
        /// <summary>
        /// 判断 Pawn 是否为高价值（S+ 档次，含自定义评级覆盖）。
        /// CombatEvaluator.GetCombatTier 优先返回自定义评级，未命中则返回自动判定。
        /// </summary>
        public static bool IsHighValue(Pawn pawn)
        {
            if (pawn == null) return false;
            return CombatEvaluator.GetCombatTier(pawn) >= CombatTier.S;
        }

        /// <summary>
        /// 为所有玩家殖民者（含食尸鬼）应用星标：
        /// - S+ 档次且 autoMarkPawn 开启 → Nick 追加 "★"
        /// - 非 S+ 或 autoMarkPawn 关闭 → 剥离已有 "★"
        /// 返回当前标记的高价值殖民者数量（供消息提示用）。
        ///
        /// 覆盖范围与 AESettings.ApplyTierTagsToAllPawns 一致：殖民者 + 食尸鬼。
        /// 食尸鬼也标记以供玩家参考其价值。
        /// </summary>
        public static int ApplyMarkers()
        {
            if (!AESettings.autoMarkPawn)
            {
                // 开关关闭时清除所有星标
                ClearMarkers();
                return 0;
            }

            int marked = 0;
            foreach (Pawn pawn in CollectMarkablePawns())
            {
                if (!PawnSuitabilityChecker.CanManageGear(pawn)) continue;

                NameTriple nt = pawn.Name as NameTriple;
                if (nt == null) continue;

                string currentNick = nt.Nick ?? string.Empty;
                // 剥离已有星标得到基础 Nick（保留评级前缀）
                string baseNick = TierTagHelper.StripStar(currentNick);

                bool shouldMark = IsHighValue(pawn);
                string newNick = shouldMark
                    ? baseNick + TierTagHelper.StarMarker
                    : baseNick;

                if (newNick != currentNick)
                {
                    pawn.Name = new NameTriple(nt.First, newNick, nt.Last);
                }

                if (shouldMark) marked++;
            }
            return marked;
        }

        /// <summary>
        /// 清除所有殖民者（含食尸鬼）Nick 上的星标后缀。
        /// 仅剥离尾部 "★"，保留评级前缀与其他字符。
        /// 在 autoMarkPawn 取消勾选时调用。
        /// </summary>
        public static int ClearMarkers()
        {
            int touched = 0;
            foreach (Pawn pawn in CollectMarkablePawns())
            {
                NameTriple nt = pawn.Name as NameTriple;
                if (nt == null) continue;

                string currentNick = nt.Nick ?? string.Empty;
                if (!TierTagHelper.HasStar(currentNick)) continue;

                string cleanNick = TierTagHelper.StripStar(currentNick);
                pawn.Name = new NameTriple(nt.First, cleanNick, nt.Last);
                touched++;
            }
            return touched;
        }

        /// <summary>
        /// 收集需要标记的 Pawn：殖民者 + 食尸鬼。
        /// 与 AESettings.ApplyTierTagsToAllPawns 收集逻辑一致，保证覆盖范围相同。
        /// </summary>
        private static List<Pawn> CollectMarkablePawns()
        {
            var pawns = new List<Pawn>();
            foreach (Pawn pawn in PawnsFinder.AllMaps_FreeColonists)
            {
                if (pawn != null) pawns.Add(pawn);
            }
            // 食尸鬼也加入标记范围（仅标记，不分配装备）
            foreach (Map map in Find.Maps)
            {
                if (map == null) continue;
                foreach (Pawn pawn in map.mapPawns.AllPawns)
                {
                    if (pawn == null) continue;
                    if (!DLCCompat.IsGhoul(pawn)) continue;
                    if (pawn.Faction == null || !pawn.Faction.IsPlayer) continue;
                    pawns.Add(pawn);
                }
            }
            return pawns;
        }
    }
}
