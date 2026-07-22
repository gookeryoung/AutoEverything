using System.Collections.Generic;
using RimWorld;
using Verse;
using AutoEverything.Core;

namespace AutoEverything.AutoEquipment
{
    /// <summary>
    /// 装备库存与候选 Pawn 收集器：为 GearAllocator 提供输入数据。
    ///
    /// 候选装备来源：
    /// - 地图上所有未穿戴的 Apparel（ThingRequestGroup.Apparel，所有玩家可见地图）
    /// - 玩家阵营 Pawn 已穿戴的 Apparel（参与重分配时被纳入候选池）
    ///
    /// 候选 Pawn 范围：
    /// - 玩家阵营自由殖民者
    /// - 玩家阵营奴隶（Ideology DLC）
    /// - 食尸鬼不参与（无法穿戴 apparel）
    ///
    /// 注：返回的 List 为内部复用缓冲区，调用方使用完毕前不可再次调用本类方法。
    /// </summary>
    internal static class GearInventoryService
    {
        // 复用缓冲区：Tick 路径避免 new List<>()
        private static readonly List<Apparel> candidateApparelBuffer = new List<Apparel>();
        private static readonly List<Pawn> candidatePawnBuffer = new List<Pawn>();
        // 已分配 apparel ID 集合：本轮分配中已被某 Pawn 占用的 apparel，避免重复分配
        private static readonly HashSet<int> allocatedApparelIds = new HashSet<int>();
        // 本轮候选收集遇到的 Forbidden 装备数量（无论是否自动取消都统计，用于诊断日志）
        private static int statsForbiddenEncountered;

        /// <summary>
        /// 清空本轮分配状态：每次 GearAllocator 开始新一轮全局分配时调用。
        /// </summary>
        public static void ResetAllocation()
        {
            candidateApparelBuffer.Clear();
            candidatePawnBuffer.Clear();
            allocatedApparelIds.Clear();
            statsForbiddenEncountered = 0;
        }

        /// <summary>
        /// 本轮候选收集遇到的 Forbidden 装备数量（被跳过或被自动取消）。
        /// 用于 GearAllocator 调试日志，帮助玩家诊断"地图上有 N 件装备被 Forbid 跳过"。
        /// </summary>
        public static int StatsForbiddenEncountered => statsForbiddenEncountered;

        /// <summary>
        /// 收集所有候选装备：地图上未穿戴的 Apparel + 玩家阵营 Pawn 已穿戴的 Apparel。
        /// 过滤：通过 ApparelLayerFilter.IsRelevant 排除附件层（腰带/背包等）。
        /// 过滤：Forbidden 装备默认跳过；若 geAutoUnforbidApparel=true 则自动取消 Forbid 标记后纳入候选。
        ///
        /// 设计：接受外部传入的 candidatePawns 列表，避免内部重复调用 CollectCandidatePawns
        /// 导致缓冲区翻倍或浪费 CPU（曾因内部调用 + 外部调用两次填充导致翻倍 bug）。
        /// </summary>
        public static List<Apparel> CollectCandidateApparel(List<Pawn> candidatePawns)
        {
            // 地图上未穿戴的 Apparel
            foreach (Map map in Find.Maps)
            {
                if (map == null) continue;
                List<Thing> things = map.listerThings.ThingsInGroup(ThingRequestGroup.Apparel);
                for (int i = 0; i < things.Count; i++)
                {
                    Apparel apparel = things[i] as Apparel;
                    if (apparel == null) continue;
                    if (!apparel.Spawned) continue;
                    // Forbidden 处理：默认跳过，geAutoUnforbidApparel=true 时自动取消 Forbid 标记
                    // 统计：无论是否自动取消都累加 statsForbiddenEncountered，用于诊断日志
                    if (IsForbidden(apparel))
                    {
                        statsForbiddenEncountered++;
                        if (!AESettings.geAutoUnforbidApparel) continue;
                        // 自动取消 Forbid 标记，让系统可以选用被禁用的装备
                        CompForbiddable forbidComp = apparel.GetComp<CompForbiddable>();
                        if (forbidComp != null) forbidComp.Forbidden = false;
                    }
                    if (!ApparelLayerFilter.IsRelevant(apparel)) continue;
                    candidateApparelBuffer.Add(apparel);
                }
            }

            // 玩家阵营 Pawn 已穿戴的 Apparel（参与全局重分配）
            for (int i = 0; i < candidatePawns.Count; i++)
            {
                Pawn pawn = candidatePawns[i];
                if (pawn.apparel == null) continue;
                List<Apparel> worn = pawn.apparel.WornApparel;
                for (int j = 0; j < worn.Count; j++)
                {
                    Apparel apparel = worn[j];
                    if (apparel == null) continue;
                    if (!ApparelLayerFilter.IsRelevant(apparel)) continue;
                    candidateApparelBuffer.Add(apparel);
                }
            }

            return candidateApparelBuffer;
        }

        /// <summary>
        /// 收集参与分配的 Pawn：玩家阵营自由殖民者 + 玩家阵营奴隶。
        /// 排除：食尸鬼（无法穿戴 apparel）、动物、机械族等。
        /// 排除：医疗中/休养中的 Pawn（扒装会取消 Job，打断手术/治疗/休养，与 WorkAllocator 保持一致）。
        ///
        /// 防御性设计：开头 Clear 缓冲区，即使重复调用也不会翻倍。
        /// 正常调用路径：ExecuteAllocation 调用一次，传给 CollectCandidateApparel 复用。
        /// </summary>
        public static List<Pawn> CollectCandidatePawns()
        {
            // 防御性 Clear：避免重复调用导致缓冲区翻倍
            candidatePawnBuffer.Clear();

            // 自由殖民者（PawnsFinder.AllMaps_FreeColonists 含未 Spawned 的）
            foreach (Pawn pawn in PawnsFinder.AllMaps_FreeColonists)
            {
                if (pawn == null) continue;
                if (!pawn.Spawned) continue;
                if (DLCCompat.IsGhoul(pawn)) continue; // 食尸鬼不参与装备分配
                if (!PawnSuitabilityChecker.CanManageGear(pawn)) continue;
                // 医疗守卫：扒装 TryDrop/Wear 会取消当前 Job，打断手术/治疗/休养
                if (PawnJobGuard.ShouldSkipForMedical(pawn)) continue;
                candidatePawnBuffer.Add(pawn);
            }

            // 玩家阵营奴隶（Ideology DLC）
            if (ModsConfig.IdeologyActive)
            {
                foreach (Map map in Find.Maps)
                {
                    if (map == null || map.mapPawns == null) continue;
                    IReadOnlyList<Pawn> allPawns = map.mapPawns.AllPawnsSpawned;
                    for (int i = 0; i < allPawns.Count; i++)
                    {
                        Pawn pawn = allPawns[i];
                        if (pawn == null) continue;
                        if (!pawn.IsSlaveOfColony) continue;
                        if (DLCCompat.IsGhoul(pawn)) continue;
                        if (!PawnSuitabilityChecker.CanManageGear(pawn)) continue;
                        // 医疗守卫：与殖民者一致，奴隶医疗中也不参与装备分配
                        if (PawnJobGuard.ShouldSkipForMedical(pawn)) continue;
                        candidatePawnBuffer.Add(pawn);
                    }
                }
            }

            return candidatePawnBuffer;
        }

        /// <summary>
        /// 标记 apparel 已被分配（占用），本轮内不再分给其他 Pawn。
        /// </summary>
        public static void MarkAllocated(Apparel apparel)
        {
            if (apparel == null) return;
            allocatedApparelIds.Add(apparel.thingIDNumber);
        }

        /// <summary>
        /// 检查 apparel 是否已被分配。
        /// </summary>
        public static bool IsAllocated(Apparel apparel)
        {
            return apparel != null && allocatedApparelIds.Contains(apparel.thingIDNumber);
        }

        /// <summary>
        /// 检查 apparel 是否被玩家标记为"禁用"。
        /// RimWorld 1.6 中 Thing 没有公共 Forbidden 属性，通过 CompForbiddable 组件查询。
        /// 无 CompForbiddable 的 apparel 视为未禁用。
        /// </summary>
        private static bool IsForbidden(Apparel apparel)
        {
            if (apparel == null) return false;
            CompForbiddable forbidComp = apparel.GetComp<CompForbiddable>();
            return forbidComp != null && forbidComp.Forbidden;
        }
    }
}
