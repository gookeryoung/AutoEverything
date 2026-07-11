using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using AutoEverything.AutoWork;
using AutoEverything.AutoMarkPawn;
using AutoEverything.AutoFood;
using AutoEverything.AutoDrug;

namespace AutoEverything.Core
{
    /// <summary>
    /// 全局自动执行器：事件驱动工作重配、食物/用药方案分配、周期触发人员评级与星标标记。
    ///
    /// 设计模式：静态门控模式，
    /// 由 CompGearManager.CompTick 每 tick 调用 TryTick()，内部静态门控每 60 tick 检查一次。
    /// 不新增 MapComponent/GameComponent，KISS 原则——CompTick 已是现成的每 tick 入口。
    ///
    /// 触发条件：
    /// - 工作重配（事件驱动）：殖民者数量变化时标记待触发；ITab 勾选时立即触发（弹消息框）
    ///   工作重配需战斗过滤：SetPriority 会取消当前 Job，战斗中执行可能打断手术
    /// - 食物/用药方案（事件驱动）：殖民者数量变化或信仰变化时标记待触发；ITab 勾选时立即触发
    ///   食物/用药方案无需战斗过滤：修改 CurrentFoodPolicy/CurrentPolicy 不取消当前 Job
    /// - 评级/星标（周期 + 事件）：每 3000 tick 周期触发；殖民者数量增加时立即触发
    /// - ITab 勾选：玩家在面板勾选时立即触发一次（弹消息框反馈）
    ///
    /// 信仰变化检测：每 60 tick 比对殖民者信仰 def 名，变化时标记待重配食物/用药方案。
    /// 覆盖"被传教成功"场景：殖民者信仰改变后自动重新分配方案。
    ///
    /// 首次初始化守卫：lastWorkTick 等 &lt; 0 时设为当前 tick，不触发执行，
    /// 避免存档加载后立即执行造成卡顿。
    /// </summary>
    internal static class AutoExecutor
    {
        // 周期触发间隔：3000 tick ≈ 50 秒
        private const int ExecuteInterval = 3000;

        // 殖民者数量检查间隔：60 tick ≈ 1 秒
        private const int CheckInterval = 60;

        // 工作重配冷却：2500 tick ≈ 42 秒
        // 殖民者数量变化后延迟触发，避免战斗中连续死亡连锁触发工作重配打断医疗 Job
        private const int ReallocCooldown = 2500;

        private static int lastCheckTick = -9999;
        private static int lastWorkTick = -9999;
        private static int lastFoodDrugTick = -9999;
        private static int lastTierTick = -9999;
        private static int lastMarkTick = -9999;

        // 殖民者数量缓存：-1 = 首次只记录不触发
        private static int lastColonistCount = -1;

        // 待重配标志：事件标记，冷却到期后执行
        private static bool pendingWorkRealloc = false;
        private static bool pendingFoodDrugRealloc = false;

        // 信仰快照：记录每个殖民者的信仰 id，用于检测信仰变化（被传教）
        private static readonly Dictionary<int, int> ideoSnapshot = new Dictionary<int, int>();

        // 错误去重 salt
        private const int WorkErrorSalt = 0xA200;
        private const int FoodErrorSalt = 0xA400;
        private const int DrugErrorSalt = 0xA600;
        private const int TierErrorSalt = 0xA300;
        private const int MarkErrorSalt = 0xA500;

        /// <summary>
        /// 由 CompGearManager.CompTick 每 tick 调用。
        /// 静态门控：每 60 tick 检查一次殖民者数量变化、信仰变化与周期触发。
        /// </summary>
        public static void TryTick()
        {
            int tick = Find.TickManager.TicksGame;

            if (tick - lastCheckTick < CheckInterval) return;
            lastCheckTick = tick;

            // 首次初始化守卫
            if (lastWorkTick < 0)
            {
                lastWorkTick = tick;
                lastFoodDrugTick = tick;
                lastTierTick = tick;
                lastMarkTick = tick;
                lastColonistCount = PawnsFinder.AllMaps_FreeColonists.Count;
                SnapshotIdeos();
                return;
            }

            // 殖民者数量变化检测
            int currentCount = PawnsFinder.AllMaps_FreeColonists.Count;
            if (currentCount != lastColonistCount)
            {
                bool isIncrease = currentCount > lastColonistCount;
                lastColonistCount = currentCount;
                if (isIncrease)
                {
                    ExecuteTier(tick, showMessage: false);
                    ExecuteMark(tick, showMessage: false);
                }
                pendingWorkRealloc = true;
                pendingFoodDrugRealloc = true;
                SnapshotIdeos();
            }
            else
            {
                // 信仰变化检测（被传教成功）：比对信仰 def 名
                if (DetectIdeoChange())
                {
                    pendingFoodDrugRealloc = true;
                    SnapshotIdeos();
                }
            }

            // 待重配触发：工作重配需战斗过滤（SetPriority 取消 Job）
            if (pendingWorkRealloc && tick - lastWorkTick >= ReallocCooldown && !AnyCombatActive())
            {
                pendingWorkRealloc = false;
                ExecuteWork(tick, showMessage: false);
            }

            // 待重配触发：食物/用药方案无需战斗过滤（改 CurrentPolicy 不取消 Job）
            if (pendingFoodDrugRealloc && tick - lastFoodDrugTick >= ReallocCooldown)
            {
                pendingFoodDrugRealloc = false;
                ExecuteFoodPolicy(tick, showMessage: false);
                ExecuteDrugPolicy(tick, showMessage: false);
            }

            // 周期触发：评级/星标
            if (tick - lastTierTick >= ExecuteInterval)
                ExecuteTier(tick, showMessage: false);
            if (tick - lastMarkTick >= ExecuteInterval)
                ExecuteMark(tick, showMessage: false);
        }

        /// <summary>
        /// ITab 勾选时调用：立即执行工作重配并弹消息框。
        /// </summary>
        public static void TriggerWorkNow()
        {
            ExecuteWork(Find.TickManager.TicksGame, showMessage: true);
        }

        /// <summary>
        /// ITab 勾选时调用：立即执行食物方案分配并弹消息框。
        /// </summary>
        public static void TriggerFoodPolicyNow()
        {
            ExecuteFoodPolicy(Find.TickManager.TicksGame, showMessage: true);
        }

        /// <summary>
        /// ITab 勾选时调用：立即执行用药方案分配并弹消息框。
        /// </summary>
        public static void TriggerDrugPolicyNow()
        {
            ExecuteDrugPolicy(Find.TickManager.TicksGame, showMessage: true);
        }

        /// <summary>
        /// ITab 勾选时调用：立即执行人员评级并弹消息框。
        /// </summary>
        public static void TriggerTierNow()
        {
            ExecuteTier(Find.TickManager.TicksGame, showMessage: true);
        }

        /// <summary>
        /// ITab 勾选时调用：立即执行高价值星标统计并弹消息框。
        /// </summary>
        public static void TriggerMarkNow()
        {
            ExecuteMark(Find.TickManager.TicksGame, showMessage: true);
        }

        private static void ExecuteWork(int tick, bool showMessage)
        {
            lastWorkTick = tick;
            if (!AESettings.autoWorkEnabled) return;

            try
            {
                int n = WorkAllocator.ReallocateAll();
                AEDebug.Log(() => $"[AutoExecutor] 工作自动配置: {n} 个殖民者 (tick={tick})");
                if (showMessage)
                    Messages.Message("AE_GlobalWorkReallocateResult".Translate(n), MessageTypeDefOf.TaskCompletion);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] 工作自动配置失败: " + ex.Message, WorkErrorSalt);
            }
        }

        private static void ExecuteFoodPolicy(int tick, bool showMessage)
        {
            lastFoodDrugTick = tick;
            if (!AESettings.autoFoodPolicyEnabled) return;

            try
            {
                int n = FoodPolicyAllocator.ReallocateAll();
                AEDebug.Log(() => $"[AutoExecutor] 食物方案自动配置: {n} 个殖民者 (tick={tick})");
                if (showMessage)
                    Messages.Message("AE_FoodPolicyResult".Translate(n), MessageTypeDefOf.TaskCompletion);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] 食物方案自动配置失败: " + ex.Message, FoodErrorSalt);
            }
        }

        private static void ExecuteDrugPolicy(int tick, bool showMessage)
        {
            if (!AESettings.autoDrugPolicyEnabled) return;

            try
            {
                int n = DrugPolicyAllocator.ReallocateAll();
                AEDebug.Log(() => $"[AutoExecutor] 用药方案自动配置: {n} 个殖民者 (tick={tick})");
                if (showMessage)
                    Messages.Message("AE_DrugPolicyResult".Translate(n), MessageTypeDefOf.TaskCompletion);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] 用药方案自动配置失败: " + ex.Message, DrugErrorSalt);
            }
        }

        private static void ExecuteTier(int tick, bool showMessage)
        {
            lastTierTick = tick;
            if (!AESettings.autoTierTag) return;

            try
            {
                int n = AESettings.ApplyTierTagsWithDefaultSort();
                AEDebug.Log(() => $"[AutoExecutor] 人员自动评级: {n} 个殖民者 (tick={tick})");
                if (showMessage)
                    Messages.Message("AE_TierTag_ApplyResult".Translate(n), MessageTypeDefOf.TaskCompletion);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] 人员自动评级失败: " + ex.Message, TierErrorSalt);
            }
        }

        private static void ExecuteMark(int tick, bool showMessage)
        {
            lastMarkTick = tick;
            if (!AESettings.autoMarkPawn) return;

            if (!showMessage) return;

            try
            {
                int n = PawnMarker.CountMarkablePawns();
                AEDebug.Log(() => $"[AutoExecutor] 高价值非殖民者标记: {n} 个对象 (tick={tick})");
                Messages.Message("AE_AutoMarkPawnResult".Translate(n), MessageTypeDefOf.TaskCompletion);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] 高价值星标统计失败: " + ex.Message, MarkErrorSalt);
            }
        }

        /// <summary>
        /// 记录当前所有殖民者的信仰 id 快照。
        /// 在首次初始化、殖民者数量变化、信仰变化后调用。
        /// </summary>
        private static void SnapshotIdeos()
        {
            ideoSnapshot.Clear();
            foreach (Pawn pawn in PawnsFinder.AllMaps_FreeColonists)
            {
                if (pawn == null) continue;
                int ideoId = pawn.Ideo?.id ?? 0;
                ideoSnapshot[pawn.thingIDNumber] = ideoId;
            }
        }

        /// <summary>
        /// 检测是否有殖民者的信仰发生了变化（被传教成功）。
        /// 返回 true 表示有变化。检测后由调用方更新快照。
        /// </summary>
        private static bool DetectIdeoChange()
        {
            foreach (Pawn pawn in PawnsFinder.AllMaps_FreeColonists)
            {
                if (pawn == null) continue;
                int pid = pawn.thingIDNumber;
                int currentIdeo = pawn.Ideo?.id ?? 0;
                if (ideoSnapshot.TryGetValue(pid, out int savedIdeo))
                {
                    if (savedIdeo != currentIdeo)
                        return true;
                }
                else
                {
                    // 新出现的殖民者（不在快照中）：由殖民者数量变化检测覆盖
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 战斗检测：检查所有地图是否有未 Downed 的敌对 Pawn。
        /// 仅在 pendingWorkRealloc && 冷却到期时调用，平时无开销。
        /// </summary>
        private static bool AnyCombatActive()
        {
            foreach (Map map in Find.Maps)
            {
                if (map.mapPawns == null) continue;
                IReadOnlyList<Pawn> allPawns = map.mapPawns.AllPawnsSpawned;
                for (int i = 0; i < allPawns.Count; i++)
                {
                    Pawn p = allPawns[i];
                    if (p.Downed || p.Dead) continue;
                    if (p.HostileTo(Faction.OfPlayer)) return true;
                }
            }
            return false;
        }
    }
}
