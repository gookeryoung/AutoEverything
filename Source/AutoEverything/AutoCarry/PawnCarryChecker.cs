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
    /// - 药品政策 carryCount=0：跳过对应药品（游戏自身药品库存管理会丢地上）
    ///
    /// 设计意图：
    /// - ShouldSkipForCarry 是"是否完全跳过该 Pawn"的硬过滤
    /// - NeedSleep / GetDrugCarryCount 是"是否跳过某类物品"的软过滤
    /// 两者分层避免一个 Pawn 因禁用某药品就被完全跳过（仍可携带食物与其他药品）
    /// </summary>
    public static class PawnCarryChecker
    {
        // DrugPolicyEntry 字段反射缓存：只需执行一次 GetField，避免每次分配 GC
        private static readonly FieldInfo entriesField = typeof(DrugPolicy).GetField("entries",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private static readonly FieldInfo allowedField = typeof(DrugPolicyEntry).GetField("allowed",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private static readonly FieldInfo takeToInventoryField = typeof(DrugPolicyEntry).GetField("takeToInventory",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        /// <summary>
        /// 综合判定：是否完全跳过该 Pawn 的自动携带配置。
        /// true 表示跳过；false 表示参与配置（仍可能因 NeedSleep/GetDrugCarryCount 跳过部分物品）。
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
        internal static bool NeedSleepCore(object restNeed)
        {
            // restNeed == null 表示该 Pawn 没有 SleepNeed（如某些基因/种族）
            return restNeed != null;
        }

        /// <summary>
        /// 查询药品政策中指定药品的"目标携带数量"takeToInventory。
        /// 玩家在药品政策 UI 的"携带"列设置的值就是这个字段。
        ///
        /// 为什么必须用这个值：
        /// RimWorld 药品政策系统有独立库存管理，每 tick 检查背包药品实际数量是否超过 takeToInventory，
        /// 超过就把多余的丢地上。如果我们硬塞 takeToInventory=0 的药品到背包，就会立刻被丢弃。
        /// 因此 AutoCarry 必须尊重这个值，不与游戏自身机制打架。
        ///
        /// 返回值语义：
        /// - -1：政策中无此药品条目（或反射获取失败），默认不携带（避免与玩家意图冲突）
        /// - 0：有条目但玩家明确设为"不携带"（或 allowed=false 禁用）
        /// - >0：玩家希望携带的数量，AutoCarry 以此为目标补充库存
        /// </summary>
        public static int GetDrugCarryCount(Pawn pawn, ThingDef drugDef)
        {
            if (pawn?.drugs?.CurrentPolicy == null || drugDef == null) return -1;
            DrugPolicy policy = pawn.drugs.CurrentPolicy;

            // 反射字段失败（API 变更），默认不携带，安全降级
            if (entriesField == null || allowedField == null || takeToInventoryField == null) return -1;

            IList entries = entriesField.GetValue(policy) as IList;
            if (entries == null) return -1;

            for (int i = 0; i < entries.Count; i++)
            {
                DrugPolicyEntry entry = entries[i] as DrugPolicyEntry;
                if (entry != null && entry.drug == drugDef)
                {
                    bool entryAllowed = (bool)allowedField.GetValue(entry);
                    // allowed=false 视为禁用，不携带
                    if (!entryAllowed) return 0;
                    int carryCount = (int)takeToInventoryField.GetValue(entry);
                    return carryCount;
                }
            }
            // 无条目：政策未配置该药品，默认不携带（玩家没说要带）
            return -1;
        }

        /// <summary>
        /// 判定殖民者药品政策是否允许活力水（Luciferium）。
        /// 玩家在药品政策中禁用活力水时不带——避免与玩家意图冲突。
        /// 保留原方法供 CarryPolicy 现有调用路径兼容，内部委托给 GetDrugCarryCount。
        /// </summary>
        public static bool LuciferiumAllowed(Pawn pawn)
        {
            int count = GetDrugCarryCount(pawn, ThingDefOf.Luciferium);
            // count=-1（无条目）视为默认允许，与旧行为一致；count>0 也是允许
            // count=0 才是明确禁用
            return count != 0;
        }

        /// <summary>
        /// 纯逻辑版本：根据药品政策条目判定是否允许活力水。
        /// 仅供测试调用。
        /// </summary>
        internal static bool LuciferiumAllowedCore(bool hasEntry, bool entryAllowed)
        {
            // 无条目时默认允许（RimWorld 原生默认未禁用）
            if (!hasEntry) return true;
            return entryAllowed;
        }
    }
}
