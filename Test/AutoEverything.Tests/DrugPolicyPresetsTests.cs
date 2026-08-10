using System;
using AutoEverything.AutoDrugPolicy;
using AutoEverything.Core;

namespace AutoEverything.Tests
{
    /// <summary>
    /// DrugPolicyPresets 评级映射的单元测试。
    ///
    /// 覆盖范围：
    /// 1. CombatTierToDrugTier 映射：S/SS/SSS → S；A/B → AB；C/D/X → CDX
    /// 2. DrugTier 枚举顺序：CDX &lt; AB &lt; S（用于 tier >= 判定叠加药品）
    /// 3. 政策 label 常量：AE-S / AE-AB / AE-CDX（避免与玩家自定义政策冲突）
    ///
    /// 设计原则：测试不依赖 RimWorld 运行时（无 Pawn/DrugPolicy 实例），
    /// 仅调用 internal static 纯逻辑方法验证映射关系。
    /// </summary>
    public static class DrugPolicyPresetsTests
    {
        public static int RunAll()
        {
            int failures = 0;
            int total = 0;

            failures += RunTierMappingTests(ref total);
            failures += RunLabelTests(ref total);
            failures += RunDrugTierOrderTests(ref total);

            return failures;
        }

        // ════════════════════════════════════════════════════════════
        // 1. CombatTierToDrugTier 映射
        // ════════════════════════════════════════════════════════════

        private static int RunTierMappingTests(ref int total)
        {
            int failures = 0;

            // ── S 档：S/SS/SSS 全部映射到 DrugTier.S ──
            CheckTierMapping(CombatTier.S, DrugPolicyPresets.DrugTier.S,
                "S → S 档", ref failures, ref total);
            CheckTierMapping(CombatTier.SS, DrugPolicyPresets.DrugTier.S,
                "SS → S 档", ref failures, ref total);
            CheckTierMapping(CombatTier.SSS, DrugPolicyPresets.DrugTier.S,
                "SSS → S 档", ref failures, ref total);

            // ── AB 档：A/B 映射到 DrugTier.AB ──
            CheckTierMapping(CombatTier.A, DrugPolicyPresets.DrugTier.AB,
                "A → AB 档", ref failures, ref total);
            CheckTierMapping(CombatTier.B, DrugPolicyPresets.DrugTier.AB,
                "B → AB 档", ref failures, ref total);

            // ── CDX 档：C/D/X 映射到 DrugTier.CDX ──
            CheckTierMapping(CombatTier.C, DrugPolicyPresets.DrugTier.CDX,
                "C → CDX 档", ref failures, ref total);
            CheckTierMapping(CombatTier.D, DrugPolicyPresets.DrugTier.CDX,
                "D → CDX 档", ref failures, ref total);
            CheckTierMapping(CombatTier.X, DrugPolicyPresets.DrugTier.CDX,
                "X → CDX 档", ref failures, ref total);

            Console.WriteLine($"[DrugPolicyPresetsTests/TierMapping] {total - failures}/{total} passed");
            return failures;
        }

        private static void CheckTierMapping(CombatTier input, DrugPolicyPresets.DrugTier expected,
            string label, ref int failures, ref int total)
        {
            total++;
            DrugPolicyPresets.DrugTier actual = DrugPolicyPresets.CombatTierToDrugTier(input);
            if (actual != expected)
            {
                Console.WriteLine($"  FAIL: {label}: expected {expected}, got {actual}");
                failures++;
            }
        }

        // ════════════════════════════════════════════════════════════
        // 2. 政策 label 常量
        // ════════════════════════════════════════════════════════════

        private static int RunLabelTests(ref int total)
        {
            int failures = 0;

            CheckLabel(DrugPolicyPresets.PolicyLabelS, "AE-S", "S 档政策 label", ref failures, ref total);
            CheckLabel(DrugPolicyPresets.PolicyLabelAB, "AE-AB", "AB 档政策 label", ref failures, ref total);
            CheckLabel(DrugPolicyPresets.PolicyLabelCDX, "AE-CDX", "CDX 档政策 label", ref failures, ref total);

            Console.WriteLine($"[DrugPolicyPresetsTests/Label] {total - failures}/{total} passed");
            return failures;
        }

        private static void CheckLabel(string actual, string expected, string label,
            ref int failures, ref int total)
        {
            total++;
            if (actual != expected)
            {
                Console.WriteLine($"  FAIL: {label}: expected [{expected}], got [{actual}]");
                failures++;
            }
        }

        // ════════════════════════════════════════════════════════════
        // 3. DrugTier 枚举顺序：CDX < AB < S
        // 用于 FillPolicyEntries 的 tier >= 判定：高档位包含低档位的所有药品
        // ════════════════════════════════════════════════════════════

        private static int RunDrugTierOrderTests(ref int total)
        {
            int failures = 0;

            // ── CDX < AB < S（确保叠加逻辑正确）──
            CheckOrder(DrugPolicyPresets.DrugTier.CDX, DrugPolicyPresets.DrugTier.AB,
                "CDX < AB", ref failures, ref total);
            CheckOrder(DrugPolicyPresets.DrugTier.AB, DrugPolicyPresets.DrugTier.S,
                "AB < S", ref failures, ref total);
            CheckOrder(DrugPolicyPresets.DrugTier.CDX, DrugPolicyPresets.DrugTier.S,
                "CDX < S", ref failures, ref total);

            // ── tier >= 判定（高档位包含低档位药品）──
            // AB >= CDX：AB 档包含 CDX 的活力水+清醒丸
            CheckGreaterEqual(DrugPolicyPresets.DrugTier.AB, DrugPolicyPresets.DrugTier.CDX,
                "AB >= CDX（AB 档包含 CDX 药品）", ref failures, ref total);
            // S >= AB：S 档包含 AB 的思滞血清
            CheckGreaterEqual(DrugPolicyPresets.DrugTier.S, DrugPolicyPresets.DrugTier.AB,
                "S >= AB（S 档包含 AB 药品）", ref failures, ref total);
            // S >= CDX：S 档包含 CDX 药品（间接，通过 AB 档包含 CDX）
            CheckGreaterEqual(DrugPolicyPresets.DrugTier.S, DrugPolicyPresets.DrugTier.CDX,
                "S >= CDX（S 档包含 CDX 药品）", ref failures, ref total);

            Console.WriteLine($"[DrugPolicyPresetsTests/TierOrder] {total - failures}/{total} passed");
            return failures;
        }

        private static void CheckOrder(DrugPolicyPresets.DrugTier lower, DrugPolicyPresets.DrugTier higher,
            string label, ref int failures, ref int total)
        {
            total++;
            if (!(lower < higher))
            {
                Console.WriteLine($"  FAIL: {label}: 期望 {lower} < {higher}");
                failures++;
            }
        }

        private static void CheckGreaterEqual(DrugPolicyPresets.DrugTier a, DrugPolicyPresets.DrugTier b,
            string label, ref int failures, ref int total)
        {
            total++;
            if (!(a >= b))
            {
                Console.WriteLine($"  FAIL: {label}: 期望 {a} >= {b}");
                failures++;
            }
        }
    }
}
