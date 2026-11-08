using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace AutoEverything.AutoMarkPawn
{
    /// <summary>
    /// 伤痕标识：统计殖民者身上的永久疤痕与缺失部位，供殖民者栏头像右下角的条形标识使用。
    ///
    /// 判定口径（2026-11-08 用户决策）：
    /// - 永久疤痕：HediffUtility.IsPermanent（Hediff_Injury 愈合后留下的疤痕）
    /// - 缺失部位：Hediff_MissingPart（断肢、摘除器官）
    /// - 按身体部位去重：同一部位的多道疤痕只算 1 条，避免烧伤等场景条数虚高
    ///
    /// 显示规则：条数 = 伤痕处数，最多 3 条（超出封顶），避免遮挡头像。
    /// 判定结果按 Pawn 缓存 2500 tick（与 RoleIconDef/TierCacheService 一致）：
    /// 疤痕/断肢在游戏中极少变化，命中路径仅 1 次字典查询。
    /// </summary>
    public static class ScarMarkDef
    {
        /// <summary>头像上最多绘制的条数：超出只显示 3 条（用户决策）。</summary>
        public const int MaxBars = 3;

        /// <summary>条形颜色：琥珀橙，与右上角深红角色图标区分（用户决策）。</summary>
        public static readonly Color BarColor = new Color(0.95f, 0.60f, 0.15f);

        private struct CountCacheEntry
        {
            public byte count;
            public int tick;
        }

        private const int CacheTTL = 2500;
        private const int CleanupInterval = 60000;

        private static readonly Dictionary<Pawn, CountCacheEntry> cache = new Dictionary<Pawn, CountCacheEntry>();
        private static readonly List<Pawn> cleanupBuffer = new List<Pawn>();
        // 部位去重缓冲：只在缓存 miss 时使用，复用避免每次分配
        private static readonly HashSet<BodyPartRecord> partBuffer = new HashSet<BodyPartRecord>();
        private static int lastCleanupTick = -9999;

        /// <summary>
        /// 获取应在头像上绘制的条数（0~3）。无伤痕返回 0。
        /// </summary>
        public static int GetBarCount(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null) return 0;

            int tick = Find.TickManager.TicksGame;
            MaybeCleanup(tick);

            if (cache.TryGetValue(pawn, out CountCacheEntry e) && tick - e.tick < CacheTTL)
                return e.count;

            int count = CountDistinctScarredParts(pawn);
            if (count > MaxBars) count = MaxBars;
            cache[pawn] = new CountCacheEntry { count = (byte)count, tick = tick };
            return count;
        }

        /// <summary>
        /// 清空伤痕计数缓存：疤痕/断肢状态突变（如手术截肢）时可立即调用。
        /// </summary>
        public static void ClearCache()
        {
            cache.Clear();
        }

        /// <summary>
        /// 统计伤痕部位数：永久疤痕 + 缺失部位，按 BodyPartRecord 去重。
        /// 空 Part 的 Hediff（罕见，如全身性伤害）按 Hediff 计数，避免漏计。
        /// </summary>
        private static int CountDistinctScarredParts(Pawn pawn)
        {
            partBuffer.Clear();
            int count = 0;
            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff h = hediffs[i];
                if (!IsScarOrMissingPart(h)) continue;
                if (h.Part == null)
                {
                    count++;
                    continue;
                }
                if (partBuffer.Add(h.Part)) count++;
            }
            return count;
        }

        private static bool IsScarOrMissingPart(Hediff hediff)
        {
            if (hediff is Hediff_MissingPart) return true;
            return hediff is Hediff_Injury && HediffUtility.IsPermanent(hediff);
        }

        /// <summary>
        /// 周期清理缓存：移除 Dead/!Spawned 的 Pawn，避免引用泄漏。
        /// 模式与 RoleIconDef.MaybeCleanupIconCache 一致。
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
