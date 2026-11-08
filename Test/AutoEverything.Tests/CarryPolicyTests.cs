using System;
using AutoEverything.AutoCarry;
using AutoEverything.Core;

namespace AutoEverything.Tests
{
    /// <summary>
    /// CarryPolicy 与 CarryItemCatalog 的单元测试。
    ///
    /// 覆盖范围：
    /// 1. 常量与食物优先级顺序（包装食物 → 干粮 → 奢侈餐 → 精致餐 → 一般餐 → 营养糊）
    /// 2. 逐项携带目录：药品/血清类别与默认数量
    /// 3. EffectiveDrugCount 纯逻辑：配置数量与药品政策数量取较小值，避免被游戏丢地上
    /// 4. ShouldCarryWakeUp / ShouldCarrySerum 纯逻辑：清醒丸与血清的携带门槛
    ///
    /// 设计原则：不依赖 RimWorld 运行时（无 Pawn/DrugPolicy 实例），只测纯函数与静态目录。
    /// </summary>
    public static class CarryPolicyTests
    {
        public static int RunAll()
        {
            int failures = 0;
            int total = 0;

            failures += RunConstantsTests(ref total);
            failures += RunFoodPriorityTests(ref total);
            failures += RunCatalogTests(ref total);
            failures += RunEffectiveDrugCountTests(ref total);
            failures += RunGateTests(ref total);

            return failures;
        }

        // ════════════════════════════════════════════════════════════
        // 1. 常量验证
        // ════════════════════════════════════════════════════════════

        private static int RunConstantsTests(ref int total)
        {
            int failures = 0;

            CheckInt(CarryPolicy.FoodCount, 3, "FoodCount=3（食物默认总量）", ref failures, ref total);

            Console.WriteLine($"[CarryPolicyTests/Constants] {total - failures}/{total} passed");
            return failures;
        }

        private static void CheckInt(int actual, int expected, string label,
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
        // 2. 食物优先级顺序
        // ════════════════════════════════════════════════════════════

        private static int RunFoodPriorityTests(ref int total)
        {
            int failures = 0;

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

            CheckFoodName(0, "MealSurvivalPack", "优先级 0: 包装食物", ref failures, ref total);
            CheckFoodName(1, "Pemmican", "优先级 1: 干粮", ref failures, ref total);
            CheckFoodName(2, "MealLavish", "优先级 2: 奢侈餐", ref failures, ref total);
            CheckFoodName(3, "MealFine", "优先级 3: 精致餐", ref failures, ref total);
            CheckFoodName(4, "MealSimple", "优先级 4: 一般餐", ref failures, ref total);
            CheckFoodName(5, "MealNutrientPaste", "优先级 5: 营养糊", ref failures, ref total);

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
        // 3. 目录：类别与默认数量
        // ════════════════════════════════════════════════════════════

        private static int RunCatalogTests(ref int total)
        {
            int failures = 0;

            // 默认携带 1 个的药品/血清（保持旧行为）
            CheckDefault("WakeUp", 1, ref failures, ref total);
            CheckDefault("GoJuice", 1, ref failures, ref total);
            CheckDefault("Penoxycyline", 1, ref failures, ref total);
            CheckDefault("PsychiteTea", 1, ref failures, ref total);
            CheckDefault("JuggernautSerum", 1, ref failures, ref total);
            CheckDefault("MetalbloodSerum", 1, ref failures, ref total);
            CheckDefault("MindNumbSerum", 1, ref failures, ref total);

            // 默认不携带
            CheckDefault("Luciferium", 0, ref failures, ref total);
            CheckDefault("Beer", 0, ref failures, ref total);
            CheckDefault("SmokeleafJoint", 0, ref failures, ref total);
            CheckDefault("Ambrosia", 0, ref failures, ref total);
            CheckDefault("Flake", 0, ref failures, ref total);
            CheckDefault("Yayo", 0, ref failures, ref total);
            CheckDefault("VoidsightSerum", 0, ref failures, ref total);

            // 目录外的 DefName 默认 0
            CheckDefault("NotARealDef", 0, ref failures, ref total);

            // 类别划分：血清 4 项、药品 10 项
            int drugCount = 0;
            int serumCount = 0;
            for (int i = 0; i < CarryItemCatalog.Items.Length; i++)
            {
                if (CarryItemCatalog.Items[i].Kind == CarryItemKind.Serum) serumCount++;
                else drugCount++;
            }
            CheckInt(drugCount, 10, "目录药品数量=10", ref failures, ref total);
            CheckInt(serumCount, 4, "目录血清数量=4（含虚空视界）", ref failures, ref total);

            // 评级门槛（AutoCarry 派发与 AutoDrugPolicy 写政策共用）
            CheckMinTier("GoJuice", CombatTier.A, ref failures, ref total);
            CheckMinTier("Penoxycyline", CombatTier.A, ref failures, ref total);
            CheckMinTier("JuggernautSerum", CombatTier.S, ref failures, ref total);
            CheckMinTier("MetalbloodSerum", CombatTier.S, ref failures, ref total);
            CheckMinTier("MindNumbSerum", CombatTier.S, ref failures, ref total);
            CheckMinTier("VoidsightSerum", CombatTier.S, ref failures, ref total);
            CheckMinTier("WakeUp", CombatTier.X, ref failures, ref total);
            CheckMinTier("PsychiteTea", CombatTier.X, ref failures, ref total);
            CheckMinTier("Luciferium", CombatTier.X, ref failures, ref total);
            CheckMinTier("Beer", CombatTier.X, ref failures, ref total);

            Console.WriteLine($"[CarryPolicyTests/Catalog] {total - failures}/{total} passed");
            return failures;
        }

        private static void CheckMinTier(string defName, CombatTier expected,
            ref int failures, ref int total)
        {
            total++;
            CombatTier actual = CombatTier.X;
            bool found = false;
            for (int i = 0; i < CarryItemCatalog.Items.Length; i++)
            {
                if (CarryItemCatalog.Items[i].DefName != defName) continue;
                actual = CarryItemCatalog.Items[i].MinTier;
                found = true;
                break;
            }
            if (!found)
            {
                Console.WriteLine($"  FAIL: 目录缺少 {defName}");
                failures++;
            }
            else if (actual != expected)
            {
                Console.WriteLine($"  FAIL: MinTier {defName}: expected {expected}, got {actual}");
                failures++;
            }
        }

        private static void CheckDefault(string defName, int expected, ref int failures, ref int total)
        {
            CheckInt(CarryItemCatalog.GetDefaultCount(defName), expected,
                $"目录默认数量 {defName}", ref failures, ref total);
        }

        // ════════════════════════════════════════════════════════════
        // 4. EffectiveDrugCount：配置数量与政策数量取较小值
        // ════════════════════════════════════════════════════════════

        private static int RunEffectiveDrugCountTests(ref int total)
        {
            int failures = 0;

            CheckEffective(0, 5, 0, "配置 0 → 0（玩家不携带）", ref failures, ref total);
            CheckEffective(0, -1, 0, "配置 0 + 政策无条目 → 0", ref failures, ref total);
            CheckEffective(2, -1, 2, "配置 2 + 政策无条目 → 2", ref failures, ref total);
            CheckEffective(2, 0, 0, "配置 2 + 政策 0 → 0（政策优先，避免被丢）", ref failures, ref total);
            CheckEffective(2, 1, 1, "配置 2 + 政策 1 → 1", ref failures, ref total);
            CheckEffective(2, 3, 2, "配置 2 + 政策 3 → 2（不超配置）", ref failures, ref total);
            CheckEffective(3, 3, 3, "配置 3 + 政策 3 → 3", ref failures, ref total);
            CheckEffective(1, 5, 1, "配置 1 + 政策 5 → 1", ref failures, ref total);

            Console.WriteLine($"[CarryPolicyTests/EffectiveDrugCount] {total - failures}/{total} passed");
            return failures;
        }

        private static void CheckEffective(int configured, int policy, int expected, string label,
            ref int failures, ref int total)
        {
            CheckInt(CarryPolicy.EffectiveDrugCount(configured, policy), expected,
                label, ref failures, ref total);
        }

        // ════════════════════════════════════════════════════════════
        // 5. 携带门槛：清醒丸与血清
        // ════════════════════════════════════════════════════════════

        private static int RunGateTests(ref int total)
        {
            int failures = 0;

            CheckBool(CarryPolicy.ShouldCarryWakeUp(true, false), true,
                "需要睡眠 → 携带清醒丸", ref failures, ref total);
            CheckBool(CarryPolicy.ShouldCarryWakeUp(false, true), true,
                "清醒丸成瘾 → 携带清醒丸", ref failures, ref total);
            CheckBool(CarryPolicy.ShouldCarryWakeUp(false, false), false,
                "不需睡眠且无成瘾 → 不携带清醒丸", ref failures, ref total);

            CheckBool(CarryPolicy.ShouldCarrySerum(true, true), true,
                "高价值 + 仅高价值限制 → 携带血清", ref failures, ref total);
            CheckBool(CarryPolicy.ShouldCarrySerum(false, true), false,
                "非高价值 + 仅高价值限制 → 不携带血清", ref failures, ref total);
            CheckBool(CarryPolicy.ShouldCarrySerum(false, false), true,
                "非高价值但取消限制 → 携带血清", ref failures, ref total);

            // 评级门槛：达到 MinTier 才携带（含高于门槛与无门槛两种情况）
            CheckBool(CarryPolicy.MeetsTierGate(CombatTier.A, CombatTier.A), true,
                "A 档 vs 门槛 A → 携带", ref failures, ref total);
            CheckBool(CarryPolicy.MeetsTierGate(CombatTier.B, CombatTier.A), false,
                "B 档 vs 门槛 A → 不携带", ref failures, ref total);
            CheckBool(CarryPolicy.MeetsTierGate(CombatTier.SSS, CombatTier.S), true,
                "SSS 档 vs 门槛 S → 携带", ref failures, ref total);
            CheckBool(CarryPolicy.MeetsTierGate(CombatTier.C, CombatTier.S), false,
                "C 档 vs 门槛 S → 不携带", ref failures, ref total);
            CheckBool(CarryPolicy.MeetsTierGate(CombatTier.X, CombatTier.X), true,
                "无门槛物品任何档位都携带", ref failures, ref total);

            Console.WriteLine($"[CarryPolicyTests/Gates] {total - failures}/{total} passed");
            return failures;
        }

        private static void CheckBool(bool actual, bool expected, string label,
            ref int failures, ref int total)
        {
            total++;
            if (actual != expected)
            {
                Console.WriteLine($"  FAIL: {label}: expected {expected}, got {actual}");
                failures++;
            }
        }
    }
}
