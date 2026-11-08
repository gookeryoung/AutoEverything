using System.Collections.Generic;
using RimWorld;
using Verse;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.Core
{
    /// <summary>
    /// 评级缓存服务：统一多模块的 Pawn 评级缓存，避免重复实现与重复计算。
    ///
    /// 设计：
    /// - Pawn 为 key，每个 Pawn 独立缓存条目（殖民者/非殖民者共用同一字典，互不污染）
    /// - 默认 TTL = 2500 tick（≈42 秒）：评级在此窗口内稳定，技能/特质不会突变
    /// - 自动 cleanup：每 60000 tick 移除 Dead/!Spawned 的 Pawn，避免内存泄漏
    /// - 调用方可通过 <see cref="Invalidate(Pawn)"/> 强制失效（如玩家点排序按钮需立即反映当前状态）
    ///
    /// 覆盖模块：
    /// - PawnMarker.IsHighValue（Harmony Postfix 每帧调用）
    /// - AESettings.ReorderColonistBar 比较器（玩家点排序按钮触发）
    /// - WorkAllocator.AssignServiceWorkType（工作分配周期触发）
    /// </summary>
    internal static class TierCacheService
    {
        private struct Entry
        {
            public CombatTier tier;
            public int tick;
        }

        private const int DefaultTTL = 2500;
        private const int CleanupInterval = 60000;

        private static readonly Dictionary<Pawn, Entry> cache = new Dictionary<Pawn, Entry>();
        private static readonly List<Pawn> cleanupBuffer = new List<Pawn>();
        private static int lastCleanupTick = -9999;

        /// <summary>
        /// 获取 Pawn 的战斗评级（含自定义评级覆盖），结果缓存 2500 tick。
        /// </summary>
        public static CombatTier GetTier(Pawn pawn)
        {
            if (pawn == null) return CombatTier.X;

            int tick = Find.TickManager.TicksGame;
            MaybeCleanup(tick);

            if (cache.TryGetValue(pawn, out Entry e) && tick - e.tick < DefaultTTL)
                return e.tier;

            CombatTier tier = CombatEvaluator.GetCombatTier(pawn);
            cache[pawn] = new Entry { tier = tier, tick = tick };
            return tier;
        }

        /// <summary>
        /// 失效指定 Pawn 的缓存。
        /// 用于玩家主动操作（如点排序按钮）需立即反映当前状态的场景。
        /// </summary>
        public static void Invalidate(Pawn pawn)
        {
            cache.Remove(pawn);
        }

        /// <summary>
        /// 清空全部缓存。玩家修改评级判定参数后调用，
        /// 让新规则立即作用于所有 Pawn（否则最长需等待 TTL 过期）。
        /// </summary>
        public static void Clear()
        {
            cache.Clear();
        }

        /// <summary>
        /// 周期清理：移除 Dead/!Spawned 的 Pawn，避免引用泄漏。
        /// 由 GetTier/GetValue 触发，每 60000 tick 执行一次。
        /// </summary>
        private static void MaybeCleanup(int tick)
        {
            if (tick - lastCleanupTick < CleanupInterval) return;
            lastCleanupTick = tick;

            cleanupBuffer.Clear();
            foreach (var kvp in cache)
            {
                Pawn p = kvp.Key;
                if (p == null || p.Dead || !p.Spawned)
                    cleanupBuffer.Add(p);
            }
            for (int i = 0; i < cleanupBuffer.Count; i++)
                cache.Remove(cleanupBuffer[i]);
        }
    }
}
