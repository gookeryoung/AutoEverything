using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using AutoEverything.Core;

namespace AutoEverything.AutoDrug
{
    /// <summary>
    /// 子功能4：自动预防性服药（penoxycyline 防疟疾/瘟疫/嗜睡病）。
    ///
    /// 设计目的：
    /// - Pawn 无 PenoxycylineHigh hediff（药效已过）→ 安排 Ingest Job 服用 penoxycyline
    /// - Pawn 库存有 penoxycyline：直接 Ingest 库存中的药品
    /// - Pawn 库存无 penoxycyline：找地图上最近的 penoxycyline 安排 Ingest（Pawn 会自动取用）
    ///
    /// penoxycyline 服用间隔（RimWorld 原生）：5.5 天 = 330000 tick
    /// 本模块通过 hediff 检测药效，不依赖固定间隔，更准确。
    ///
    /// 模式：per-pawn 处理（不周期门控，由 DrugAllocator.RunAllPawns 控制 3000 tick 周期）。
    /// 医疗守卫：服药前走 PawnJobGuard.ShouldSkipForMedical。
    /// </summary>
    public static class MedicationAllocator
    {
        // 错误去重 salt：与 DrugPolicyManager/TreatmentAllocator 区分
        private const int MedicationErrorSalt = 0xA620;

        // penoxycyline ThingDef 懒加载缓存
        // 规则：禁止在静态字段初始化器中调用 DefDatabase（跨线程风险），用懒加载属性
        private static ThingDef cachedPenoxycylineDef;
        private static bool penoxycylineResolved = false;

        private static ThingDef PenoxycylineDef
        {
            get
            {
                if (!penoxycylineResolved)
                {
                    penoxycylineResolved = true;
                    cachedPenoxycylineDef = DefDatabase<ThingDef>.GetNamed("Penoxycyline", false);
                }
                return cachedPenoxycylineDef;
            }
        }

        // PenoxycylineHigh HediffDef 懒加载缓存
        // 检测药效：hediff 存在且 Severity > 0 表示药效还在
        private static HediffDef cachedPenoxycylineHighDef;
        private static bool penoxycylineHighResolved = false;

        private static HediffDef PenoxycylineHighDef
        {
            get
            {
                if (!penoxycylineHighResolved)
                {
                    penoxycylineHighResolved = true;
                    cachedPenoxycylineHighDef = DefDatabase<HediffDef>.GetNamed("PenoxycylineHigh", false);
                }
                return cachedPenoxycylineHighDef;
            }
        }

        // 拾取冷却：避免反复拾取药品——与 EvaluateInventory 一致
        private const int PickupCooldown = 2500;
        private static int lastMedicationTick = -9999;

        /// <summary>
        /// 单 Pawn 入口：检查 Pawn 是否需要 penoxycyline，需要则安排 Ingest Job。
        /// 受 AESettings.autoMedication 子开关控制。
        /// </summary>
        public static void AllocateForPawn(Pawn pawn)
        {
            if (!AESettings.autoMedication) return;
            if (pawn == null || pawn.Dead || pawn.Downed || pawn.Map == null) return;

            // 防止反复拾取——冷却 2500 tick
            if (Find.TickManager.TicksGame - lastMedicationTick < PickupCooldown) return;

            // 医疗守卫：服药 Job 会 TryTakeOrderedJob 取消当前 Job
            // 正在执行 TendPatient/DoBill/床上休养 时跳过，避免打断治疗
            if (PawnJobGuard.ShouldSkipForMedical(pawn)) return;
            // 征召中不打断战斗
            if (pawn.Drafted) return;

            try
            {
                ThingDef penoxy = PenoxycylineDef;
                HediffDef penoxyHigh = PenoxycylineHighDef;
                if (penoxy == null || penoxyHigh == null) return;

                // 检测药效：已有 PenoxycylineHigh hediff 则跳过（药效还在）
                Hediff existing = pawn.health?.hediffSet?.GetFirstHediffOfDef(penoxyHigh);
                if (existing != null && existing.Severity > 0f)
                {
                    if (AEDebug.IsActive)
                        AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(pawn)} penoxycyline 药效仍在 (severity={existing.Severity:F2})");
                    return;
                }

                // 优先服用库存中的 penoxycyline；无则找地图上最近的
                Thing target = FindPenoxycylineInInventory(pawn, penoxy);
                if (target == null)
                {
                    target = FindPenoxycylineOnMap(pawn, penoxy);
                }

                if (target == null)
                {
                    if (AEDebug.IsActive)
                        AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(pawn)} 需 penoxycyline 但库存与地图均无可用");
                    return;
                }

                AssignIngestJob(pawn, target);
                lastMedicationTick = Find.TickManager.TicksGame;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] 自动服药失败 " + pawn.LabelShort + ": " + ex.Message,
                    pawn.thingIDNumber ^ MedicationErrorSalt);
            }
        }

        /// <summary>
        /// 在 Pawn 库存中查找 penoxycyline。
        /// </summary>
        private static Thing FindPenoxycylineInInventory(Pawn pawn, ThingDef penoxyDef)
        {
            foreach (Thing item in pawn.inventory.innerContainer)
            {
                if (item.def == penoxyDef && item.stackCount > 0) return item;
            }
            return null;
        }

        /// <summary>
        /// 在地图上找最近的可用 penoxycyline（未禁用、可储备）。
        /// 用 GenClosest.ClosestThingReachable，避免 LINQ。
        /// </summary>
        private static Thing FindPenoxycylineOnMap(Pawn pawn, ThingDef penoxyDef)
        {
            return GenClosest.ClosestThingReachable(
                pawn.Position, pawn.Map,
                ThingRequest.ForDef(penoxyDef),
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn),
                50f,
                t => !t.IsForbidden(pawn) && pawn.CanReserve(t) && t.stackCount > 0);
        }

        /// <summary>
        /// 安排服用 penoxycyline 的 Job。
        /// JobDefOf.Ingest 让 Pawn 自动取用并服用 Thing。
        /// </summary>
        private static void AssignIngestJob(Pawn pawn, Thing penoxy)
        {
            // 双重医疗守卫：安排前再检查一次
            if (PawnJobGuard.ShouldSkipForMedical(pawn)) return;

            Job job = JobMaker.MakeJob(JobDefOf.Ingest, penoxy);
            job.count = 1;
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);

            if (AEDebug.IsActive)
                AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(pawn)} 服用 penoxycyline (来源={penoxy.LabelShort})");
        }
    }
}
