using System;
using System.Collections.Generic;
using AutoEverything.AutoEquipment;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.Tests
{
    /// <summary>
    /// GearAllocator 纯逻辑核心的单元测试。
    ///
    /// 覆盖范围：
    /// 1. ComputeHeavyUpgradeFlags：重甲顺延名额计算
    ///    - 重甲数量 ≥ Pawn 数量：所有 Flexible 升级
    ///    - 重甲数量 < Pawn 数量：按 CombatTier 降序前 N 个 Flexible 升级，其余保持
    ///    - 无 Heavy Pawn + 无重甲：无升级
    ///    - Light Pawn 始终不升级（保工作效率）
    ///    - Heavy Pawn 已占名额，不参与顺延
    ///    - 边界：空列表、全 Heavy、全 Light、负数防御（heavyPawnCount > heavyArmorCount）
    /// 2. ShouldStealFromWearerCore：扒装守卫得分比较
    ///    - stealer 得分明显高于 wearer → 允许扒装
    ///    - stealer 得分接近 wearer（差值 ≤ 阈值）→ 拒绝扒装，防振荡
    ///    - 边界：差值恰好等于阈值（严格 >，拒绝）、负数得分、零阈值
    ///
    /// 设计原则：测试不依赖 RimWorld 运行时（无 Pawn/Map/Apparel 实例），
    /// 仅调用 internal static 纯逻辑方法，构造数值输入即可验证。
    ///
    /// 业务契约（与 README.md "扒装守卫"章节同步）：
    /// - ShouldStealFromWearerCore(stealerScore, wearerScore, threshold) =
    ///   stealerScore - wearerScore > threshold
    /// - 严格大于（非 ≥）确保得分相当时不扒装，避免边际抢装振荡
    /// </summary>
    public static class GearAllocatorTests
    {
        public static int RunAll()
        {
            int failures = 0;
            int total = 0;

            failures += RunEmptyAndBoundaryTests(ref total);
            failures += RunAllFlexibleTests(ref total);
            failures += RunMixedTests(ref total);
            failures += RunLightNeverUpgradeTests(ref total);
            failures += RunHeavyAlreadyOccupiedTests(ref total);
            failures += RunPriorityOrderTests(ref total);
            failures += RunStealGuardTests(ref total);

            return failures;
        }

        // ════════════════════════════════════════════════════════════
        // 1. 边界：空列表 / 全空 / 负数防御
        // ════════════════════════════════════════════════════════════

        private static int RunEmptyAndBoundaryTests(ref int total)
        {
            int failures = 0;

            // 空列表：无任何候选 Pawn → 无升级标志
            Check(0, 0, new List<ArmorPreference>(), new bool[0],
                "empty prefs → empty flags", ref failures, ref total);

            // 空列表 + 有重甲：仍返回空（无 Pawn 可升级）
            Check(5, 0, new List<ArmorPreference>(), new bool[0],
                "empty prefs + 5 heavy armor → empty flags", ref failures, ref total);

            // heavyPawnCount > heavyArmorCount：remaining=0，无升级（防御负数）
            // 场景：3 个 Heavy Pawn 但只有 1 件重甲 → 名额已被 Heavy 占满，Flexible 无顺延
            Check(1, 3, ListOf(ArmorPreference.Flexible), new bool[] { false },
                "1 armor + 3 Heavy pawns + 1 Flexible → no upgrade (remaining=0)", ref failures, ref total);

            // heavyPawnCount == heavyArmorCount：remaining=0，Flexible 无顺延
            Check(2, 2, ListOf(ArmorPreference.Flexible, ArmorPreference.Flexible),
                new bool[] { false, false },
                "2 armor + 2 Heavy pawns + 2 Flexible → no upgrade (remaining=0)", ref failures, ref total);

            Console.WriteLine($"[GearAllocatorTests/EmptyBoundary] {total - failures}/{total} passed");
            return failures;
        }

        // ════════════════════════════════════════════════════════════
        // 2. 全 Flexible：重甲数量决定升级名额
        // ════════════════════════════════════════════════════════════

        private static int RunAllFlexibleTests(ref int total)
        {
            int failures = 0;

            // 3 Flexible + 0 Heavy Pawn + 0 重甲：无升级
            Check(0, 0,
                ListOf(ArmorPreference.Flexible, ArmorPreference.Flexible, ArmorPreference.Flexible),
                new bool[] { false, false, false },
                "3 Flexible + 0 armor + 0 Heavy pawn → no upgrade", ref failures, ref total);

            // 3 Flexible + 0 Heavy Pawn + 1 重甲：第 1 个升级（按顺序）
            Check(1, 0,
                ListOf(ArmorPreference.Flexible, ArmorPreference.Flexible, ArmorPreference.Flexible),
                new bool[] { true, false, false },
                "3 Flexible + 1 armor + 0 Heavy pawn → first upgrade", ref failures, ref total);

            // 3 Flexible + 0 Heavy Pawn + 2 重甲：前 2 个升级
            Check(2, 0,
                ListOf(ArmorPreference.Flexible, ArmorPreference.Flexible, ArmorPreference.Flexible),
                new bool[] { true, true, false },
                "3 Flexible + 2 armor + 0 Heavy pawn → first 2 upgrade", ref failures, ref total);

            // 3 Flexible + 0 Heavy Pawn + 3 重甲：全部升级
            Check(3, 0,
                ListOf(ArmorPreference.Flexible, ArmorPreference.Flexible, ArmorPreference.Flexible),
                new bool[] { true, true, true },
                "3 Flexible + 3 armor + 0 Heavy pawn → all upgrade", ref failures, ref total);

            // 3 Flexible + 0 Heavy Pawn + 5 重甲（重甲多于 Pawn）：全部升级（不会超额）
            Check(5, 0,
                ListOf(ArmorPreference.Flexible, ArmorPreference.Flexible, ArmorPreference.Flexible),
                new bool[] { true, true, true },
                "3 Flexible + 5 armor (excess) + 0 Heavy pawn → all 3 upgrade", ref failures, ref total);

            Console.WriteLine($"[GearAllocatorTests/AllFlexible] {total - failures}/{total} passed");
            return failures;
        }

        // ════════════════════════════════════════════════════════════
        // 3. 混合：Heavy / Flexible / Light 共存
        // ════════════════════════════════════════════════════════════

        private static int RunMixedTests(ref int total)
        {
            int failures = 0;

            // 1 Heavy + 2 Flexible + 1 Light + 3 重甲：
            // remaining = max(0, 3-1) = 2 → 2 个 Flexible 都升级，Light 不动
            Check(3, 1,
                ListOf(ArmorPreference.Heavy, ArmorPreference.Flexible, ArmorPreference.Flexible, ArmorPreference.Light),
                new bool[] { false, true, true, false },
                "1H+2F+1L + 3 armor + 1 Heavy pawn → 2 Flexible upgrade, Light skip", ref failures, ref total);

            // 1 Heavy + 2 Flexible + 1 Light + 2 重甲：
            // remaining = max(0, 2-1) = 1 → 仅第 1 个 Flexible 升级
            Check(2, 1,
                ListOf(ArmorPreference.Heavy, ArmorPreference.Flexible, ArmorPreference.Flexible, ArmorPreference.Light),
                new bool[] { false, true, false, false },
                "1H+2F+1L + 2 armor + 1 Heavy pawn → 1 Flexible upgrade", ref failures, ref total);

            // 1 Heavy + 2 Flexible + 1 Light + 1 重甲：
            // remaining = max(0, 1-1) = 0 → 无 Flexible 升级（重甲仅够 Heavy 占用）
            Check(1, 1,
                ListOf(ArmorPreference.Heavy, ArmorPreference.Flexible, ArmorPreference.Flexible, ArmorPreference.Light),
                new bool[] { false, false, false, false },
                "1H+2F+1L + 1 armor + 1 Heavy pawn → no Flexible upgrade", ref failures, ref total);

            Console.WriteLine($"[GearAllocatorTests/Mixed] {total - failures}/{total} passed");
            return failures;
        }

        // ════════════════════════════════════════════════════════════
        // 4. Light 始终不升级（保工作效率的核心契约）
        // ════════════════════════════════════════════════════════════

        private static int RunLightNeverUpgradeTests(ref int total)
        {
            int failures = 0;

            // 全 Light + 大量重甲：无任何升级
            Check(10, 0,
                ListOf(ArmorPreference.Light, ArmorPreference.Light, ArmorPreference.Light),
                new bool[] { false, false, false },
                "3 Light + 10 armor → no upgrade (Light never upgrades)", ref failures, ref total);

            // 混合：Light 在 Flexible 之前，Flexible 升级而 Light 不动
            // remaining=2，Light[0] 不升，Flexible[1] 升，Flexible[2] 升
            Check(2, 0,
                ListOf(ArmorPreference.Light, ArmorPreference.Flexible, ArmorPreference.Flexible),
                new bool[] { false, true, true },
                "1L+2F + 2 armor → Light skip, 2 Flexible upgrade", ref failures, ref total);

            // 混合：Light 穿插在中间，不影响 Flexible 名额消耗顺序
            // remaining=1，Flexible[0] 升，Light[1] 不升，Flexible[2] 不升（名额已耗尽）
            Check(1, 0,
                ListOf(ArmorPreference.Flexible, ArmorPreference.Light, ArmorPreference.Flexible),
                new bool[] { true, false, false },
                "F+L+F + 1 armor → first Flexible upgrade, Light skip, last Flexible no quota", ref failures, ref total);

            Console.WriteLine($"[GearAllocatorTests/LightNeverUpgrade] {total - failures}/{total} passed");
            return failures;
        }

        // ════════════════════════════════════════════════════════════
        // 5. Heavy 已占名额，不参与顺延（Heavy 在 sortedPrefs 中始终 false）
        // ════════════════════════════════════════════════════════════

        private static int RunHeavyAlreadyOccupiedTests(ref int total)
        {
            int failures = 0;

            // 全 Heavy + 大量重甲：Heavy 已占名额，flags 全 false（不需要再"升级"为 Heavy）
            Check(10, 3,
                ListOf(ArmorPreference.Heavy, ArmorPreference.Heavy, ArmorPreference.Heavy),
                new bool[] { false, false, false },
                "3 Heavy + 10 armor → no upgrade (Heavy already Heavy)", ref failures, ref total);

            // 1 Heavy + 1 Flexible + 1 重甲：remaining = max(0, 1-1) = 0 → Flexible 不升级
            Check(1, 1,
                ListOf(ArmorPreference.Heavy, ArmorPreference.Flexible),
                new bool[] { false, false },
                "1H+1F + 1 armor + 1 Heavy pawn → Flexible no quota", ref failures, ref total);

            // 1 Heavy + 1 Flexible + 2 重甲：remaining = max(0, 2-1) = 1 → Flexible 升级
            Check(2, 1,
                ListOf(ArmorPreference.Heavy, ArmorPreference.Flexible),
                new bool[] { false, true },
                "1H+1F + 2 armor + 1 Heavy pawn → Flexible upgrade", ref failures, ref total);

            Console.WriteLine($"[GearAllocatorTests/HeavyAlreadyOccupied] {total - failures}/{total} passed");
            return failures;
        }

        // ════════════════════════════════════════════════════════════
        // 6. 优先级顺序：按 sortedPrefs 顺序消耗名额（应已按 CombatTier 降序排好）
        // ════════════════════════════════════════════════════════════

        private static int RunPriorityOrderTests(ref int total)
        {
            int failures = 0;

            // 5 Flexible + 2 重甲 + 0 Heavy Pawn：前 2 个升级
            // 模拟 CombatTier 降序：SSS/SS/S/A/B 中的前 2 个（SSS/SS）升级
            Check(2, 0,
                ListOf(ArmorPreference.Flexible, ArmorPreference.Flexible, ArmorPreference.Flexible,
                       ArmorPreference.Flexible, ArmorPreference.Flexible),
                new bool[] { true, true, false, false, false },
                "5 Flexible + 2 armor → first 2 (highest tier) upgrade", ref failures, ref total);

            // 穿插 Heavy/Light：Heavy[0] + Flexible[1] + Light[2] + Flexible[3] + Flexible[4]
            // 2 重甲 + 1 Heavy Pawn → remaining = max(0, 2-1) = 1
            // Heavy[0]=false（已占），Flexible[1]=true（名额 1→0），Light[2]=false，Flexible[3]=false（名额耗尽），Flexible[4]=false
            Check(2, 1,
                ListOf(ArmorPreference.Heavy, ArmorPreference.Flexible, ArmorPreference.Light,
                       ArmorPreference.Flexible, ArmorPreference.Flexible),
                new bool[] { false, true, false, false, false },
                "H+F+L+F+F + 2 armor + 1 Heavy pawn → only first Flexible upgrade", ref failures, ref total);

            // 名额恰好等于 Flexible 数：所有 Flexible 升级
            // 3 Flexible + 1 Heavy + 4 重甲 + 1 Heavy Pawn → remaining = max(0, 4-1) = 3
            Check(4, 1,
                ListOf(ArmorPreference.Flexible, ArmorPreference.Flexible, ArmorPreference.Flexible, ArmorPreference.Heavy),
                new bool[] { true, true, true, false },
                "3F+1H + 4 armor + 1 Heavy pawn → 3 Flexible upgrade, Heavy skip", ref failures, ref total);

            Console.WriteLine($"[GearAllocatorTests/PriorityOrder] {total - failures}/{total} passed");
            return failures;
        }

        // ════════════════════════════════════════════════════════════
        // 7. 扒装守卫 ShouldStealFromWearerCore：stealer vs wearer 得分比较
        //    防止两个 Pawn 之间反复抢装导致振荡
        // ════════════════════════════════════════════════════════════

        private static int RunStealGuardTests(ref int total)
        {
            int failures = 0;

            // stealer 得分明显高于 wearer → 允许扒装
            CheckSteal(15f, 10f, 0.5f, true,
                "stealer 15 > wearer 10 + 0.5 → allow", ref failures, ref total);
            CheckSteal(20f, 10f, 5f, true,
                "stealer 20 > wearer 10 + 5 → allow", ref failures, ref total);

            // stealer 得分接近 wearer（差值 = 阈值）→ 拒绝（严格 >）
            CheckSteal(10.5f, 10f, 0.5f, false,
                "stealer 10.5 == wearer 10 + 0.5 (boundary) → decline", ref failures, ref total);
            CheckSteal(15f, 10f, 5f, false,
                "stealer 15 == wearer 10 + 5 (boundary) → decline", ref failures, ref total);

            // stealer 得分仅略高于 wearer（差值 < 阈值）→ 拒绝
            CheckSteal(10.3f, 10f, 0.5f, false,
                "stealer 10.3 < wearer 10 + 0.5 → decline", ref failures, ref total);
            CheckSteal(12f, 10f, 5f, false,
                "stealer 12 < wearer 10 + 5 → decline", ref failures, ref total);

            // stealer 得分等于 wearer → 拒绝
            CheckSteal(10f, 10f, 0.5f, false,
                "stealer == wearer → decline", ref failures, ref total);
            CheckSteal(10f, 10f, 0f, false,
                "stealer == wearer, threshold 0 → decline (strict >)", ref failures, ref total);

            // stealer 得分低于 wearer → 拒绝
            CheckSteal(8f, 10f, 0.5f, false,
                "stealer < wearer → decline", ref failures, ref total);
            CheckSteal(5f, 10f, 0.5f, false,
                "stealer much < wearer → decline", ref failures, ref total);

            // 负数得分：stealer 仍高于 wearer + 阈值 → 允许
            CheckSteal(-5f, -10f, 0.5f, true,
                "negative scores: stealer -5 > wearer -10 + 0.5 → allow", ref failures, ref total);
            CheckSteal(-5f, -5f, 0.5f, false,
                "negative scores equal → decline", ref failures, ref total);

            // 零阈值：仍要求严格 >（防止得分相同时反复抢装）
            CheckSteal(10.0001f, 10f, 0f, true,
                "threshold 0: stealer slightly > wearer → allow", ref failures, ref total);
            CheckSteal(10f, 10f, 0f, false,
                "threshold 0: equal → decline", ref failures, ref total);

            // 极端值：stealer MaxValue → 允许
            CheckSteal(float.MaxValue, 10f, 0.5f, true,
                "stealer MaxValue → allow", ref failures, ref total);
            // 极端值：wearer MaxValue → 拒绝（stealer 不可能更高）
            CheckSteal(10f, float.MaxValue, 0.5f, false,
                "wearer MaxValue → decline", ref failures, ref total);
            // 极端值：wearer MinValue → 允许（stealer 任何有限值都更高）
            CheckSteal(0f, float.MinValue, 0.5f, true,
                "wearer MinValue → allow", ref failures, ref total);

            // 振荡场景模拟：A 抢 B 的 Y，下轮 B 抢回
            // 假设 Score(Y, A) = 11, Score(Y, B) = 12, threshold = 0.5
            // 第一轮 A 想扒 B 的 Y：ShouldSteal(stealer=A=11, wearer=B=12) → 11-12=-1 < 0.5 → 拒绝
            // 这正是防振荡的关键：A 不会抢 B 的 Y，因为 B 得分更高
            CheckSteal(11f, 12f, 0.5f, false,
                "oscillation guard: A(11) should not steal from B(12)", ref failures, ref total);
            // 反过来：B 想扒 A 的 Y：ShouldSteal(stealer=B=12, wearer=A=11) → 12-11=1 > 0.5 → 允许
            CheckSteal(12f, 11f, 0.5f, true,
                "oscillation guard: B(12) may steal from A(11)", ref failures, ref total);

            Console.WriteLine($"[GearAllocatorTests/StealGuard] {total - failures}/{total} passed");
            return failures;
        }

        // ════════════════════════════════════════════════════════════
        // 辅助方法
        // ════════════════════════════════════════════════════════════

        private static List<ArmorPreference> ListOf(params ArmorPreference[] arr)
        {
            return new List<ArmorPreference>(arr);
        }

        private static void Check(
            int heavyArmorCount, int heavyPawnCount,
            List<ArmorPreference> sortedPrefs, bool[] expected, string label,
            ref int failures, ref int total)
        {
            total++;
            bool[] actual = GearAllocator.ComputeHeavyUpgradeFlags(heavyArmorCount, heavyPawnCount, sortedPrefs);

            if (actual.Length != expected.Length)
            {
                Console.WriteLine($"  FAIL: {label}: length mismatch, expected {expected.Length}, got {actual.Length}");
                failures++;
                return;
            }

            for (int i = 0; i < expected.Length; i++)
            {
                if (actual[i] != expected[i])
                {
                    Console.WriteLine($"  FAIL: {label}: index {i} expected {expected[i]}, got {actual[i]}");
                    Console.WriteLine($"        full expected: [{string.Join(", ", BoolStr(expected))}]");
                    Console.WriteLine($"        full actual:   [{string.Join(", ", BoolStr(actual))}]");
                    failures++;
                    return;
                }
            }
        }

        private static void CheckSteal(
            float stealerScore, float wearerScore, float threshold, bool expected, string label,
            ref int failures, ref int total)
        {
            total++;
            bool actual = GearAllocator.ShouldStealFromWearerCore(stealerScore, wearerScore, threshold);
            if (actual != expected)
            {
                Console.WriteLine($"  FAIL: {label}: expected {expected}, got {actual}");
                Console.WriteLine($"        stealer={stealerScore}, wearer={wearerScore}, threshold={threshold}, diff={stealerScore - wearerScore}");
                failures++;
            }
        }

        private static string[] BoolStr(bool[] arr)
        {
            string[] s = new string[arr.Length];
            for (int i = 0; i < arr.Length; i++) s[i] = arr[i] ? "true" : "false";
            return s;
        }
    }
}
