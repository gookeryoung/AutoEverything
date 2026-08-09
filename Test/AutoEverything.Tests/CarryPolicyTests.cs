using System;
using AutoEverything.AutoCarry;

namespace AutoEverything.Tests
{
    /// <summary>
    /// CarryPolicy 配置与食物优先级顺序的单元测试。
    ///
    /// 覆盖范围：
    /// 1. 数量常量：FoodCount=3, LuciferiumCount=1, WakeUpCount=1, PenoxycylineCount=1
    /// 2. 食物优先级顺序：包装食物 → 干粮 → 奢侈餐 → 精致餐 → 一般餐
    ///    验证 FoodDefNames 数组顺序与用户需求一致
    ///
    /// 设计原则：测试不依赖 RimWorld 运行时（无 DefDatabase 初始化），
    /// 仅验证静态配置常量与数组顺序，确保用户需求"包装食物→干粮→奢侈→精致→一般"被正确编码。
    /// </summary>
    public static class CarryPolicyTests
    {
        public static int RunAll()
        {
            int failures = 0;
            int total = 0;

            failures += RunConstantsTests(ref total);
            failures += RunFoodPriorityTests(ref total);

            return failures;
        }

        // ════════════════════════════════════════════════════════════
        // 1. 数量常量：验证用户需求"食物x3, 活力水x1, 清醒丸x1, 思滞血清x1"
        // ════════════════════════════════════════════════════════════

        private static int RunConstantsTests(ref int total)
        {
            int failures = 0;

            CheckConst(CarryPolicy.FoodCount, 3, "FoodCount=3 (食物 x3)", ref failures, ref total);
            CheckConst(CarryPolicy.LuciferiumCount, 1, "LuciferiumCount=1 (活力水 x1)", ref failures, ref total);
            CheckConst(CarryPolicy.WakeUpCount, 1, "WakeUpCount=1 (清醒丸 x1)", ref failures, ref total);
            CheckConst(CarryPolicy.PenoxycylineCount, 1, "PenoxycylineCount=1 (思滞血清 x1)", ref failures, ref total);

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

            // ── 验证数组长度（7 项：包装食物1 + 干粮2 + 奢侈1 + 精致1 + 一般2）──
            total++;
            if (CarryPolicy.FoodDefNames == null)
            {
                Console.WriteLine("  FAIL: FoodDefNames 不应为 null");
                failures++;
            }
            else if (CarryPolicy.FoodDefNames.Length != 7)
            {
                Console.WriteLine($"  FAIL: FoodDefNames 长度应为 7, 实际 {CarryPolicy.FoodDefNames.Length}");
                failures++;
            }

            // ── 验证顺序：用户需求"包装食物 → 干粮 → 奢侈 → 精致 → 一般" ──
            CheckFoodName(0, "PackagedSurvivalMeal", "优先级 0: 包装食物（不腐坏、便于远征）", ref failures, ref total);
            CheckFoodName(1, "Pemmican", "优先级 1: 干粮（长久保存）", ref failures, ref total);
            CheckFoodName(2, "JerkedDriedMeat", "优先级 2: 干粮（风干肉，CORE 1.4+）", ref failures, ref total);
            CheckFoodName(3, "MealLavish", "优先级 3: 奢侈餐", ref failures, ref total);
            CheckFoodName(4, "MealFine", "优先级 4: 精致餐", ref failures, ref total);
            CheckFoodName(5, "MealSimple", "优先级 5: 一般餐", ref failures, ref total);
            CheckFoodName(6, "MealNutrientPaste", "优先级 6: 营养糊（最低优先级兜底）", ref failures, ref total);

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
    }
}
