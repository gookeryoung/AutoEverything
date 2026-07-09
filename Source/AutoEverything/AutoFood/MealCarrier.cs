using System;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using AutoEverything.Core;

namespace AutoEverything.AutoFood
{
    /// <summary>
    /// 子功能1：自动携带行军口粮。
    ///
    /// 设计目的：
    /// - 征召中的 Pawn（远征/战斗）库存无行军口粮时，自动拾取地图上最近的行军口粮
    /// - 数量由 AESettings.carryMealCount 控制（默认 1 份应急）
    /// - 行军口粮 defName = MealSurvivalPack（RimWorld 1.6 实际 defName，非 wiki 标签的 "packaged survival meal"）
    ///
    /// 模式：per-pawn 处理（参考 MedicationAllocator），不周期门控，
    /// 由 FoodAllocator.RunAllPawns 控制 3000 tick 周期；
    /// 内部加 2500 tick 拾取冷却，避免反复拾取同一物品。
    ///
    /// 医疗守卫：拾取会 TryTakeOrderedJob 取消当前 Job，征召中仍跳过医疗 Job 中的 Pawn
    /// （虽征召中的 Pawn 鲜少在医疗 Job，但仍保留守卫防止边角情况）。
    /// </summary>
    public static class MealCarrier
    {
        // 错误去重 salt：与 FeedingAllocator/FoodRestrictionManager/FoodAllocator 区分
        private const int MealErrorSalt = 0xA710;

        // 拾取冷却：避免反复拾取——与 MedicationAllocator.PickupCooldown 一致
        private const int PickupCooldown = 2500;
        private static int lastPickupTick = -9999;

        // 行军口粮 ThingDef 懒加载缓存
        // 规则：禁止在静态字段初始化器中调用 DefDatabase（跨线程风险），用懒加载属性
        // defName = "MealSurvivalPack"（RimWorld 1.6 原生 defName）
        private static ThingDef cachedMealDef;
        private static bool mealResolved = false;

        private static ThingDef MealSurvivalPackDef
        {
            get
            {
                if (!mealResolved)
                {
                    mealResolved = true;
                    cachedMealDef = DefDatabase<ThingDef>.GetNamed("MealSurvivalPack", false);
                }
                return cachedMealDef;
            }
        }

        /// <summary>
        /// 单 Pawn 入口：征召中的 Pawn 若库存不足行军口粮则拾取。
        /// 受 AESettings.autoCarryMeal 子开关控制。
        /// </summary>
        public static void UpdateForPawn(Pawn pawn)
        {
            if (!AESettings.autoCarryMeal) return;
            if (pawn == null || pawn.Dead || pawn.Downed || pawn.Map == null) return;

            // 仅征召中的 Pawn 携带行军口粮（远征/战斗应急）
            if (!pawn.Drafted) return;

            // 冷却检查：避免反复拾取（与 MedicationAllocator.PickupCooldown 一致）
            if (Find.TickManager.TicksGame - lastPickupTick < PickupCooldown) return;

            // 医疗守卫：拾取 Job 会 TryTakeOrderedJob 取消当前 Job，
            // 虽征召中的 Pawn 鲜少在医疗 Job，仍守卫防止边角情况打断
            if (PawnJobGuard.ShouldSkipForMedical(pawn)) return;

            try
            {
                ThingDef mealDef = MealSurvivalPackDef;
                if (mealDef == null)
                {
                    // 行军口粮 Def 缺失（理论上不会发生，原生 Def），静默跳过
                    return;
                }

                int targetCount = AESettings.carryMealCount;
                if (targetCount < 1) targetCount = 1;

                // 检查库存已有数量：库存足够则跳过
                int haveCount = CountInInventory(pawn, mealDef);
                if (haveCount >= targetCount)
                {
                    if (AEDebug.IsActive)
                        AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(pawn)} 征召库存已有行军口粮 x{haveCount} (target={targetCount})");
                    return;
                }

                int need = targetCount - haveCount;

                // 找地图上最近的可用行军口粮（参考 MedicationAllocator.FindPenoxycylineOnMap）
                Thing food = GenClosest.ClosestThingReachable(
                    pawn.Position, pawn.Map,
                    ThingRequest.ForDef(mealDef),
                    PathEndMode.ClosestTouch,
                    TraverseParms.For(pawn),
                    50f,
                    t => !t.IsForbidden(pawn) && pawn.CanReserve(t) && t.stackCount > 0);

                if (food == null)
                {
                    if (AEDebug.IsActive)
                        AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(pawn)} 征召需行军口粮但地图无可用");
                    return;
                }

                AssignPickupJob(pawn, food, need);
                lastPickupTick = Find.TickManager.TicksGame;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] 自动携带口粮失败 " + pawn.LabelShort + ": " + ex.Message,
                    pawn.thingIDNumber ^ MealErrorSalt);
            }
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
        /// 安排拾取行军口粮的 Job：JobDefOf.TakeCountToInventory。
        /// count 取 need 与目标堆叠数的较小值，避免取超过实际可用。
        /// </summary>
        private static void AssignPickupJob(Pawn pawn, Thing food, int need)
        {
            // 双重医疗守卫：安排前再检查一次
            if (PawnJobGuard.ShouldSkipForMedical(pawn)) return;

            Job job = JobMaker.MakeJob(JobDefOf.TakeCountToInventory, food);
            job.count = Mathf.Min(need, food.stackCount);
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);

            if (AEDebug.IsActive)
                AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(pawn)} 征召拾取行军口粮 x{job.count} (来源={food.LabelShort})");
        }
    }
}
