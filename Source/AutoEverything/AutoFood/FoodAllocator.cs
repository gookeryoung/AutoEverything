using System;
using RimWorld;
using Verse;
using AutoEverything.Core;

namespace AutoEverything.AutoFood
{
    /// <summary>
    /// 自动食物模块主调度器：周期触发并统一调度 3 个子功能。
    ///
    /// 设计模式：参考 DrugAllocator 的"周期门控 + 第一个 Pawn 承担全局扫描"模式。
    /// - AllocateForPawn(pawn)：周期 3000 tick 门控，触发后扫描所有地图殖民者
    /// - ReallocateAll()：ITab / AutoExecutor 触发，立即执行（绕过周期门控）
    ///
    /// 子功能：
    /// 1. MealCarrier：征召中的 Pawn 自动拾取行军口粮（per-pawn，自带 2500 tick 拾取冷却）
    /// 2. FeedingAllocator：扫描卧床饥饿伤员安排医生喂食（全局扫描，自带 3000 tick 周期门控）
    /// 3. FoodRestrictionManager：按角色切换 FoodPolicy（伤员/医生 → 高营养限制）
    ///
    /// 调用入口：CompGearManager.CompTick 在 DrugAllocator.AllocateForPawn 后调用本类。
    /// </summary>
    public static class FoodAllocator
    {
        // 全局分配间隔：3000 tick ≈ 50 秒（与 AutoExecutor.ExecuteInterval 一致）
        // 食物分配非紧急到秒级，延迟可接受
        private const int AllocationInterval = 3000;
        private static int lastAllocationTick = -9999;

        // 错误去重 salt：与 AutoExecutor 各 ErrorSalt 区分
        private const int FoodErrorSalt = 0xA700;

        /// <summary>
        /// 单 Pawn 入口：受全局周期控制，仅当距离上次全局分配超过 AllocationInterval 时才执行。
        /// 第一个触发该周期的 Pawn 承担全局分配成本，其余 Pawn 跳过。
        /// 由 CompGearManager.CompTick 在 DrugAllocator.AllocateForPawn 后调用。
        /// </summary>
        public static void AllocateForPawn(Pawn pawn)
        {
            int tick = Find.TickManager.TicksGame;
            if (tick - lastAllocationTick < AllocationInterval) return;
            lastAllocationTick = tick;

            RunAllPawns();
        }

        /// <summary>
        /// 全局重配入口：ITab / AutoExecutor 触发时立即执行所有地图殖民者的食物配置。
        /// 重置周期门控，确保下一次 AllocateForPawn 不会立即触发重复执行。
        /// </summary>
        public static void ReallocateAll()
        {
            lastAllocationTick = Find.TickManager.TicksGame;
            RunAllPawns();
        }

        /// <summary>
        /// 遍历所有地图的殖民者，逐个调用 per-pawn 子功能。
        /// FeedingAllocator 是全局扫描（伤员+医生），自带周期门控，由第一个触发的 Pawn 承担扫描成本。
        /// 过滤链与 DrugAllocator 一致：食尸鬼/不适用/Dead/Downed 均排除。
        /// try-catch 隔离：失败时 Log.ErrorOnce 记录，不影响其他逻辑。
        /// </summary>
        private static void RunAllPawns()
        {
            if (!AESettings.autoFoodEnabled) return;

            try
            {
                foreach (Map map in Find.Maps)
                {
                    if (map?.mapPawns == null) continue;
                    foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
                    {
                        if (pawn == null) continue;
                        if (DLCCompat.IsGhoul(pawn)) continue;
                        if (!PawnSuitabilityChecker.CanManageGear(pawn)) continue;
                        if (pawn.Dead || pawn.Downed) continue;

                        // 各子功能内部检查子开关，关闭时直接 return
                        MealCarrier.UpdateForPawn(pawn);
                        FoodRestrictionManager.UpdateForPawn(pawn);
                        // FeedingAllocator 自带 3000 tick 周期门控，
                        // 内部判定后只承担一次全局扫描成本（与 TreatmentAllocator 模式一致）
                        FeedingAllocator.AllocateForPawn(pawn);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] 自动食物分配失败: " + ex.Message, FoodErrorSalt);
            }
        }
    }
}
