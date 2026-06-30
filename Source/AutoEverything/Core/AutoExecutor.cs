using System;
using RimWorld;
using Verse;
using AutoEverything.AutoEquipment;
using AutoEverything.AutoWork;
using AutoEverything.AutoMarkPawn;

namespace AutoEverything.Core
{
    /// <summary>
    /// 全局自动执行器：周期触发工作重配、人员评级与装备重配。
    ///
    /// 设计模式：复用 SidearmAllocator/BeltAllocator 的静态门控模式，
    /// 由 CompGearManager.CompTick 每 tick 调用 TryTick()，内部静态门控每 60 tick 检查一次。
    /// 不新增 MapComponent/GameComponent，KISS 原则——CompTick 已是现成的每 tick 入口。
    ///
    /// 触发条件：
    /// - 周期触发：每 3000 tick（约 50 秒）执行一次工作重配、人员评级与装备重配
    /// - 新增殖民者：殖民者数量增加时立即触发（不弹消息框）
    /// - ITab 勾选：玩家在面板勾选时立即触发一次（弹消息框反馈）
    ///
    /// 装备重配采用轻量升级检查（ForceEvaluate），不放下当前装备，不打断征召战斗。
    ///
    /// 首次初始化守卫：lastWorkTick/lastTierTick/lastGearTick &lt; 0 时设为当前 tick，不触发执行，
    /// 避免存档加载后立即执行造成卡顿。
    /// </summary>
    internal static class AutoExecutor
    {
        // 周期触发间隔：3000 tick ≈ 50 秒
        // 工作重配与人员评级均为非紧急操作，延迟可接受
        private const int ExecuteInterval = 3000;

        // 殖民者数量检查间隔：60 tick ≈ 1 秒
        // 每 tick 查询 PawnsFinder.AllMaps_FreeColonists.Count 有少量开销，60 tick 检查一次足够
        private const int CheckInterval = 60;

        private static int lastCheckTick = -9999;
        private static int lastWorkTick = -9999;
        private static int lastTierTick = -9999;
        private static int lastGearTick = -9999;
        private static int lastMarkTick = -9999;

        // 殖民者数量缓存：-1 = 首次只记录不触发，避免存档加载误触发
        private static int lastColonistCount = -1;

        // 错误去重 salt：每个错误点独立，避免跨方法冲突
        private const int WorkErrorSalt = 0xA200;
        private const int TierErrorSalt = 0xA300;
        private const int GearErrorSalt = 0xA400;
        private const int MarkErrorSalt = 0xA500;

        /// <summary>
        /// 由 CompGearManager.CompTick 每 tick 调用。
        /// 静态门控：每 60 tick 检查一次殖民者数量变化与周期触发。
        /// 自动周期路径不弹消息框（避免刷屏），仅走 AEDebug.Log。
        /// </summary>
        public static void TryTick()
        {
            int tick = Find.TickManager.TicksGame;

            // 静态门控：每 60 tick 才执行一次实际检查
            if (tick - lastCheckTick < CheckInterval) return;
            lastCheckTick = tick;

            // 首次初始化守卫：记录当前 tick 与殖民者数量，不触发执行
            // 避免存档加载后立即执行造成卡顿
            if (lastWorkTick < 0)
            {
                lastWorkTick = tick;
                lastTierTick = tick;
                lastGearTick = tick;
                lastMarkTick = tick;
                lastColonistCount = PawnsFinder.AllMaps_FreeColonists.Count;
                return;
            }

            // 新增殖民者检测：数量增加时立即触发工作+评级+装备重配+星标（不弹消息）
            int currentCount = PawnsFinder.AllMaps_FreeColonists.Count;
            if (currentCount > lastColonistCount)
            {
                lastColonistCount = currentCount;
                ExecuteWork(tick, showMessage: false);
                ExecuteTier(tick, showMessage: false);
                ExecuteGear(tick, showMessage: false);
                ExecuteMark(tick, showMessage: false);
                return;
            }
            lastColonistCount = currentCount;

            // 周期触发：每 3000 tick 执行一次
            if (tick - lastWorkTick >= ExecuteInterval)
            {
                ExecuteWork(tick, showMessage: false);
            }
            if (tick - lastTierTick >= ExecuteInterval)
            {
                ExecuteTier(tick, showMessage: false);
            }
            if (tick - lastGearTick >= ExecuteInterval)
            {
                ExecuteGear(tick, showMessage: false);
            }
            if (tick - lastMarkTick >= ExecuteInterval)
            {
                ExecuteMark(tick, showMessage: false);
            }
        }

        /// <summary>
        /// ITab 勾选时调用：立即执行工作重配并弹消息框反馈。
        /// </summary>
        public static void TriggerWorkNow()
        {
            ExecuteWork(Find.TickManager.TicksGame, showMessage: true);
        }

        /// <summary>
        /// ITab 勾选时调用：立即执行人员评级并弹消息框反馈。
        /// </summary>
        public static void TriggerTierNow()
        {
            ExecuteTier(Find.TickManager.TicksGame, showMessage: true);
        }

        /// <summary>
        /// ITab 勾选时调用：立即执行装备重配并弹消息框反馈。
        /// </summary>
        public static void TriggerGearNow()
        {
            ExecuteGear(Find.TickManager.TicksGame, showMessage: true);
        }

        /// <summary>
        /// ITab 勾选时调用：立即执行高价值星标标记并弹消息框反馈。
        /// </summary>
        public static void TriggerMarkNow()
        {
            ExecuteMark(Find.TickManager.TicksGame, showMessage: true);
        }

        /// <summary>
        /// 执行工作重配：调用 WorkAllocator.ReallocateAll()。
        /// 受 AESettings.autoWorkEnabled 开关控制，关闭时不执行。
        /// try-catch 隔离：失败时 Log.ErrorOnce 记录，不影响其他逻辑。
        /// </summary>
        private static void ExecuteWork(int tick, bool showMessage)
        {
            lastWorkTick = tick;
            if (!AESettings.autoWorkEnabled) return;

            try
            {
                int n = WorkAllocator.ReallocateAll();
                AEDebug.Log(() => $"[AutoExecutor] 工作自动配置: {n} 个殖民者 (tick={tick})");
                if (showMessage)
                {
                    Messages.Message(
                        "AE_GlobalWorkReallocateResult".Translate(n),
                        MessageTypeDefOf.TaskCompletion);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] 工作自动配置失败: " + ex.Message, WorkErrorSalt);
            }
        }

        /// <summary>
        /// 执行人员评级：调用 AESettings.ApplyTierTagsWithDefaultSort()。
        /// 受 AESettings.autoTierTag 开关控制，关闭时不执行。
        /// try-catch 隔离：失败时 Log.ErrorOnce 记录，不影响其他逻辑。
        /// </summary>
        private static void ExecuteTier(int tick, bool showMessage)
        {
            lastTierTick = tick;
            if (!AESettings.autoTierTag) return;

            try
            {
                int n = AESettings.ApplyTierTagsWithDefaultSort();
                AEDebug.Log(() => $"[AutoExecutor] 人员自动评级: {n} 个殖民者 (tick={tick})");
                if (showMessage)
                {
                    Messages.Message(
                        "AE_TierTag_ApplyResult".Translate(n),
                        MessageTypeDefOf.TaskCompletion);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] 人员自动评级失败: " + ex.Message, TierErrorSalt);
            }
        }

        /// <summary>
        /// 执行装备重配：轻量升级检查（每 Pawn 调用 ForceEvaluate，不放下当前装备）。
        /// 受 AESettings.autoGearReallocate 开关控制，关闭时不执行。
        /// 过滤链与 CompGearManager.CompTick 一致：食尸鬼/不适用/Dead/Downed/奴隶/锁定/征召 均排除。
        /// try-catch 隔离：失败时 Log.ErrorOnce 记录，不影响其他逻辑。
        /// </summary>
        private static void ExecuteGear(int tick, bool showMessage)
        {
            lastGearTick = tick;
            if (!AESettings.autoGearReallocate) return;

            try
            {
                int n = 0;
                foreach (Map map in Find.Maps)
                {
                    foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
                    {
                        if (DLCCompat.IsGhoul(pawn)) continue;
                        if (!PawnSuitabilityChecker.CanManageGear(pawn)) continue;
                        if (pawn.Dead || pawn.Downed) continue;
                        if (DLCCompat.IsSlave(pawn)) continue;   // 奴隶未征召不参与自动装备
                        if (pawn.Drafted) continue;              // 不打断征召战斗
                        CompGearManager comp = pawn.GetComp<CompGearManager>();
                        if (comp == null) continue;
                        if (comp.locked) continue;               // 尊重玩家锁定
                        // 未成年仅评估防具，跳过武器/副武器/库存（与 CompGearManager.CompTick 守卫一致）
                        if (DLCCompat.IsChild(pawn))
                        {
                            comp.ForceEvaluate(CompGearManager.ReloadTarget.Apparel);
                            n++;
                            continue;
                        }
                        comp.ForceEvaluate(CompGearManager.ReloadTarget.All);
                        n++;
                    }
                }
                AEDebug.Log(() => $"[AutoExecutor] 自动装备重配: {n} 个殖民者 (tick={tick})");
                if (showMessage)
                {
                    Messages.Message(
                        "AE_AutoGearReallocateResult".Translate(n),
                        MessageTypeDefOf.TaskCompletion);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] 自动装备重配失败: " + ex.Message, GearErrorSalt);
            }
        }

        /// <summary>
        /// 执行高价值星标标记：调用 PawnMarker.ApplyMarkers()。
        /// 受 AESettings.autoMarkPawn 开关控制，关闭时不执行（清除由 ITab 勾选变化处理）。
        /// try-catch 隔离：失败时 Log.ErrorOnce 记录，不影响其他逻辑。
        /// </summary>
        private static void ExecuteMark(int tick, bool showMessage)
        {
            lastMarkTick = tick;
            if (!AESettings.autoMarkPawn) return;

            try
            {
                int n = PawnMarker.ApplyMarkers();
                AEDebug.Log(() => $"[AutoExecutor] 高价值星标: {n} 个殖民者 (tick={tick})");
                if (showMessage)
                {
                    Messages.Message(
                        "AE_AutoMarkPawnResult".Translate(n),
                        MessageTypeDefOf.TaskCompletion);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] 高价值星标失败: " + ex.Message, MarkErrorSalt);
            }
        }
    }
}
