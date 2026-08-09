using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using AutoEverything.AutoWork;
using AutoEverything.AutoMarkPawn;
using AutoEverything.AutoCarry;

namespace AutoEverything.Core
{
    /// <summary>
    /// 全局自动执行器：事件驱动工作重配、周期触发人员评级、事件驱动高价值标记扫描、周期触发自动携带。
    ///
    /// 设计模式：静态门控模式，
    /// 由 <see cref="AutoEverythingGameComponent"/>.Tick 每 tick 调用 TryTick()，内部静态门控每 60 tick 检查一次。
    /// 不再依赖 Pawn 上的 ThingComp（CompGearManager 已移除，避免与其他装备管理类 MOD 冲突）。
    ///
    /// 触发条件：
    /// - 工作重配（事件 + 周期）：殖民者数量变化时标记待触发；ITab 勾选时立即触发（弹消息框）；
    ///   每 3600 tick（约 1 分钟）周期触发一次，避免长期无人员变动时分配过时
    ///   工作重配需战斗过滤：SetPriority 会取消当前 Job，战斗中执行可能打断手术
    /// - 评级（周期 + 事件）：每 3000 tick 周期触发；殖民者数量增加时立即触发
    ///   周期/事件触发仅更新 Nick 前缀（评级变化时），不重排殖民者栏——避免覆盖玩家手动排序；
    ///   玩家主动触发（ITab 勾选/点排序按钮）才调 ReorderColonistBar 重排
    /// - 高价值标记（事件 + 殖民者栏绘制）：人类单位数量增加时扫描新增高价值目标并弹消息；
    ///   角色定位图标在殖民者栏固定位置由 Harmony ColonistBarColonistDrawer.DrawColonist Postfix 绘制
    ///   （基于特质组合判定，无评级缓存依赖），无需周期触发
    /// - 自动携带（周期 + ITab 触发）：每 6000 tick（约 100 秒）周期触发；
    ///   ITab 勾选时立即触发。需战斗过滤避免战斗中离开战位；医疗守卫避免打断手术/休养
    /// - ITab 勾选：玩家在面板勾选时立即触发一次（弹消息框反馈，含完整目标列表）
    ///
    /// 首次初始化守卫：work.lastTick 等 &lt; 0 时设为当前 tick，不触发执行，
    /// 避免存档加载后立即执行造成卡顿。
    ///
    /// 注：AutoFood/AutoDrug 模块已移除（与其他 MOD 冲突），相关 foodDrug 阶段状态与信仰检测逻辑同步删除。
    /// 注：AutoEquipment 模块（自动装备分配）已整体移除——玩家反馈换装效果不理想，
    /// 改用 RimWorld 原生换装（玩家手动管理装备）。
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

        // 工作重配周期：3600 tick ≈ 1 分钟
        // 勾选状态下定期重新分配工作，避免长期无人员变动时工作分配过时
        private const int WorkExecuteInterval = 3600;

        // 自动携带周期：6000 tick ≈ 100 秒
        // 殖民者周期性从仓库拾取食物与药品到背包，需战斗过滤避免战斗中离开战位
        private const int CarryExecuteInterval = 6000;

        // 阶段状态：把 lastTick + pending 打包为 struct，集中管理各自动阶段的状态
        // work：工作重配（事件触发 ReallocCooldown 冷却 + 周期触发 WorkExecuteInterval 间隔，均需战斗过滤）
        // carry：自动携带（周期触发 CarryExecuteInterval 间隔，需战斗过滤）
        // tier：评级（周期 ExecuteInterval 触发，无 pending——非事件驱动）
        private struct PhaseState
        {
            public int lastTick;
            public bool pending;
        }

        private static PhaseState work = new PhaseState { lastTick = -9999 };
        private static PhaseState carry = new PhaseState { lastTick = -9999 };
        private static int lastTierTick = -9999;
        private static int lastCheckTick = -9999;
        // 注：Mark 无周期触发——角色定位图标在殖民者栏固定位置由 Harmony
        // ColonistBarColonistDrawer.DrawColonist Postfix 绘制（调用 RoleIconDef.GetRoleIcons，
        // 基于特质组合判定，无评级缓存依赖）。
        // Mark 触发条件：人类单位数量增加（事件）+ ITab 勾选切换（玩家主动）。

        // 殖民者数量缓存：-1 = 首次只记录不触发（用于工作重配与评级事件检测）
        private static int lastColonistCount = -1;

        // 全人类单位数量缓存：-1 = 首次只记录不触发（用于高价值标记事件检测）
        // 范围：殖民者+奴隶+囚犯+敌对+中立/盟友+野生人类，与 PawnMarker.IsMarkableTarget 一致
        private static int lastAllHumanlikeCount = -1;

        // Mark 扫描复用缓冲区（避免 Tick 路径 new List<>() 触发 GC）
        private static readonly List<Pawn> allMarkedBuffer = new List<Pawn>();
        private static readonly List<Pawn> newlyMarkedBuffer = new List<Pawn>();

        // 错误去重 salt
        private const int WorkErrorSalt = 0xA200;
        private const int TierErrorSalt = 0xA300;
        private const int CarryErrorSalt = 0xA400;
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
                carry.lastTick = tick;
                lastTierTick = tick;
                lastColonistCount = PawnsFinder.AllMaps_FreeColonists.Count;
                lastAllHumanlikeCount = CountAllHumanlikeSpawned();
                return;
            }

            // 殖民者数量变化检测（驱动工作重配与评级）
            int currentCount = PawnsFinder.AllMaps_FreeColonists.Count;
            if (currentCount != lastColonistCount)
            {
                bool isIncrease = currentCount > lastColonistCount;
                lastColonistCount = currentCount;
                if (isIncrease)
                {
                    // 评级立即触发：Nick 前缀更新（仅编辑 Nick 不取消 Job，安全）
                    ExecuteTier(tick, showMessage: false);
                }
                work.pending = true;
            }

            // 全人类单位数量变化检测（驱动高价值标记扫描）
            // 范围比殖民者广：包含奴隶/囚犯/敌对/中立/野生，任一新增都可能引入新高价值目标
            int currentHumanlikeCount = CountAllHumanlikeSpawned();
            if (currentHumanlikeCount != lastAllHumanlikeCount)
            {
                bool isHumanlikeIncrease = currentHumanlikeCount > lastAllHumanlikeCount;
                lastAllHumanlikeCount = currentHumanlikeCount;
                if (isHumanlikeIncrease && AESettings.autoMarkPawn)
                {
                    // 新增人类单位：扫描并通知新发现的高价值目标
                    // resetTracking=false：仅通知首次出现的高价值目标，已通知过的不再重复弹消息
                    ExecuteMark(tick, showMessage: true, resetTracking: false);
                }
            }

            // 待重配触发：工作重配需战斗过滤（SetPriority 取消 Job）
            if (work.pending && tick - work.lastTick >= ReallocCooldown && !AnyCombatActive())
            {
                work.pending = false;
                ExecuteWork(tick, showMessage: false);
            }
            // 周期触发：勾选状态下每 1 分钟重新分配工作，避免长期无人员变动时分配过时
            // 需战斗过滤：与事件触发一致，避免战斗中打断医疗 Job
            // 复用 work.lastTick：周期触发后同样刷新冷却计时，事件触发也基于此计算
            else if (tick - work.lastTick >= WorkExecuteInterval && !AnyCombatActive())
            {
                ExecuteWork(tick, showMessage: false);
            }

            // 周期触发：仅评级（Mark 无周期——Postfix 每帧自检）
            if (tick - lastTierTick >= ExecuteInterval)
                ExecuteTier(tick, showMessage: false);

            // 周期触发：自动携带（战斗过滤——避免殖民者战斗中离开战位去拿物品）
            if (tick - carry.lastTick >= CarryExecuteInterval && !AnyCombatActive())
                ExecuteCarry(tick, showMessage: false);
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
        /// ITab 勾选切换时调用：立即执行全局重扫描并弹消息框。
        /// resetTracking=true：清空已通知集合，所有当前高价值单位都视为"新发现"并列入消息。
        /// 注：取消勾选（autoMarkPawn=false）也会调用本方法——ExecuteMark 检测开关后静默返回，
        /// 星标由 Harmony Postfix 实时检查开关自动停止绘制。
        /// </summary>
        public static void TriggerMarkNow()
        {
            ExecuteMark(Find.TickManager.TicksGame, showMessage: true, resetTracking: true);
        }

        /// <summary>
        /// ITab 勾选时调用：立即执行自动携带分配并弹消息框。
        /// 注：手动触发也受战斗过滤约束——战斗中派发 TakeInventory Job 会让殖民者离开战位导致伤亡。
        /// 若战斗中调用，弹提示消息但不执行分配。
        /// </summary>
        public static void TriggerCarryNow()
        {
            if (AnyCombatActive())
            {
                Messages.Message("AE_AutoCarry_SkippedInCombat".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }
            ExecuteCarry(Find.TickManager.TicksGame, showMessage: true);
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

        private static void ExecuteMark(int tick, bool showMessage, bool resetTracking)
        {
            // 取消勾选（autoMarkPawn=false）：静默返回。
            // 星标由 Harmony Postfix 实时检查开关自动停止绘制；notifiedMarkedIds 在下次勾选时由 resetTracking=true 清空。
            if (!AESettings.autoMarkPawn) return;

            try
            {
                // 扫描所有地图的高价值单位，更新 notifiedMarkedIds 跟踪集合
                // resetTracking=true 时清空跟踪集合，所有当前高价值单位都视为"新发现"
                PawnMarker.ScanAndMark(allMarkedBuffer, newlyMarkedBuffer, resetTracking);

                if (showMessage)
                {
                    if (resetTracking)
                    {
                        // ITab 勾选切换（玩家主动触发）：始终弹消息，列出当前所有高价值单位
                        string msg = PawnMarker.FormatMessage(allMarkedBuffer, "AE_AutoMarkPawn_FullScan");
                        Messages.Message(msg, MessageTypeDefOf.TaskCompletion);
                    }
                    else if (newlyMarkedBuffer.Count > 0)
                    {
                        // 人员变动触发：仅当有新发现的高价值目标时弹消息（避免无新高价值时刷屏）
                        string msg = PawnMarker.FormatMessage(newlyMarkedBuffer, "AE_AutoMarkPawn_NewFound");
                        Messages.Message(msg, MessageTypeDefOf.TaskCompletion);
                    }
                }

                if (AEDebug.IsActive)
                {
                    int allCount = allMarkedBuffer.Count;
                    int newCount = newlyMarkedBuffer.Count;
                    AEDebug.Log(() => $"[AutoExecutor] 高价值标记扫描: 全部={allCount} 新增={newCount} (tick={tick}, reset={resetTracking})");
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] 高价值标记扫描失败: " + ex.Message, MarkErrorSalt);
            }
        }

        private static void ExecuteCarry(int tick, bool showMessage)
        {
            carry.lastTick = tick;
            if (!AESettings.autoCarryEnabled) return;

            try
            {
                int n = CarryAllocator.ReallocateAll();
                AEDebug.Log(() => $"[AutoExecutor] 自动携带分配: {n} 个殖民者 (tick={tick})");
                if (showMessage)
                    Messages.Message("AE_AutoCarry_Result".Translate(n), MessageTypeDefOf.TaskCompletion);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] 自动携带分配失败: " + ex.Message, CarryErrorSalt);
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

        /// <summary>
        /// 统计所有地图上已生成的可管理人类单位数量（含殖民者/奴隶/囚犯/敌对/中立/野生）。
        /// 用于高价值标记的人员变动检测——任一新增都可能引入新高价值目标。
        /// 过滤条件与 <see cref="PawnMarker.IsMarkableTarget"/> 一致：Spawned &amp;&amp; !Dead &amp;&amp; CanManageGear。
        /// </summary>
        private static int CountAllHumanlikeSpawned()
        {
            int count = 0;
            foreach (Map map in Find.Maps)
            {
                if (map == null || map.mapPawns == null) continue;
                IReadOnlyList<Pawn> all = map.mapPawns.AllPawnsSpawned;
                for (int i = 0; i < all.Count; i++)
                {
                    Pawn p = all[i];
                    if (p.Dead) continue;
                    if (!PawnSuitabilityChecker.CanManageGear(p)) continue;
                    count++;
                }
            }
            return count;
        }
    }
}
