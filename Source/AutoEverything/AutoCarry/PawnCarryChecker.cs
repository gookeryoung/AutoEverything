using System.Collections;
using System.Reflection;
using RimWorld;
using Verse;
using AutoEverything.Core;

namespace AutoEverything.AutoCarry
{
    /// <summary>
    /// 殖民者携带适用性检查：综合判定 Pawn 是否适合自动携带配置。
    ///
    /// 排除规则（与用户需求一致）：
    /// - 食尸鬼：用户明确要求"请勿为食尸鬼配置"
    /// - 机器人（机械族）：通过 PawnSuitabilityChecker.CanManageGear 已排除（仅 Humanlike 通过）
    /// - 奴隶：用户决策"仅自由殖民者"
    /// - 医疗 Job / 卧床休养：复用 PawnJobGuard.ShouldSkipForMedical，避免打断手术
    /// - 不需要睡眠：跳过清醒丸（在 CarryPolicy 内部判定，此处不排除 Pawn）
    /// - 禁止活力水：跳过活力水（在 CarryPolicy 内部判定，此处不排除 Pawn）
    ///
    /// 设计意图：
    /// - ShouldSkipForCarry 是"是否完全跳过该 Pawn"的硬过滤
    /// - NeedSleep / LuciferiumAllowed 是"是否跳过某类物品"的软过滤
    /// 两者分层避免一个 Pawn 因禁用某药品就被完全跳过（仍可携带食物与其他药品）
    /// </summary>
    public static class PawnCarryChecker
    {
        /// <summary>
        /// 综合判定：是否完全跳过该 Pawn 的自动携带配置。
        /// true 表示跳过；false 表示参与配置（仍可能因 NeedSleep/LuciferiumAllowed 跳过部分物品）。
        /// </summary>
        public static bool ShouldSkipForCarry(Pawn pawn)
        {
            if (pawn == null) return true;
            // 仅人类 like 通过（机械族/动物/昆虫等已排除）
            if (!PawnSuitabilityChecker.CanManageGear(pawn)) return true;
            // 食尸鬼：用户明确要求不配置
            if (DLCCompat.IsGhoul(pawn)) return true;
            // 奴隶：用户决策"仅自由殖民者"
            if (pawn.IsSlaveOfColony) return true;
            // 死亡/倒下：无法去仓库拾取
            if (pawn.Dead || pawn.Downed) return true;
            // 医疗 Job / 卧床休养：避免打断手术与休养
            if (PawnJobGuard.ShouldSkipForMedical(pawn)) return true;
            return false;
        }

        /// <summary>
        /// 判定殖民者是否需要睡眠。
        /// 不需要睡眠的殖民者（如某些基因/种族）不带清醒丸——清醒丸主要用于抵抗睡眠。
        /// 判定方式：pawn.needs.rest == null 表示该 Pawn 没有 SleepNeed。
        /// </summary>
        public static bool NeedSleep(Pawn pawn)
        {
            return NeedSleepCore(pawn?.needs?.rest);
        }

        /// <summary>
        /// 纯逻辑版本：根据 Need_Rest 是否为 null 判定是否需要睡眠。
        /// 仅供测试调用。
        /// </summary>
        /// <param name="restNeed">殖民者的 Need_Rest 实例，null 表示不需要睡眠</param>
        /// <returns>true 表示需要睡眠；false 表示不需要</returns>
        internal static bool NeedSleepCore(object restNeed)
        {
            // restNeed == null 表示该 Pawn 没有 SleepNeed（如某些基因/种族）
            return restNeed != null;
        }

        /// <summary>
        /// 判定殖民者药品政策是否允许活力水（Luciferium）。
        /// 玩家在药品政策中禁用活力水时不带——避免与玩家意图冲突。
        /// 实现方式：使用反射获取 DrugPolicy.entries 字段，避免 API 可见性差异。
        /// </summary>
        public static bool LuciferiumAllowed(Pawn pawn)
        {
            if (pawn?.drugs?.CurrentPolicy == null) return true;
            DrugPolicy policy = pawn.drugs.CurrentPolicy;
            // 使用反射获取 entries 字段（不同 RimWorld 版本可见性可能不同）
            FieldInfo fieldInfo = typeof(DrugPolicy).GetField("entries",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (fieldInfo == null) return true; // 字段不存在，默认允许

            IList entries = fieldInfo.GetValue(policy) as IList;
            if (entries == null) return true;

            bool hasEntry = false;
            bool entryAllowed = false;
            for (int i = 0; i < entries.Count; i++)
            {
                DrugPolicyEntry entry = entries[i] as DrugPolicyEntry;
                if (entry != null && entry.drug == ThingDefOf.Luciferium)
                {
                    hasEntry = true;
                    // 使用反射获取 allowed 字段，避免可见性问题
                    FieldInfo allowedField = typeof(DrugPolicyEntry).GetField("allowed",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (allowedField == null) break;
                    entryAllowed = (bool)allowedField.GetValue(entry);
                    break;
                }
            }
            return LuciferiumAllowedCore(hasEntry, entryAllowed);
        }

        /// <summary>
        /// 纯逻辑版本：根据药品政策条目判定是否允许活力水。
        /// 仅供测试调用。
        /// </summary>
        /// <param name="hasEntry">true 表示政策中有 Luciferium 条目；false 表示无条目（默认允许）</param>
        /// <param name="entryAllowed">条目的 allowed 字段值（hasEntry=false 时此值忽略）</param>
        /// <returns>true 表示允许；false 表示禁止</returns>
        internal static bool LuciferiumAllowedCore(bool hasEntry, bool entryAllowed)
        {
            // 无条目时默认允许（RimWorld 原生默认未禁用）
            if (!hasEntry) return true;
            return entryAllowed;
        }
    }
}
