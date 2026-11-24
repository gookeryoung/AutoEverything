using System.Collections.Generic;
using RimWorld;
using Verse;

namespace AutoEverything.Core
{
    /// <summary>
    /// Pawn 收集器：统一受评级打标管理的 Pawn 收集逻辑，避免多模块重复实现。
    ///
    /// 提供两个入口：
    /// - <see cref="AllManagedPawns"/>：按玩家配置的评级范围收集（殖民者/食尸鬼、访问者、奴隶与囚犯）
    /// - <see cref="AllTaggablePawns"/>：忽略范围开关的全量收集，仅供「清除评级名称」使用，
    ///   避免玩家收窄范围后既有前缀无法清除
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
        private static readonly List<Pawn> taggableBuffer = new List<Pawn>();

        /// <summary>
        /// 按【自动】→ 人员评级 的范围配置收集参与评级的 Pawn。
        /// 食尸鬼跟随「殖民者」开关；奴隶/囚犯共用同一开关；访问者指地图上其他派系的 Guest 状态人形。
        /// </summary>
        /// <remarks>
        /// 返回的 List 为内部复用缓冲区，调用方不应长期持有；遍历完即视为释放。
        /// </remarks>
        public static List<Pawn> AllManagedPawns()
        {
            managedBuffer.Clear();
            Collect(managedBuffer,
                AESettings.tierScopeColonists,
                AESettings.tierScopeVisitors,
                AESettings.tierScopeSlavesPrisoners);
            return managedBuffer;
        }

        /// <summary>
        /// 忽略范围开关，收集所有可能被本 MOD 打过评级前缀的 Pawn。
        /// 供「清除评级名称」使用：玩家先评级再关闭某组范围时，仍能恢复原名。
        /// </summary>
        public static List<Pawn> AllTaggablePawns()
        {
            taggableBuffer.Clear();
            Collect(taggableBuffer, true, true, true);
            return taggableBuffer;
        }

        private static void Collect(List<Pawn> buffer, bool colonists, bool visitors, bool slavesPrisoners)
        {
            // 殖民者（PawnsFinder.AllMaps_FreeColonists 含未 Spawned 的殖民者，如正在卧床/运输中）
            if (colonists)
            {
                foreach (Pawn pawn in PawnsFinder.AllMaps_FreeColonists)
                {
                    if (pawn != null) buffer.Add(pawn);
                }
            }

            // 食尸鬼（仅玩家派系；食尸鬼不属于 FreeColonists，需从地图 Pawn 中过滤）
            // 跟随「殖民者」范围：食尸鬼是玩家侧亚人，与殖民者同组管理
            if (colonists)
            {
                foreach (Map map in Find.Maps)
                {
                    if (map == null) continue;
                    foreach (Pawn pawn in map.mapPawns.AllPawns)
                    {
                        if (pawn == null) continue;
                        if (!DLCCompat.IsGhoul(pawn)) continue;
                        if (pawn.Faction == null || !pawn.Faction.IsPlayer) continue;
                        buffer.Add(pawn);
                    }
                }
            }

            // 奴隶与囚犯：用 PawnsFinder 集合覆盖远行队/运输中的成员（不止地图上已生成）
            if (slavesPrisoners)
            {
                foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_SlavesOfColony)
                {
                    if (pawn != null) buffer.Add(pawn);
                }
                foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_PrisonersOfColony)
                {
                    if (pawn != null) buffer.Add(pawn);
                }
            }

            // 访问者：地图上其他派系、处于 Guest 状态的人形（商队/访客）
            // 排除玩家派系：任务临时成员等玩家侧 Guest 若属自由殖民者已在上面收集，避免重复
            if (visitors)
            {
                foreach (Pawn pawn in PawnsFinder.AllMaps_Spawned)
                {
                    if (pawn == null) continue;
                    if (pawn.Faction == null || pawn.Faction.IsPlayer) continue;
                    if (pawn.GuestStatus != GuestStatus.Guest) continue;
                    if (!PawnSuitabilityChecker.CanManageGear(pawn)) continue;
                    buffer.Add(pawn);
                }
            }
        }
    }
}
