using System.Collections.Generic;
using RimWorld;
using Verse;
using AutoEverything.Core;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.AutoMarkPawn
{
    /// <summary>
    /// 高价值非殖民者标记模块：为 S+ 档次的访客、敌人、交易者、野生人类等
    /// 在头顶实时绘制鲜艳红色星标 "★"。
    ///
    /// 设计目的：
    /// - 玩家一眼可辨高价值非殖民者（S/SS/SSS 档），便于优先俘虏、招募或警惕
    /// - 不修改任何 Pawn 的 Nick/Name，避免影响派系交互界面、交易显示、战斗通知
    /// - 视觉标记纯前端绘制（Harmony Postfix），安全可逆，无存档副作用
    ///
    /// 标记范围（非殖民者人类）：
    /// - 敌对派系敌人（来袭突袭/袭营的敌方 Pawn）
    /// - 友好派系访客（来访的 Visitor）
    /// - 交易者（派系/轨道交易商）
    /// - 野生人类/难民/流浪者
    ///
    /// 视觉实现：
    /// - HarmonyPatches.PawnUIOverlay_DrawPawnGUIOverlay_Patch 在 DrawPawnGUIOverlay Postfix 中绘制
    /// - 头顶位置：pawn.DrawPos 上方，世界坐标转屏幕坐标
    /// - 颜色：Color(1.0f, 0.15f, 0.15f) 鲜艳红色
    ///
    /// 触发方式：
    /// - 实时绘制：Harmony 补丁每帧调用（DrawPawnGUIOverlay 由游戏每帧触发）
    /// - 消息提示：ITab 勾选时统计当前非殖民者高价值对象数量并弹消息
    /// </summary>
    public static class PawnMarker
    {
        // 评级缓存：DrawPawnGUIOverlay Postfix 每帧调用 IsHighValue，
        // 若直接 GetCombatTier 则每个非殖民者每秒重算 60 次完整评级（技能/特质/配偶递归查询）。
        // 2500 tick ≈ 42 秒，评级在此窗口内稳定，非殖民者技能/特质不会突变。
        private struct CachedTier
        {
            public CombatTier tier;
            public int tick;
        }

        private const int CacheInterval = 2500;
        private const int CleanupInterval = 60000;
        private static readonly Dictionary<Pawn, CachedTier> tierCache = new Dictionary<Pawn, CachedTier>();
        private static readonly List<Pawn> cleanupBuffer = new List<Pawn>();
        private static int lastCleanupTick = -9999;

        /// <summary>
        /// 判断 Pawn 是否为高价值（S+ 档次，含自定义评级覆盖）。
        /// CombatEvaluator.GetCombatTier 优先返回自定义评级，未命中则返回自动判定。
        /// 结果按 tick 缓存，避免每帧重算（DrawPawnGUIOverlay Postfix 高频调用）。
        /// </summary>
        public static bool IsHighValue(Pawn pawn)
        {
            if (pawn == null) return false;

            int tick = Find.TickManager.TicksGame;

            // 定期清理：移除不 Spawned / 已死亡的 Pawn（避免内存泄漏）
            if (tick - lastCleanupTick > CleanupInterval)
            {
                CleanupCache();
                lastCleanupTick = tick;
            }

            if (tierCache.TryGetValue(pawn, out CachedTier c) && tick - c.tick < CacheInterval)
                return c.tier >= CombatTier.S;

            CombatTier tier = CombatEvaluator.GetCombatTier(pawn);
            tierCache[pawn] = new CachedTier { tier = tier, tick = tick };
            return tier >= CombatTier.S;
        }

        /// <summary>
        /// 清理缓存中不 Spawned 或已死亡的 Pawn，避免引用泄漏。
        /// 由 IsHighValue 周期触发（60000 tick ≈ 17 分钟），无显式调用方。
        /// </summary>
        private static void CleanupCache()
        {
            cleanupBuffer.Clear();
            foreach (var kvp in tierCache)
            {
                Pawn p = kvp.Key;
                if (p == null || p.Dead || !p.Spawned)
                    cleanupBuffer.Add(p);
            }
            for (int i = 0; i < cleanupBuffer.Count; i++)
                tierCache.Remove(cleanupBuffer[i]);
        }

        /// <summary>
        /// 判断 Pawn 是否为可标记的非殖民者对象：
        /// - 已生成在地图上（Spawned）
        /// - 未死亡（Dead）
        /// - 非玩家阵营（Faction != OfPlayer），涵盖敌人/访客/交易者/野生人类
        /// - 人类like 种族（CanManageGear），排除动物/机械族/昆虫
        ///
        /// 倒下（Downed）的 Pawn 仍标记：便于玩家优先俘虏高价值敌人。
        /// 食尸鬼不排除：非殖民者食尸鬼也按评级标记供玩家参考。
        /// </summary>
        public static bool IsMarkableTarget(Pawn pawn)
        {
            if (pawn == null) return false;
            if (!pawn.Spawned) return false;
            if (pawn.Dead) return false;
            if (pawn.Faction == Faction.OfPlayer) return false;
            if (!PawnSuitabilityChecker.CanManageGear(pawn)) return false;
            return true;
        }

        /// <summary>
        /// 统计当前所有地图上非殖民者高价值对象数量。
        /// 供 ITab 勾选时消息提示用，周期路径不调用（避免扫描开销）。
        /// </summary>
        public static int CountMarkablePawns()
        {
            int count = 0;
            foreach (Map map in Find.Maps)
            {
                if (map == null) continue;
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (!IsMarkableTarget(pawn)) continue;
                    if (IsHighValue(pawn)) count++;
                }
            }
            return count;
        }
    }
}
