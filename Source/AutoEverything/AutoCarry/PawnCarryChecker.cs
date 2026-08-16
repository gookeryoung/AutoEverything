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
    /// - 药品政策无 takeToInventory 条目：跳过对应药品（游戏自身药品库存管理会丢地上）
    ///
    /// 设计意图：
    /// - ShouldSkipForCarry 是"是否完全跳过该 Pawn"的硬过滤
    /// - NeedSleep / GetDrugCarryCount 是"是否跳过某类物品"的软过滤
    /// 两者分层避免一个 Pawn 因禁用某药品就被完全跳过（仍可携带食物与其他药品）
    ///
    /// 关键修复（2026-08-10）：
    /// DrugPolicyEntry 字段全部公开（探针验证），无 allowed 字段。
    /// 直接访问公开字段 takeToInventory 判定（GetDrugCarryCount 方法）。
    /// </summary>
    public static class PawnCarryChecker
    {
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
            // 无意识/昏迷：无法主动服药与拾取（意识水平极低时跳过）
            if (pawn.health?.capacities != null && !pawn.health.capacities.CanBeAwake) return true;
            // 医疗 Job / 卧床休养：避免打断手术与休养
            if (PawnJobGuard.ShouldSkipForMedical(pawn)) return true;
            // 仪式参与中（含心灵仪式）：取药/取食会中断仪式
            if (PawnJobGuard.IsInRitual(pawn)) return true;
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
        /// 纯逻辑版本：综合判定是否跳过 Pawn 的自动携带配置。
        /// 仅供测试调用，参数对应 ShouldSkipForCarry 的各检查项。
        /// </summary>
        internal static bool ShouldSkipForCarryCore(
            bool isNull, bool canManageGear, bool isGhoul, bool isSlave,
            bool isDeadOrDowned, bool canBeAwake, bool shouldSkipForMedical,
            bool isInRitual)
        {
            if (isNull) return true;
            if (!canManageGear) return true;
            if (isGhoul) return true;
            if (isSlave) return true;
            if (isDeadOrDowned) return true;
            if (!canBeAwake) return true;
            if (shouldSkipForMedical) return true;
            if (isInRitual) return true;
            return false;
        }

        /// <summary>
        /// 查询药品政策中指定药品的"目标携带数量"takeToInventory。
        /// 玩家在药品政策 UI 的"携带"列设置的值就是这个字段。
        ///
        /// 返回值语义：
        /// - -1：政策中无此药品条目（默认不携带，避免与玩家意图冲突）
        /// - 0：有条目但 takeToInventory=0（明确不携带）
        /// - >0：玩家希望携带的数量，AutoCarry 以此为目标补充库存
        ///
        /// 关键修复：DrugPolicyEntry.takeToInventory 是公开字段，直接访问无需反射。
        /// 之前误用 GetField("allowed") 返回 null 导致逻辑失效。
        /// </summary>
        public static int GetDrugCarryCount(Pawn pawn, ThingDef drugDef)
        {
            if (pawn?.drugs?.CurrentPolicy == null || drugDef == null) return -1;
            DrugPolicy policy = pawn.drugs.CurrentPolicy;

            // 探针验证：DrugPolicy.entriesInt 是私有字段，但 DrugPolicy 提供 Count 属性与 Item 索引器
            // 用公开索引器遍历，避免反射
            int count = policy.Count;
            for (int i = 0; i < count; i++)
            {
                DrugPolicyEntry entry = policy[i];
                if (entry != null && entry.drug == drugDef)
                {
                    return entry.takeToInventory;
                }
            }
            // 无条目：政策未配置该药品，默认不携带（玩家没说要带）
            return -1;
        }
    }
}
