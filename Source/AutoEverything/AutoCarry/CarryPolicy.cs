using System.Collections.Generic;
using RimWorld;
using Verse;

namespace AutoEverything.AutoCarry
{
    /// <summary>
    /// 携带物品策略：定义殖民者应自动携带的物品清单与数量。
    ///
    /// 设计意图：
    /// - 食物 x3：殖民者短期远征或战斗中的口粮
    /// - 活力水（Luciferium）：数量来源于药品政策"携带"列（takeToInventory）；政策无条目/不携带则跳过（避免被游戏丢地上）
    /// - 清醒丸（WakeUp）：数量来源于药品政策"携带"列；不需要睡眠的殖民者额外跳过（即使政策有也没意义）
    /// - 思滞血清（Penoxycyline）：数量来源于药品政策"携带"列
    ///
    /// 重要：药品携带量不再硬编码为 1，必须尊重药品政策的 takeToInventory 字段！
    /// 因为 RimWorld 药品政策系统每 tick 检查背包，如果实际数量超过 takeToInventory，
    /// 就会把超出部分丢地上。硬塞 takeToInventory=0 的药品必然会立刻被丢弃。
    /// 玩家在"药品政策→携带"列设多少，AutoCarry 就补多少。默认 0 的药品直接跳过。
    ///
    /// 食物优先级（用户决策 2026-08-09）：
    /// 包装食物 → 干粮 → 奢侈餐 → 精致餐 → 一般餐
    /// </summary>
    public static class CarryPolicy
    {
        // 食物目标数量：3 个
        public const int FoodCount = 3;

        // 药品"携带"列默认值（无政策条目或反射失败时的兜底数量——0 = 不携带，避免丢地上）
        private const int DefaultDrugCarryCount = 0;

        // 食物 DefName 列表（按优先级从高到低排序）
        // internal 暴露供测试验证优先级顺序
        internal static readonly string[] FoodDefNames =
        {
            "PackagedSurvivalMeal", // 包装食物：不腐坏、便于远征
            "Pemmican",             // 干粮：长久保存
            "JerkedDriedMeat",      // 干粮：风干肉（CORE 1.4+）
            "MealLavish",           // 奢侈餐
            "MealFine",             // 精致餐
            "MealSimple",           // 一般餐
            "MealNutrientPaste"     // 一般餐：营养糊（最低优先级兜底）
        };

        // 食物 ThingDef 缓存：懒加载避免跨线程访问 DefDatabase
        private static List<ThingDef> cachedFoodDefs;
        private static ThingDef cachedLuciferium;
        private static ThingDef cachedWakeUp;
        private static ThingDef cachedPenoxycyline;

        /// <summary>
        /// 懒加载并缓存食物 ThingDef 列表（仅执行一次）。
        /// 缺失的 DefName 静默跳过（DLC 差异或 MOD 移除时降级）。
        /// </summary>
        private static List<ThingDef> GetFoodDefs()
        {
            if (cachedFoodDefs != null) return cachedFoodDefs;
            cachedFoodDefs = new List<ThingDef>();
            for (int i = 0; i < FoodDefNames.Length; i++)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(FoodDefNames[i]);
                if (def != null) cachedFoodDefs.Add(def);
            }
            return cachedFoodDefs;
        }

        /// <summary>
        /// 填充殖民者应携带的物品清单到外部缓冲区（避免每次分配新 List）。
        /// 调用方负责在调用前 Clear 缓冲区。
        /// 药品数量完全来源于药品政策"携带"列（takeToInventory），
        /// 避免硬编码数量与游戏药品库存管理机制冲突导致物品被丢地上。
        /// </summary>
        public static void FillCarryItems(Pawn pawn, List<CarryEntry> result)
        {
            // 食物：始终携带（无个性化过滤）
            List<ThingDef> foodDefs = GetFoodDefs();
            for (int i = 0; i < foodDefs.Count; i++)
            {
                result.Add(new CarryEntry { Def = foodDefs[i], Count = FoodCount });
            }

            // 活力水：根据药品政策 takeToInventory 决定数量
            ThingDef luciferium = GetLuciferiumDef();
            if (luciferium != null)
            {
                int carryCount = GetDrugPolicyCarryCount(pawn, luciferium);
                if (carryCount > 0)
                    result.Add(new CarryEntry { Def = luciferium, Count = carryCount });
            }

            // 清醒丸：不需要睡眠 + 政策有"携带"列才带
            ThingDef wakeUp = GetWakeUpDef();
            if (wakeUp != null && PawnCarryChecker.NeedSleep(pawn))
            {
                int carryCount = GetDrugPolicyCarryCount(pawn, wakeUp);
                if (carryCount > 0)
                    result.Add(new CarryEntry { Def = wakeUp, Count = carryCount });
            }

            // 思滞血清：根据药品政策 takeToInventory 决定数量
            ThingDef penoxycyline = GetPenoxycylineDef();
            if (penoxycyline != null)
            {
                int carryCount = GetDrugPolicyCarryCount(pawn, penoxycyline);
                if (carryCount > 0)
                    result.Add(new CarryEntry { Def = penoxycyline, Count = carryCount });
            }
        }

        /// <summary>
        /// 从药品政策查询指定药品的目标携带数量。
        /// - 政策有条目且 takeToInventory > 0 → 返回该值
        /// - 政策无条目 / allowed=false / takeToInventory=0 → 返回 0（不携带，避免丢地上）
        /// </summary>
        private static int GetDrugPolicyCarryCount(Pawn pawn, ThingDef drugDef)
        {
            int policyCount = PawnCarryChecker.GetDrugCarryCount(pawn, drugDef);
            // -1（无条目/反射失败）或 0 都视为不携带
            return policyCount > 0 ? policyCount : DefaultDrugCarryCount;
        }

        /// <summary>活力水 ThingDef（懒加载，缺失返回 null）</summary>
        internal static ThingDef GetLuciferiumDef()
        {
            if (cachedLuciferium == null)
                cachedLuciferium = ThingDefOf.Luciferium;
            return cachedLuciferium;
        }

        /// <summary>清醒丸 ThingDef（懒加载，缺失返回 null）</summary>
        internal static ThingDef GetWakeUpDef()
        {
            if (cachedWakeUp == null)
                cachedWakeUp = ThingDefOf.WakeUp;
            return cachedWakeUp;
        }

        /// <summary>思滞血清 ThingDef（懒加载，缺失返回 null）</summary>
        internal static ThingDef GetPenoxycylineDef()
        {
            if (cachedPenoxycyline == null)
                cachedPenoxycyline = ThingDefOf.Penoxycyline;
            return cachedPenoxycyline;
        }
    }

    /// <summary>
    /// 携带物品条目：ThingDef + 目标数量。
    /// 结构体避免堆分配，列表中的元素直接存值。
    /// </summary>
    public struct CarryEntry
    {
        public ThingDef Def;
        public int Count;
    }
}
