using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using AutoEverything.AutoWork;
using AutoEverything.AutoMarkPawn;

namespace AutoEverything.Core
{
    /// <summary>
    /// 全局自动执行器：事件驱动工作重配、周期触发人员评级。
    ///
    /// 设计模式：静态门控模式，
    /// 由 <see cref="AutoEverythingGameComponent"/>.Tick 每 tick 调用 TryTick()，内部静态门控每 60 tick 检查一次。
    /// 不再依赖 Pawn 上的 ThingComp（CompGearManager 已移除，避免与其他装备管理类 MOD 冲突）。
    ///
    /// 触发条件：
    /// - 工作重配（事件驱动）：殖民者数量变化时标记待触发；ITab 勾选时立即触发（弹消息框）
    ///   工作重配需战斗过滤：SetPriority 会取消当前 Job，战斗中执行可能打断手术
    /// - 评级（周期 + 事件）：每 3000 tick 周期触发；殖民者数量增加时立即触发
    ///   周期/事件触发仅更新 Nick 前缀（评级变化时），不重排殖民者栏——避免覆盖玩家手动排序；
    ///   玩家主动触发（ITab 勾选/点排序按钮）才调 ReorderColonistBar 重排
    /// - 星标（事件 + 每帧）：殖民者数量增加时弹消息反馈；红星绘制靠 Harmony PawnUIOverlay
    ///   Postfix 每帧调用 PawnMarker.IsHighValue（走 TierCacheService 共享 2500 tick 缓存），无需周期触发
    /// - ITab 勾选：玩家在面板勾选时立即触发一次（弹消息框反馈）
    ///
    /// 首次初始化守卫：work.lastTick 等 &lt; 0 时设为当前 tick，不触发执行，
    /// 避免存档加载后立即执行造成卡顿。
    ///
    /// 注：AutoFood/AutoDrug 模块已移除（与其他 MOD 冲突），相关 foodDrug 阶段状态与信仰检测逻辑同步删除。
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

        // 阶段状态：把 lastTick + pending 打包为 struct，集中管理各自动阶段的状态
        // work：工作重配（需战斗过滤，ReallocCooldown 冷却）
        // tier：评级（周期 ExecuteInterval 触发，无 pending——非事件驱动）
        private struct PhaseState
        {
            public int lastTick;
            public bool pending;
        }

        private static PhaseState work = new PhaseState { lastTick = -9999 };
        private static int lastTierTick = -9999;
        private static int lastCheckTick = -9999;
        // 注：Mark（红星标注）无需 lastXxxTick——ExecuteMark(showMessage:false) 是空操作，
        // 红星绘制完全靠 Harmony PawnUIOverlay Postfix 每帧调用 PawnMarker.IsHighValue（走 TierCacheService 共享 2500 tick 缓存）。
        // Mark 仅靠"殖民者数量增加"事件 + ITab 勾选触发，不需周期触发。

        // 殖民者数量缓存：-1 = 首次只记录不触发
        private static int lastColonistCount = -1;

        // 错误去重 salt
        private const int WorkErrorSalt = 0xA200;
        private const int TierErrorSalt = 0xA300;
        private const int MarkErrorSalt = 0xA500;

        /// <summary>
        /// 由 <see cref="AutoEverythingGameComponent"/>.Tick 每 tick 调用。
        /// 静态门控：每 60 tick 检查一次殖民者数量变化与周期触发。
        /// </summary>
        public static void TryTick()
        {
            int tick = Find.TickManager.TicksGame;

            if (tick - lastCheckTick < CheckInterval) return;
            lastCheckTick = tick;

            // 首次初始化守卫
            if (work.lastTick < 0)
            {
                work.lastTick = tick;
                lastTierTick = tick;
                lastColonistCount = PawnsFinder.AllMaps_FreeColonists.Count;
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
                    // 评级立即触发：Nick 前缀更新（仅编辑 Nick 不取消 Job，安全）
                    ExecuteTier(tick, showMessage: false);
                    // Mark 不在此触发——红星绘制靠 Harmony Postfix 每帧自检 IsHighValue（带 2500 tick 缓存），
                    // 新增殖民者本身属于 OfPlayer 不会被标记，仅新增的非殖民者高价值目标会在 Postfix 自动识别
                }
                work.pending = true;
            }

            // 待重配触发：工作重配需战斗过滤（SetPriority 取消 Job）
            if (work.pending && tick - work.lastTick >= ReallocCooldown && !AnyCombatActive())
            {
                work.pending = false;
                ExecuteWork(tick, showMessage: false);
            }

            // 周期触发：仅评级（Mark 无周期——Postfix 每帧自检）
            if (tick - lastTierTick >= ExecuteInterval)
                ExecuteTier(tick, showMessage: false);
        }

        /// <summary>
        /// ITab 勾选时调用：立即执行工作重配并弹消息框。
        /// </summary>
        public static void TriggerWorkNow()
        {
            ExecuteWork(Find.TickManager.TicksGame, showMessage: true);
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
            work.lastTick = tick;
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

        private static void ExecuteTier(int tick, bool showMessage)
        {
            lastTierTick = tick;
            if (!AESettings.autoTierTag) return;

            try
            {
                // 周期/事件触发(showMessage=false)：仅更新 Nick 前缀，不重排殖民者栏
                // ——避免每 3000 tick 覆盖玩家手动调整的殖民者栏顺序
                // 玩家主动触发(showMessage=true，ITab 勾选)：调 ApplyTierTagsWithDefaultSort 重排
                int n = showMessage
                    ? AESettings.ApplyTierTagsWithDefaultSort()
                    : AESettings.ApplyTierTagsToAllPawns();
                AEDebug.Log(() => $"[AutoExecutor] 人员自动评级: {n} 个殖民者 (tick={tick}, sort={showMessage})");
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
            if (!AESettings.autoMarkPawn) return;

            // showMessage=false 时无操作：Mark 无周期触发需求，
            // 红星绘制靠 Harmony PawnUIOverlay Postfix 每帧调用 PawnMarker.IsHighValue（走 TierCacheService 共享 2500 tick 缓存）
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
        /// 战斗检测：检查所有地图是否有未 Downed 的敌对 Pawn。
        /// 仅在 work.pending && 冷却到期时调用，平时无开销。
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
