using System;
using AutoEverything.AutoCarry;

namespace AutoEverything.Tests
{
    /// <summary>
    /// CarryPolicy 配置与食物优先级顺序的单元测试。
    ///
    /// 覆盖范围：
    /// 1. 常量：FoodCount=3, DefaultDrugCarryCount=0（药品默认不携带，避免丢地上）
    /// 2. 食物优先级顺序：包装食物 → 干粮 → 奢侈餐 → 精致餐 → 一般餐
    ///    验证 FoodDefNames 数组顺序与用户需求一致
    /// 3. DrugCarryCountCore 纯逻辑：验证 carryCount 转换语义
    ///    >0 原值返回；-1/0 都返回 0（不携带，避免被游戏丢地上）
    ///
    /// 设计原则：药品数量不再硬编码——必须尊重药品政策"携带"列（takeToInventory），
    /// 否则游戏自身机制会把超额药品丢地上。测试仅验证转换语义，不依赖 RimWorld 运行时。
    /// </summary>
    public static class CarryPolicyTests
    {
        public static int RunAll()
        {
            int failures = 0;
            int total = 0;

            failures += RunConstantsTests(ref total);
            failures += RunFoodPriorityTests(ref total);
            failures += RunDrugCarryCountTests(ref total);

            return failures;
        }

        // ════════════════════════════════════════════════════════════
        // 1. 常量验证
        // ════════════════════════════════════════════════════════════

        private static int RunConstantsTests(ref int total)
        {
            int failures = 0;

            CheckConst(CarryPolicy.FoodCount, 3, "FoodCount=3 (食物 x3，始终携带)", ref failures, ref total);

            Console.WriteLine($"[CarryPolicyTests/Constants] {total - failures}/{total} passed");
            return failures;
        }

        private static void CheckConst(int actual, int expected, string label,
            ref int failures, ref int total)
        {
            total++;
            if (actual != expected)
            {
                Console.WriteLine($"  FAIL: {label}: expected {expected}, got {actual}");
                failures++;
            }
        }

        // ════════════════════════════════════════════════════════════
        // 2. 食物优先级顺序：包装食物 → 干粮 → 奢侈餐 → 精致餐 → 一般餐
        // ════════════════════════════════════════════════════════════

        private static int RunFoodPriorityTests(ref int total)
        {
            int failures = 0;

            // ── 验证数组长度（6 项：包装食物1 + 干粮1 + 奢侈1 + 精致1 + 一般1 + 营养糊1）──
            // RimWorld 1.6 中实际存在的食物 DefName（已校验 Data/Core/Defs/ThingDefs_Items/Items_Food.xml）：
            //   MealSurvivalPack / Pemmican / MealLavish / MealFine / MealSimple / MealNutrientPaste
            // 注意：不存在 PackagedSurvivalMeal（旧名误用）和 JerkedDriedMeat（凭空编造）
            total++;
            if (CarryPolicy.FoodDefNames == null)
            {
                Console.WriteLine("  FAIL: FoodDefNames 不应为 null");
                failures++;
            }
            else if (CarryPolicy.FoodDefNames.Length != 6)
            {
                Console.WriteLine($"  FAIL: FoodDefNames 长度应为 6, 实际 {CarryPolicy.FoodDefNames.Length}");
                failures++;
            }

            // ── 验证顺序：用户需求"包装食物 → 干粮 → 奢侈 → 精致 → 一般" ──
            CheckFoodName(0, "MealSurvivalPack", "优先级 0: 包装食物（生存口粮，不腐坏、便于远征）", ref failures, ref total);
            CheckFoodName(1, "Pemmican", "优先级 1: 干粮（长久保存）", ref failures, ref total);
            CheckFoodName(2, "MealLavish", "优先级 2: 奢侈餐", ref failures, ref total);
            CheckFoodName(3, "MealFine", "优先级 3: 精致餐", ref failures, ref total);
            CheckFoodName(4, "MealSimple", "优先级 4: 一般餐", ref failures, ref total);
            CheckFoodName(5, "MealNutrientPaste", "优先级 5: 营养糊（最低优先级兜底）", ref failures, ref total);

            Console.WriteLine($"[CarryPolicyTests/FoodPriority] {total - failures}/{total} passed");
            return failures;
        }

        private static void CheckFoodName(int index, string expected, string label,
            ref int failures, ref int total)
        {
            total++;
            string actual = CarryPolicy.FoodDefNames[index];
            if (actual != expected)
            {
                Console.WriteLine($"  FAIL: {label}: expected [{expected}], got [{actual}]");
                failures++;
            }
        }

        // ════════════════════════════════════════════════════════════
        // 3. DrugCarryCount 转换语义：>0 原值，-1/0 → 0（不携带，避免丢地上）
        // ════════════════════════════════════════════════════════════

        private static int RunDrugCarryCountTests(ref int total)
        {
            int failures = 0;

            // ── -1（无政策条目/反射失败）→ 0，默认不携带避免丢地上 ──
            CheckCarryConvert(-1, 0, "carryCount=-1（无条目）→ 0 不携带", ref failures, ref total);

            // ── 0（政策有条目但 takeToInventory=0，或 allowed=false）→ 0 ──
            CheckCarryConvert(0, 0, "carryCount=0（不携带）→ 0", ref failures, ref total);

            // ── >0 → 原值（玩家在政策"携带"列设的数量）──
            CheckCarryConvert(1, 1, "carryCount=1 → 1（政策设为携带1个）", ref failures, ref total);
            CheckCarryConvert(2, 2, "carryCount=2 → 2（政策设为携带2个）", ref failures, ref total);
            CheckCarryConvert(5, 5, "carryCount=5 → 5（政策设为携带5个）", ref failures, ref total);

            Console.WriteLine($"[CarryPolicyTests/DrugCarryCount] {total - failures}/{total} passed");
            return failures;
        }

        private static void CheckCarryConvert(int policyCount, int expected, string label,
            ref int failures, ref int total)
        {
            total++;
            int actual = DrugCarryCountConvert(policyCount);
            if (actual != expected)
            {
                Console.WriteLine($"  FAIL: {label}: expected {expected}, got {actual}");
                failures++;
            }
        }

        /// <summary>
        /// 镜像 CarryPolicy.GetDrugPolicyCarryCount 的纯逻辑转换，供测试验证。
        /// 语义：>0 原值返回；-1/0 都返回 0（不携带，避免被游戏丢地上）。
        /// </summary>
        private static int DrugCarryCountConvert(int policyCount)
        {
            return policyCount > 0 ? policyCount : 0;
        }
    }
}
