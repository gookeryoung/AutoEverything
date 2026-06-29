using System.Collections.Generic;
using RimWorld;
using Verse;

namespace AutoEquipment
{
    public enum GearContext
    {
        Normal,
        Combat,
        Work,
        Hunting,
        Cold,
        Hot
    }

    public static class ContextDetector
    {
        // 记录每个 Pawn 处于极端温度的起始 tick（按 Pawn ID 索引）
        private static readonly Dictionary<int, int> coldSinceTick = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> hotSinceTick = new Dictionary<int, int>();

        // 记录每个 Pawn 上一次的情境，仅在变化时输出日志以减少噪音
        private static readonly Dictionary<int, GearContext> lastLoggedContext = new Dictionary<int, GearContext>();

        // 需持续暴露于极端温度一定时间后才触发 Cold/Hot 情境（2500 tick ≈ 42 秒）
        private const int TempSustainTicks = 2500;

        // 字典清理周期：缩短为 6000 tick（约 100 秒），减少 thingIDNumber 复用导致的误判窗口
        // 原值 60000 tick 过长，Pawn 死亡后 ID 可能被快速复用给新 Pawn，导致新 Pawn 误读旧温度状态
        private const int CleanupInterval = 6000;
        private static int nextCleanupTick = 6000;

        /// <summary>
        /// 清理已死亡/离开地图的 Pawn 在字典中的残留条目。
        /// 由 CompGearManager 的 Tick 路径定期调用。
        /// </summary>
        public static void CleanupDeadPawns()
        {
            int tick = Find.TickManager.TicksGame;
            if (tick < nextCleanupTick) return;
            nextCleanupTick = tick + CleanupInterval;

            PawnStateCleaner.CleanupAll(coldSinceTick, hotSinceTick, lastLoggedContext);
        }

        /// <summary>
        /// 校验指定 Pawn ID 是否仍存活（在当前地图的 FreeColonistsSpawned 中）。
        /// 用于 GetContext 中防御 thingIDNumber 复用：若字典中记录的 ID 已不在存活集合，
        /// 视为旧记录，从字典中移除后重新记录。
        /// </summary>
        private static bool IsPawnAlive(int pawnId)
        {
            return PawnStateCleaner.IsAlive(pawnId);
        }

        /// <summary>
        /// 判定 Pawn 当前的装备情境。
        /// </summary>
        public static GearContext GetContext(Pawn pawn)
        {
            if (pawn == null) return GearContext.Normal;

            // 战斗：已征召或正在逃跑
            if (pawn.Drafted)
                return LogContextIfChanged(pawn, GearContext.Combat, "已征召");

            // 狩猎工作
            if (AESettings.huntingWeapon && IsHunting(pawn))
                return LogContextIfChanged(pawn, GearContext.Hunting, "狩猎工作中");

            // 温度检测：仅在持续暴露后才触发
            if (AESettings.temperatureAware && pawn.Map != null)
            {
                float ambientTemp = pawn.AmbientTemperature;
                FloatRange comfortRange = pawn.ComfortableTemperatureRange();
                int tick = Find.TickManager.TicksGame;
                int pawnId = pawn.thingIDNumber;

                bool isCold = ambientTemp < comfortRange.min - AESettings.tempDangerMargin;
                bool isHot = ambientTemp > comfortRange.max + AESettings.tempDangerMargin;

                if (isCold)
                {
                    // 防御 thingIDNumber 复用：若记录的 tick 距当前超过合理窗口，
                    // 视为旧 Pawn 残留记录（ID 被复用给新 Pawn），清除后重新记录
                    if (coldSinceTick.TryGetValue(pawnId, out int coldTick)
                        && tick - coldTick > CleanupInterval + TempSustainTicks)
                    {
                        coldSinceTick.Remove(pawnId);
                    }
                    if (!coldSinceTick.ContainsKey(pawnId))
                        coldSinceTick[pawnId] = tick;
                    if (tick - coldSinceTick[pawnId] >= TempSustainTicks)
                        return LogContextIfChanged(pawn, GearContext.Cold,
                            $"环境={ambientTemp:F1}C, 舒适下限={comfortRange.min:F1}C");
                }
                else
                {
                    coldSinceTick.Remove(pawnId);
                }

                if (isHot)
                {
                    // 防御 thingIDNumber 复用：同上
                    if (hotSinceTick.TryGetValue(pawnId, out int hotTick)
                        && tick - hotTick > CleanupInterval + TempSustainTicks)
                    {
                        hotSinceTick.Remove(pawnId);
                    }
                    if (!hotSinceTick.ContainsKey(pawnId))
                        hotSinceTick[pawnId] = tick;
                    if (tick - hotSinceTick[pawnId] >= TempSustainTicks)
                        return LogContextIfChanged(pawn, GearContext.Hot,
                            $"环境={ambientTemp:F1}C, 舒适上限={comfortRange.max:F1}C");
                }
                else
                {
                    hotSinceTick.Remove(pawnId);
                }
            }

            // 工作中
            if (pawn.CurJob != null && !pawn.CurJob.def.alwaysShowWeapon)
                return LogContextIfChanged(pawn, GearContext.Work, $"工作={pawn.CurJob.def.defName}");

            return LogContextIfChanged(pawn, GearContext.Normal, null);
        }

        private static GearContext LogContextIfChanged(Pawn pawn, GearContext newContext, string reason)
        {
            int pawnId = pawn.thingIDNumber;
            if (lastLoggedContext.TryGetValue(pawnId, out GearContext prev))
            {
                if (prev != newContext)
                {
                    Log.Message($"[AutoEquipment] {pawn.LabelShort} 情境变化: {prev} -> {newContext}"
                        + (reason != null ? $" ({reason})" : ""));
                    lastLoggedContext[pawnId] = newContext;
                }
            }
            else
            {
                // 首次见到该 Pawn：用 AEDebug.Log 避免游戏加载时刷屏
                // 仅情境变化时才用 Log.Message 输出，减少玩家控制台噪音
                AEDebug.Log(() => $"[AutoEquipment] {pawn.LabelShort} 初始情境: {newContext}"
                    + (reason != null ? $" ({reason})" : ""));
                lastLoggedContext[pawnId] = newContext;
            }
            return newContext;
        }

        public static bool IsHunting(Pawn pawn)
        {
            if (pawn?.CurJob == null) return false;
            return pawn.CurJob.def == JobDefOf.Hunt
                || pawn.CurJob.def == JobDefOf.PredatorHunt;
        }

        /// <summary>
        /// 检测 Pawn 是否受到近战威胁（用于副武器切出判断）。
        /// 返回 true 的条件：敌方相邻且正在近战攻击，
        /// 或仅有近战能力的敌方接近至 3 格以内。
        /// </summary>
        public static bool IsUnderMeleeAttack(Pawn pawn)
        {
            if (pawn?.Map == null) return false;

            foreach (var threat in pawn.Map.attackTargetsCache.GetPotentialTargetsFor(pawn))
            {
                if (!(threat.Thing is Pawn attacker) || attacker.Dead || attacker.Downed) continue;
                if (!attacker.HostileTo(pawn)) continue;

                float dist = attacker.Position.DistanceTo(pawn.Position);

                // 相邻且正在近战攻击：立即威胁
                if (dist <= 1.5f && attacker.CurrentEffectiveVerb?.IsMeleeAttack == true)
                    return true;

                // 接近至 3 格内且无远程武器：即将近战
                if (dist <= 3f)
                {
                    bool attackerHasRanged = attacker.equipment?.Primary?.def.IsRangedWeapon == true;
                    if (!attackerHasRanged)
                        return true;
                }
            }
            return false;
        }
    }
}
