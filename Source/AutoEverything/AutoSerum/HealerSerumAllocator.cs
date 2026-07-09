using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using AutoEverything.Core;

namespace AutoEverything.AutoSerum
{
    /// <summary>
    /// 子功能2：重伤员自动注射 MechSerumHealer 治愈最严重伤情。
    ///
    /// 设计目的：
    /// - 全局扫描重伤员（SummaryHealthPercent &lt; 0.3 且 ShouldBeTendedNowByPlayer）
    /// - 重伤员若库存有 MechSerumHealer，则自注射（JobDefOf.UseItem，targetA=血清）
    /// - MechSerumHealer 的 CompUseEffect_FixWorstHealthCondition 会自动治愈最严重伤情
    ///
    /// 血清来源：仅限伤员自身库存。
    /// 设计取舍（简化实现）：
    /// - 不走医生手术给药（AdministerMechSerumHealer recipe），避免复杂手术流程
    /// - 不让伤员走去地图拾取（重伤员行走风险大，且打断休养）
    /// - 重伤员若曾携带治愈血清（由 SerumCarrier 给医生/高评级战斗员配备），自注射救命
    ///   未携带血清的伤员依赖玩家手动或原生医疗流程
    ///
    /// 医疗守卫（关键，参考 FeedingAllocator）：
    /// - 伤员本身在床上休养（IsRecoveringInBed=true），若应用 ShouldSkipForMedical 会永真跳过
    /// - 注射血清是救命操作，UseItem Job 短暂（600 tick = 10 秒），可接受打断 LayDown
    /// - 故对伤员不应用守卫，仅跳过 Downed（无法执行 Job）与征召中（玩家控制）
    ///
    /// 模式：参考 TreatmentAllocator/FeedingAllocator——AllocateForPawn(pawn) 内部周期门控（3000 tick），
    /// 第一个触发的 Pawn 承担全局扫描成本。
    /// </summary>
    public static class HealerSerumAllocator
    {
        // 全局分配间隔：与 SerumAllocator 一致（3000 tick ≈ 50 秒）
        // 治愈血清稀有，非紧急到秒级，原生医疗流程会处理常规治疗
        private const int AllocationInterval = 3000;
        private static int lastAllocationTick = -9999;

        // 候选缓存（Tick 路径禁止 new List，复用静态字段）
        private static readonly List<Pawn> woundedCandidates = new List<Pawn>();

        // 错误去重 salt：与 SerumCarrier/BoostSerumAllocator/SerumAllocator 区分
        private const int HealerErrorSalt = 0xA820;

        // 伤员血量阈值：SummaryHealthPercent < 30% 视为重伤，需注射治愈血清
        private const float CriticalHealthPercent = 0.3f;

        // MechSerumHealer ThingDef 懒加载缓存（Core 血清，无需 DLC 检查）
        private static ThingDef cachedHealerSerumDef;
        private static bool healerSerumResolved = false;

        private static ThingDef HealerSerumDef
        {
            get
            {
                if (!healerSerumResolved)
                {
                    healerSerumResolved = true;
                    cachedHealerSerumDef = DefDatabase<ThingDef>.GetNamed("MechSerumHealer", false);
                }
                return cachedHealerSerumDef;
            }
        }

        // UseItem JobDef 懒加载缓存
        // 注：JobDefOf 无 UseItem 静态字段（1.6 DefOf 未生成），用 DefDatabase 按名查找
        // defName="UseItem"（见 Core/Defs/JobDefs/Jobs_Misc.xml），driverClass=JobDriver_UseItem
        // MechSerumHealer 的 CompProperties_Usable.useJob=UseItem，故自注射用此 JobDef
        private static JobDef cachedUseItemDef;
        private static bool useItemDefResolved = false;

        private static JobDef UseItemDef
        {
            get
            {
                if (!useItemDefResolved)
                {
                    useItemDefResolved = true;
                    cachedUseItemDef = DefDatabase<JobDef>.GetNamed("UseItem", false);
                }
                return cachedUseItemDef;
            }
        }

        /// <summary>
        /// 单 Pawn 入口：受全局周期控制，仅当距离上次全局分配超过 AllocationInterval 时才执行。
        /// 第一个触发的 Pawn 承担全局扫描成本，其余 Pawn 跳过。
        /// </summary>
        public static void AllocateForPawn(Pawn pawn)
        {
            int tick = Find.TickManager.TicksGame;
            if (tick - lastAllocationTick < AllocationInterval) return;
            lastAllocationTick = tick;

            AllocateAllColonists();
        }

        /// <summary>
        /// 全局扫描：收集所有重伤员，逐个检查库存是否有治愈血清，有则自注射。
        /// try-catch 隔离：失败时 Log.ErrorOnce 记录，不影响其他 Pawn 评估。
        /// </summary>
        private static void AllocateAllColonists()
        {
            if (!AESettings.autoHealerSerum) return;

            try
            {
                ThingDef serumDef = HealerSerumDef;
                JobDef useItemDef = UseItemDef;
                if (serumDef == null || useItemDef == null) return;

                woundedCandidates.Clear();

                foreach (Map map in Find.Maps)
                {
                    if (map?.mapPawns == null) continue;
                    foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
                    {
                        if (pawn == null) continue;
                        if (DLCCompat.IsGhoul(pawn)) continue;
                        if (!PawnSuitabilityChecker.CanManageGear(pawn)) continue;
                        if (pawn.Dead) continue;
                        // Downed 的伤员无法执行 Job（自注射需 Pawn 能动作），跳过
                        // Downed 伤员依赖玩家手动手术给药（AdministerMechSerumHealer）
                        if (pawn.Downed) continue;

                        // 重伤候选：血量 < 30% 且需立即治疗
                        // 注：不应用 ShouldSkipForMedical——伤员在床上休养（IsRecoveringInBed=true）
                        //   若应用守卫会永真跳过。UseItem Job 短暂，可接受打断 LayDown 救命（参考 FeedingAllocator）
                        float healthPct = pawn.health?.summaryHealth?.SummaryHealthPercent ?? 1f;
                        if (healthPct >= CriticalHealthPercent) continue;
                        if (!HealthAIUtility.ShouldBeTendedNowByPlayer(pawn)) continue;

                        woundedCandidates.Add(pawn);
                    }
                }

                if (woundedCandidates.Count == 0) return;

                // 逐个伤员检查库存血清并自注射
                for (int i = 0; i < woundedCandidates.Count; i++)
                {
                    Pawn patient = woundedCandidates[i];
                    TrySelfInject(patient, serumDef, useItemDef);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] 自动治愈血清注射失败: " + ex.Message, HealerErrorSalt);
            }
        }

        /// <summary>
        /// 检查伤员库存是否有 MechSerumHealer，有则安排自注射 Job。
        /// UseItem Job：targetA=血清 Thing，Pawn 对自身使用。
        /// MechSerumHealer 无 CompProperties_Targetable（仅 MechSerumResurrector 有），
        /// 故 UseItem 默认对自身使用，无需选目标。
        /// </summary>
        private static void TrySelfInject(Pawn patient, ThingDef serumDef, JobDef useItemDef)
        {
            // 征召中的伤员由玩家控制，不打断（玩家可手动使用血清）
            if (patient.Drafted) return;

            Thing serum = FindSerumInInventory(patient, serumDef);
            if (serum == null)
            {
                if (AEDebug.IsActive)
                    AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(patient)} 重伤但库存无治愈血清 (hp={patient.health?.summaryHealth?.SummaryHealthPercent:F2})");
                return;
            }

            // 不应用 ShouldSkipForMedical：伤员在床上休养时守卫永真，救命优先
            Job job = JobMaker.MakeJob(useItemDef, serum);
            job.count = 1;
            patient.jobs.TryTakeOrderedJob(job, JobTag.Misc);

            if (AEDebug.IsActive)
                AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(patient)} 自注射治愈血清 (hp={patient.health?.summaryHealth?.SummaryHealthPercent:F2}, 来源={serum.LabelShort})");
        }

        /// <summary>
        /// 在 Pawn 库存中查找 MechSerumHealer。
        /// </summary>
        private static Thing FindSerumInInventory(Pawn pawn, ThingDef serumDef)
        {
            foreach (Thing item in pawn.inventory.innerContainer)
            {
                if (item.def == serumDef && item.stackCount > 0) return item;
            }
            return null;
        }
    }
}
