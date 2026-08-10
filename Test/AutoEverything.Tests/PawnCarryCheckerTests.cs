using System;
using AutoEverything.AutoCarry;

namespace AutoEverything.Tests
{
    /// <summary>
    /// PawnCarryChecker 纯逻辑核心的单元测试。
    ///
    /// 覆盖范围：
    /// 1. NeedSleepCore — null（不需要睡眠）与非 null（需要睡眠）判定
    /// 2. LuciferiumAllowedCore — 4 种组合（hasEntry × entryAllowed），验证「无条目默认允许」「有条目按 allowed」
    ///
    /// 设计原则：测试不依赖 RimWorld 运行时（无 Pawn/DrugPolicy 实例），
    /// 仅调用 internal static 纯逻辑方法验证软过滤判定。
    /// </summary>
    public static class PawnCarryCheckerTests
    {
        public static int RunAll()
        {
            int failures = 0;
            int total = 0;

            failures += RunNeedSleepTests(ref total);
            failures += RunLuciferiumAllowedTests(ref total);

            return failures;
        }

        // ════════════════════════════════════════════════════════════
        // 1. NeedSleepCore：根据 Need_Rest 是否为 null 判定
        // ════════════════════════════════════════════════════════════

        private static int RunNeedSleepTests(ref int total)
        {
            int failures = 0;

            // ── null 表示不需要睡眠 ─────────────────────────────────
            CheckNeedSleep(null, false, "null → 不需要睡眠", ref failures, ref total);

            // ── 非 null 表示需要睡眠（任意对象都视为有睡眠需求）──────
            CheckNeedSleep(new object(), true, "非 null → 需要睡眠", ref failures, ref total);

            Console.WriteLine($"[PawnCarryCheckerTests/NeedSleep] {total - failures}/{total} passed");
            return failures;
        }

        private static void CheckNeedSleep(object restNeed, bool expected, string label,
            ref int failures, ref int total)
        {
            total++;
            bool actual = PawnCarryChecker.NeedSleepCore(restNeed);
            if (actual != expected)
            {
                Console.WriteLine($"  FAIL: {label}: expected {expected}, got {actual}");
                failures++;
            }
        }

        // ════════════════════════════════════════════════════════════
        // 2. LuciferiumAllowedCore：药品政策条目判定
        // ════════════════════════════════════════════════════════════

        private static int RunLuciferiumAllowedTests(ref int total)
        {
            int failures = 0;

            // ── 无条目：不允许携带（GetDrugCarryCount 返回 -1 → LuciferiumAllowed 返回 false）──
            // 语义：政策未配置该药品，玩家没说要带，AutoCarry 不主动带
            CheckLuciferium(false, false, false, "无条目 → 不允许（政策未配置）", ref failures, ref total);
            CheckLuciferium(false, true, false, "无条目（entryAllowed 忽略）→ 不允许", ref failures, ref total);

            // ── 有条目且 allowed=true：允许 ─────────────────────────
            CheckLuciferium(true, true, true, "有条目 allowed=true → 允许", ref failures, ref total);

            // ── 有条目且 allowed=false：禁止（玩家在药品政策中禁用）──
            CheckLuciferium(true, false, false, "有条目 allowed=false → 禁止", ref failures, ref total);

            Console.WriteLine($"[PawnCarryCheckerTests/LuciferiumAllowed] {total - failures}/{total} passed");
            return failures;
        }

        private static void CheckLuciferium(bool hasEntry, bool entryAllowed, bool expected,
            string label, ref int failures, ref int total)
        {
            total++;
            bool actual = PawnCarryChecker.LuciferiumAllowedCore(hasEntry, entryAllowed);
            if (actual != expected)
            {
                Console.WriteLine($"  FAIL: {label}: expected {expected}, got {actual}");
                failures++;
            }
        }
    }
}
