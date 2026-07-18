using System.Collections.Generic;
using RimWorld;
using Verse;

namespace AutoEverything.Core
{
    /// <summary>
    /// Pawn 收集器：统一受管理 Pawn 的收集逻辑，避免多模块重复实现。
    ///
    /// 当前提供：
    /// - <see cref="AllManagedPawns"/>：所有受评级打标管理的 Pawn（殖民者 + 食尸鬼）
    ///
    /// 工作分配的候选收集条件特化（Spawned + 医疗 + workSettings 过滤）保留在 WorkAllocator 内，
    /// 不在此处统一以避免耦合工作分配特有逻辑（单一职责）。
    ///
    /// 注：返回的 List 为内部复用缓冲区，调用方使用完毕前不可再次调用本类方法。
    /// </summary>
    internal static class PawnCollector
    {
        // 复用缓冲区，避免每次调用分配新 List（KISS：单线程主线程使用，无需并发保护）
        private static readonly List<Pawn> managedBuffer = new List<Pawn>();

        /// <summary>
        /// 收集所有受评级打标管理的 Pawn：殖民者（含未 Spawned）+ 玩家食尸鬼。
        /// 食尸鬼不属于 FreeColonists，需从 AllPawns 中过滤。
        /// </summary>
        /// <remarks>
        /// 返回的 List 为内部复用缓冲区，调用方不应长期持有；遍历完即视为释放。
        /// </remarks>
        public static List<Pawn> AllManagedPawns()
        {
            managedBuffer.Clear();

            // 殖民者（PawnsFinder.AllMaps_FreeColonists 含未 Spawned 的殖民者，如正在卧床/运输中）
            foreach (Pawn pawn in PawnsFinder.AllMaps_FreeColonists)
            {
                if (pawn != null) managedBuffer.Add(pawn);
            }

            // 食尸鬼（仅玩家派系，从 map.mapPawns.AllPawns 过滤；食尸鬼也参与评级打标）
            foreach (Map map in Find.Maps)
            {
                if (map == null) continue;
                foreach (Pawn pawn in map.mapPawns.AllPawns)
                {
                    if (pawn == null) continue;
                    if (!DLCCompat.IsGhoul(pawn)) continue;
                    if (pawn.Faction == null || !pawn.Faction.IsPlayer) continue;
                    managedBuffer.Add(pawn);
                }
            }

            return managedBuffer;
        }
    }
}
