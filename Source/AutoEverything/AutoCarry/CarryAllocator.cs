using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;
using Verse.AI;
using AutoEverything.Core;

namespace AutoEverything.AutoCarry
{
    /// <summary>
    /// 携带分配器：周期性扫描自由殖民者的背包，
    /// 对缺物品的殖民者派发 TakeInventory Job 让其去仓库拾取。
    ///
    /// 触发方式：AutoExecutor 周期触发（6000 tick）+ ITab 勾选时立即触发。
    /// 战斗过滤：复用 AutoExecutor.AnyCombatActive()，战斗中暂停派发。
    /// 医疗守卫：复用 PawnJobGuard.ShouldSkipForMedical()，正在手术/休养的殖民者跳过。
    ///
    /// 设计原则：
    /// - 单次只派发一个 Job：TryTakeOrderedJob 会取消当前 Job，避免一次给 Pawn 派发多个 Job 互相覆盖
    /// - 一周期一物品：每个 Pawn 一周期内最多派发一个 TakeInventory Job，下周期再处理其他缺物品
    /// - 不强制 Spawned：卧床/运输中的殖民者已通过 ShouldSkipForCarry 过滤（医疗守卫）
    /// - 性能：复用静态缓冲区，Tick 路径禁止 LINQ
    /// </summary>
    public static class CarryAllocator
    {
        // 候选殖民者缓存（复用避免 GC）
        private static readonly List<Pawn> candidatePawns = new List<Pawn>();

        // 单 Pawn 携带物品清单缓存（每 Pawn 重新填充前 Clear）
        private static readonly List<CarryEntry> carryEntries = new List<CarryEntry>();

        // 错误去重 salt
        private const int CarryErrorSalt = 0xA400;

        /// <summary>
        /// 全局携带物品重配入口。
        /// 遍历所有自由殖民者，对缺物品者派发 TakeInventory Job。
        /// 返回受影响的殖民者数量（即派发了 Job 的 Pawn 数）。
        ///
        /// 调用方职责：战斗过滤（AnyCombatActive）由 AutoExecutor 在调用前判断。
        /// 本方法不再二次检查战斗状态——ITab 手动触发也应受战斗过滤约束（调用方控制）。
        /// </summary>
        public static int ReallocateAll()
        {
            // 1. 收集候选殖民者：仅自由殖民者（不含奴隶、不含食尸鬼）
            candidatePawns.Clear();
            foreach (Pawn pawn in PawnsFinder.AllMaps_FreeColonists)
            {
                if (pawn == null) continue;
                if (PawnCarryChecker.ShouldSkipForCarry(pawn)) continue;
                // 跨地图的殖民者（如乘穿梭机/运输舱中）无 Map 时跳过
                if (pawn.Map == null) continue;
                candidatePawns.Add(pawn);
            }

            if (candidatePawns.Count == 0) return 0;

            int affected = 0;
            for (int i = 0; i < candidatePawns.Count; i++)
            {
                Pawn pawn = candidatePawns[i];
                try
                {
                    if (TryDispatchCarryJob(pawn))
                    {
                        affected++;
                    }
                }
                catch (Exception ex)
                {
                    // 错误隔离：单 Pawn 失败不影响其他 Pawn
                    Log.ErrorOnce("[AutoEverything] 携带物品分配失败: " + ex.Message,
                        pawn.thingIDNumber ^ CarryErrorSalt);
                }
            }

            if (AEDebug.IsActive) AEDebug.Log(() =>
                $"[CarryAllocator] === ReallocateAll end (pawns={candidatePawns.Count}, affected={affected}) ===");

            return affected;
        }

        /// <summary>
        /// 为单个 Pawn 派发携带物品 Job。
        /// 遍历该 Pawn 应携带的物品清单，找到第一个缺物品且有仓库可拿的物品，派发 TakeInventory Job。
        /// 单次最多派发一个 Job（避免互相覆盖），下周期再处理其他物品。
        /// </summary>
        /// <returns>true 表示已派发 Job；false 表示无需派发（物品齐全或仓库无货）</returns>
        private static bool TryDispatchCarryJob(Pawn pawn)
        {
            carryEntries.Clear();
            CarryPolicy.FillCarryItems(pawn, carryEntries);

            for (int i = 0; i < carryEntries.Count; i++)
            {
                CarryEntry entry = carryEntries[i];
                ThingDef def = entry.Def;
                int target = entry.Count;

                // 计算当前 inventory 已有数量
                int current = CountInInventory(pawn, def);
                int needed = target - current;
                if (needed <= 0) continue;

                // 在地图上找该 ThingDef 最近的可用 Thing
                Thing target_thing = FindNearestAvailableThing(pawn, def);
                if (target_thing == null) continue;

                // 派发数量：取缺失量与目标 Thing 堆叠数的较小值
                int takeCount = Math.Min(needed, target_thing.stackCount);
                if (takeCount <= 0) continue;

                Job job = JobMaker.MakeJob(JobDefOf.TakeInventory, target_thing);
                job.count = takeCount;
                // JobTag.MiscWork：常规工作标签，可被高优先级 Job 打断
                bool ok = pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                if (ok)
                {
                    if (AEDebug.IsActive) AEDebug.Log(() =>
                        $"[CarryAllocator] {AEDebug.Label(pawn)} 派发 TakeInventory: {def.defName} x{takeCount}");
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 计算 Pawn inventory 中指定 ThingDef 的总堆叠数。
        /// 不用 LINQ 避免 Tick 路径 GC。
        /// </summary>
        private static int CountInInventory(Pawn pawn, ThingDef def)
        {
            int count = 0;
            ThingOwner<Thing> inventory = pawn.inventory?.innerContainer;
            if (inventory == null) return 0;
            for (int i = 0; i < inventory.Count; i++)
            {
                Thing t = inventory[i];
                if (t.def == def) count += t.stackCount;
            }
            return count;
        }

        /// <summary>
        /// 在 Pawn 所在地图上找该 ThingDef 最近的可用 Thing。
        /// 可用条件：Spawned &amp;&amp; !Forbidden &amp;&amp; !reservedByOther &amp;&amp; 在允许区域内。
        /// 不用 GenClosest.ClosestThingReachable（含路径检查开销大），
        /// 改用 listerThings.ThingsOfDef + 手动最近搜索，性能更可控。
        /// </summary>
        private static Thing FindNearestAvailableThing(Pawn pawn, ThingDef def)
        {
            Map map = pawn.Map;
            if (map == null) return null;

            List<Thing> things = map.listerThings.ThingsOfDef(def);
            if (things.Count == 0) return null;

            // 允许区域（安全区）：玩家划定允许区域后，殖民者只应在区域内活动。
            // EffectiveAreaRestrictionInPawnCurrentMap 已处理地图切换等边缘情况；null 表示未限制。
            // 区域外的物品（如战场/危险区）不可作为拾取目标。
            Area allowedArea = pawn.playerSettings?.EffectiveAreaRestrictionInPawnCurrentMap;

            Thing nearest = null;
            float nearestDistSq = float.MaxValue;
            IntVec3 pawnPos = pawn.Position;

            for (int i = 0; i < things.Count; i++)
            {
                Thing t = things[i];
                if (!t.Spawned) continue;
                if (t.IsForbidden(pawn)) continue;
                // 跳过已被他人预约的（避免抢夺他人 Job 目标）
                // map.reservationManager.CanReserve 检查目标是否可被当前 Pawn 预约（含他人预约检查）
                if (!map.reservationManager.CanReserve(pawn, t, 1, -1, null, false)) continue;
                // 允许区域外（危险区）的物品跳过，防止殖民者跑出安全区拾取
                if (allowedArea != null && !allowedArea[t.Position]) continue;

                float distSq = (t.Position - pawnPos).LengthHorizontalSquared;
                if (distSq < nearestDistSq)
                {
                    nearestDistSq = distSq;
                    nearest = t;
                }
            }
            return nearest;
        }
    }
}
