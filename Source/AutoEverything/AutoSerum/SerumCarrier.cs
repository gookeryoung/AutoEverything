using System;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using AutoEverything.Core;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.AutoSerum
{
    /// <summary>
    /// 子功能1：血清库存携带。
    ///
    /// 设计目的：
    /// - 医生角色（Role.Doctor）或高评级战斗员（CombatTier >= S）库存携带 MechSerumHealer（治愈血清）备用
    /// - 携带数量 1 件（治愈血清稀有，避免一人囤积）
    /// - 当该 Pawn 重伤时，HealerSerumAllocator 会触发其库存中的血清自注射
    ///
    /// 模式：per-pawn 处理（参考 MealCarrier/MedicationAllocator），不周期门控，
    /// 由 SerumAllocator.RunAllPawns 控制 3000 tick 周期；
    /// 内部加 2500 tick 拾取冷却，避免反复拾取同一物品。
    ///
    /// 医疗守卫：拾取会 TryTakeOrderedJob 取消当前 Job，征召中/医疗 Job 中/床上休养 均跳过。
    /// </summary>
    public static class SerumCarrier
    {
        // 错误去重 salt：与 HealerSerumAllocator/BoostSerumAllocator/SerumAllocator 区分
        private const int CarrierErrorSalt = 0xA810;

        // 拾取冷却：避免反复拾取——与 MealCarrier/MedicationAllocator 一致
        private const int PickupCooldown = 2500;
        private static int lastPickupTick = -9999;

        // MechSerumHealer ThingDef 懒加载缓存
        // 规则：禁止在静态字段初始化器中调用 DefDatabase（跨线程风险），用懒加载属性
        // MechSerumHealer 是 Core 血清，无需 ModsConfig.AnomalyActive
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

        /// <summary>
        /// 单 Pawn 入口：医生/高评级战斗员若库存无治愈血清则拾取 1 件。
        /// 受 AESettings.autoCarryHealerSerum 子开关控制。
        /// </summary>
        public static void UpdateForPawn(Pawn pawn)
        {
            if (!AESettings.autoCarryHealerSerum) return;
            if (pawn == null || pawn.Dead || pawn.Downed || pawn.Map == null) return;

            // 仅医生或高评级（>= S）战斗员携带治愈血清（稀有资源，优先给关键人员）
            if (!IsSerumCarrierCandidate(pawn)) return;

            // 冷却检查：避免反复拾取（与 MealCarrier.PickupCooldown 一致）
            if (Find.TickManager.TicksGame - lastPickupTick < PickupCooldown) return;

            // 医疗守卫：拾取 Job 会 TryTakeOrderedJob 取消当前 Job，
            // 医疗 Job 中/床上休养/征召中 均不打断
            if (PawnJobGuard.ShouldSkipForMedical(pawn)) return;
            if (pawn.Drafted) return;

            try
            {
                ThingDef serumDef = HealerSerumDef;
                if (serumDef == null)
                {
                    // 治愈血清 Def 缺失（理论上不会发生，Core Def），静默跳过
                    return;
                }

                // 检查库存已有数量：>=1 则跳过（携带 1 件备用即可）
                int haveCount = CountInInventory(pawn, serumDef);
                if (haveCount >= 1)
                {
                    if (AEDebug.IsActive)
                        AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(pawn)} 库存已有治愈血清 x{haveCount}");
                    return;
                }

                // 找地图上最近的可用治愈血清（参考 MealCarrier/FindPenoxycylineOnMap）
                Thing serum = GenClosest.ClosestThingReachable(
                    pawn.Position, pawn.Map,
                    ThingRequest.ForDef(serumDef),
                    PathEndMode.ClosestTouch,
                    TraverseParms.For(pawn),
                    50f,
                    t => !t.IsForbidden(pawn) && pawn.CanReserve(t) && t.stackCount > 0);

                if (serum == null)
                {
                    if (AEDebug.IsActive)
                        AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(pawn)} 需治愈血清但地图无可用");
                    return;
                }

                AssignPickupJob(pawn, serum);
                lastPickupTick = Find.TickManager.TicksGame;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] 自动携带治愈血清失败 " + pawn.LabelShort + ": " + ex.Message,
                    pawn.thingIDNumber ^ CarrierErrorSalt);
            }
        }

        /// <summary>
        /// 判断 Pawn 是否适合携带治愈血清：医生角色 或 高评级战斗员（>= S）。
        /// 设计意图：治愈血清稀有，优先配备给关键人员——
        ///   医生常在战场救援，高评级战斗员是主力输出，重伤自注射可救命。
        /// </summary>
        private static bool IsSerumCarrierCandidate(Pawn pawn)
        {
            Role role = RoleDetector.DetectRole(pawn);
            if (role == Role.Doctor) return true;

            CombatTier tier = CombatEvaluator.GetCombatTier(pawn);
            return tier >= CombatTier.S;
        }

        /// <summary>
        /// 统计 Pawn 库存中指定 ThingDef 的总堆叠数。
        /// Tick 路径禁止 LINQ，用 foreach + 累加。
        /// </summary>
        private static int CountInInventory(Pawn pawn, ThingDef def)
        {
            int count = 0;
            foreach (Thing item in pawn.inventory.innerContainer)
            {
                if (item.def == def) count += item.stackCount;
            }
            return count;
        }

        /// <summary>
        /// 安排拾取治愈血清的 Job：JobDefOf.TakeCountToInventory。
        /// count 固定 1（治愈血清稀有，1 件应急即可）。
        /// </summary>
        private static void AssignPickupJob(Pawn pawn, Thing serum)
        {
            // 双重医疗守卫：安排前再检查一次
            if (PawnJobGuard.ShouldSkipForMedical(pawn)) return;

            Job job = JobMaker.MakeJob(JobDefOf.TakeCountToInventory, serum);
            job.count = Mathf.Min(1, serum.stackCount);
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);

            if (AEDebug.IsActive)
                AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(pawn)} 拾取治愈血清 x{job.count} (来源={serum.LabelShort})");
        }
    }
}
