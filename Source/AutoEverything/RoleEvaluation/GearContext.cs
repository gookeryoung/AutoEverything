using System.Collections.Generic;
using RimWorld;
using Verse;
using AutoEverything.Core;

namespace AutoEverything.RoleEvaluation
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

        // 需持续暴露于极端温度一定时间后才触发 Cold/Hot 情境（2500 tick ≈ 42 秒）
        private const int TempSustainTicks = 2500;

        // thingIDNumber 复用防御窗口：若记录的 tick 距当前超过此值，视为旧 Pawn 残留记录
        // 6000 tick ≈ 100 秒，Pawn 死亡后 ID 可能被快速复用给新 Pawn，导致新 Pawn 误读旧温度状态
        private const int IdReuseGuardWindow = 6000;

        /// <summary>
        /// 判定 Pawn 当前的装备情境。
        /// 注：装备评估功能已移除，此方法仅用于 ITab 情境徽章显示。
        /// </summary>
        public static GearContext GetContext(Pawn pawn)
        {
            if (pawn == null) return GearContext.Normal;

            // 食尸鬼直接返回 Normal：其 CurJob 可能是"等待"类被误判为 Work，显示"工作中"会误导玩家
            if (DLCCompat.IsGhoul(pawn)) return GearContext.Normal;

            // 征召中或正在逃跑：战斗情境
            if (pawn.Drafted)
                return LogContextIfChanged(pawn, GearContext.Combat, "已征召");

            // 狩猎工作
            if (IsHunting(pawn))
                return LogContextIfChanged(pawn, GearContext.Hunting, "狩猎工作中");

            // 战斗 Job（未征召反击）：AttackStatic/AttackMelee/UseVerbOnThing/Wait_Combat 等
            // alwaysShowWeapon=true 的 Job（非狩猎）都是战斗相关，应归为 Combat 情境
            if (pawn.CurJob != null && pawn.CurJob.def.alwaysShowWeapon)
                return LogContextIfChanged(pawn, GearContext.Combat, $"战斗={pawn.CurJob.def.defName}");

            // 温度检测：仅在持续暴露后才触发
            // 温度判定常量硬编码（原 AESettings.tempDangerMargin 已随装备模块移除）
            if (pawn.Map != null)
            {
                float ambientTemp = pawn.AmbientTemperature;
                FloatRange comfortRange = pawn.ComfortableTemperatureRange();
                int tick = Find.TickManager.TicksGame;
                int pawnId = pawn.thingIDNumber;

                float tempDangerMargin = 5f;
                bool isCold = ambientTemp < comfortRange.min - tempDangerMargin;
                bool isHot = ambientTemp > comfortRange.max + tempDangerMargin;

                if (isCold)
                {
                    // 防御 thingIDNumber 复用：若记录的 tick 距当前超过合理窗口，
                    // 视为旧 Pawn 残留记录（ID 被复用给新 Pawn），清除后重新记录
                    if (coldSinceTick.TryGetValue(pawnId, out int coldTick)
                        && tick - coldTick > IdReuseGuardWindow + TempSustainTicks)
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
                        && tick - hotTick > IdReuseGuardWindow + TempSustainTicks)
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
            // 情境检测完成：debug 模式下输出（接受重复日志，简化代码，去重字典已移除）
            if (AEDebug.IsActive) AEDebug.Log(() => $"[AutoEverything] {pawn.LabelShort} 情境: {newContext}"
                + (reason != null ? $" ({reason})" : ""));
            return newContext;
        }

        public static bool IsHunting(Pawn pawn)
        {
            if (pawn?.CurJob == null) return false;
            return pawn.CurJob.def == JobDefOf.Hunt
                || pawn.CurJob.def == JobDefOf.PredatorHunt;
        }
    }
}
