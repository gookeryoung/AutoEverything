using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using AutoEverything.AutoWork;
using AutoEverything.AutoMarkPawn;

namespace AutoEverything.Core
{
    /// <summary>
    /// 全局自动执行器：事件驱动工作重配、周期触发人员评级与星标标记。
    ///
    /// 设计模式：静态门控模式，
    /// 由 CompGearManager.CompTick 每 tick 调用 TryTick()，内部静态门控每 60 tick 检查一次。
    /// 不新增 MapComponent/GameComponent，KISS 原则——CompTick 已是现成的每 tick 入口。
    ///
    /// 触发条件：
    /// - 工作重配（事件驱动）：殖民者数量增加或减少时立即触发（不弹消息框）；ITab 勾选时立即触发（弹消息框）
    ///   工作重配不再周期触发，避免频繁变更优先级触发 RimWorld Job 重评估，从而中断手术/进食等长 Job
    /// - 评级/星标（周期 + 事件）：每 3000 tick（约 50 秒）周期触发；殖民者数量增加时立即触发
    /// - ITab 勾选：玩家在面板勾选时立即触发一次（弹消息框反馈）
    ///
    /// 首次初始化守卫：lastWorkTick/lastTierTick &lt; 0 时设为当前 tick，不触发执行，
    /// 避免存档加载后立即执行造成卡顿。
    /// </summary>
    internal static class AutoExecutor
    {
        // 周期触发间隔：3000 tick ≈ 50 秒
        // 人员评级/星标均为非紧急操作，延迟可接受
        // 注：工作重配改为事件驱动（殖民者增减 + ITab 手动触发），不再周期触发
        private const int ExecuteInterval = 3000;

        // 殖民者数量检查间隔：60 tick ≈ 1 秒
        // 每 tick 查询 PawnsFinder.AllMaps_FreeColonists.Count 有少量开销，60 tick 检查一次足够
        private const int CheckInterval = 60;

        // 工作重配冷却：2500 tick ≈ 42 秒
        // 殖民者数量变化后延迟触发，避免战斗中连续死亡连锁触发工作重配打断医疗 Job
        // ITab 手动触发（TriggerWorkNow）不受此冷却限制
        private const int WorkReallocCooldown = 2500;

        private static int lastCheckTick = -9999;
        private static int lastWorkTick = -9999;
        private static int lastTierTick = -9999;
        private static int lastMarkTick = -9999;

        // 殖民者数量缓存：-1 = 首次只记录不触发，避免存档加载误触发
        private static int lastColonistCount = -1;

        // 待重配标志：殖民者数量变化时标记，冷却结束且非战斗中才真正触发
        // 避免战斗中死亡立即触发 ReallocateAll 打断医生手术
        private static bool pendingWorkRealloc = false;

        // 错误去重 salt：每个错误点独立，避免跨方法冲突
        private const int WorkErrorSalt = 0xA200;
        private const int TierErrorSalt = 0xA300;
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
                lastMarkTick = tick;
                lastColonistCount = PawnsFinder.AllMaps_FreeColonists.Count;
                return;
            }

            // 殖民者数量变化检测：增加或减少时标记待重配（不弹消息）
            // 工作重配改为事件驱动 + 冷却 + 战斗过滤：
            //   - 战斗中死亡立即触发会打断医生手术（SetPriority 取消 TendPatient/DoBill）
            //   - 标记 pendingWorkRealloc，冷却结束且非战斗中才真正触发
            // 评级/星标：增加时立即触发（不打断 Job——评级只改 Nick）
            int currentCount = PawnsFinder.AllMaps_FreeColonists.Count;
            if (currentCount != lastColonistCount)
            {
                bool isIncrease = currentCount > lastColonistCount;
                lastColonistCount = currentCount;
                // 评级/星标：增加时立即触发（不打断 Job）
                if (isIncrease)
                {
                    ExecuteTier(tick, showMessage: false);
                    ExecuteMark(tick, showMessage: false);
                }
                // 工作重配：标记待触发，不立即执行
                pendingWorkRealloc = true;
            }

            // 待重配触发：冷却结束且非战斗中才真正执行工作重配
            // AnyCombatActive 仅在 pending && 冷却到期时调用，平时无开销
            if (pendingWorkRealloc && tick - lastWorkTick >= WorkReallocCooldown && !AnyCombatActive())
            {
                pendingWorkRealloc = false;
                ExecuteWork(tick, showMessage: false);
            }

            // 周期触发：评级/星标 3000 tick（工作重配已改为事件驱动，无周期触发）
            if (tick - lastTierTick >= ExecuteInterval)
            {
                ExecuteTier(tick, showMessage: false);
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
        /// 高价值非殖民者星标：头顶图标由 Harmony 补丁实时绘制，此处仅用于消息提示。
        /// 周期路径（showMessage=false）直接返回，避免扫描所有地图 Pawn 的开销。
        /// ITab 勾选时（showMessage=true）统计当前非殖民者高价值对象数量并弹消息。
        /// </summary>
        private static void ExecuteMark(int tick, bool showMessage)
        {
            lastMarkTick = tick;
            if (!AESettings.autoMarkPawn) return;

            // 头顶图标由 Harmony 补丁实时绘制，周期路径无需重复执行
            if (!showMessage) return;

            try
            {
                int n = PawnMarker.CountMarkablePawns();
                AEDebug.Log(() => $"[AutoExecutor] 高价值非殖民者标记: {n} 个对象 (tick={tick})");
                Messages.Message(
                    "AE_AutoMarkPawnResult".Translate(n),
                    MessageTypeDefOf.TaskCompletion);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] 高价值星标统计失败: " + ex.Message, MarkErrorSalt);
            }
        }

        /// <summary>
        /// 战斗检测：检查所有地图是否有未 Downed 的敌对 Pawn。
        /// 仅在 pendingWorkRealloc && 冷却到期时调用，平时无开销。
        /// 用于战斗中延迟工作重配——避免 SetPriority 打断医生正在执行的 TendPatient/DoBill(Bill_Medical)。
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
