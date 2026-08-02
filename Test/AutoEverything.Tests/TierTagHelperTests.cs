using System;
using AutoEverything.Core;

namespace AutoEverything.Tests
{
    /// <summary>
    /// TierTagHelper 评级前缀剥离工具的单元测试。
    /// 覆盖 8 个合法 CombatTier 前缀（X/D/C/B/A/S/SS/SSS）的 Strip 与 HasPrefix，
    /// 以及边界场景：无 #、# 首位、前缀超长、空字符串、null、非法前缀。
    /// </summary>
    public static class TierTagHelperTests
    {
        public static int RunAll()
        {
            int failures = 0;
            int total = 0;

            // ── Strip：8 个合法 CombatTier 前缀全部剥离 ─────────────
            CheckStrip("X#王五", "王五", "X# → 剥离", ref failures, ref total);
            CheckStrip("D#王五", "王五", "D# → 剥离", ref failures, ref total);
            CheckStrip("C#王五", "王五", "C# → 剥离", ref failures, ref total);
            CheckStrip("B#王五", "王五", "B# → 剥离", ref failures, ref total);
            CheckStrip("A#王五", "王五", "A# → 剥离", ref failures, ref total);
            CheckStrip("S#王五", "王五", "S# → 剥离", ref failures, ref total);
            CheckStrip("SS#王五", "王五", "SS# → 剥离", ref failures, ref total);
            CheckStrip("SSS#王五", "王五", "SSS# → 剥离", ref failures, ref total);

            // ── HasPrefix：8 个合法 CombatTier 前缀全部识别 ─────────
            CheckHasPrefix("X#王五", true, "X# → true", ref failures, ref total);
            CheckHasPrefix("D#王五", true, "D# → true", ref failures, ref total);
            CheckHasPrefix("C#王五", true, "C# → true", ref failures, ref total);
            CheckHasPrefix("B#王五", true, "B# → true", ref failures, ref total);
            CheckHasPrefix("A#王五", true, "A# → true", ref failures, ref total);
            CheckHasPrefix("S#王五", true, "S# → true", ref failures, ref total);
            CheckHasPrefix("SS#王五", true, "SS# → true", ref failures, ref total);
            CheckHasPrefix("SSS#王五", true, "SSS# → true", ref failures, ref total);

            // ── Strip 边界：无 # / # 首位 / 前缀超长 / 非法前缀 / 空 / null ──
            CheckStrip("王五", "王五", "无 # → 原值", ref failures, ref total);
            CheckStrip("#王五", "#王五", "# 在首位 → 原值", ref failures, ref total);
            CheckStrip("XXXX#王五", "XXXX#王五", "前缀 4 字符超长 → 原值", ref failures, ref total);
            CheckStrip("ABC#王五", "ABC#王五", "3 字符非档次前缀 → 原值", ref failures, ref total);
            CheckStrip("Z#王五", "Z#王五", "1 字符非档次前缀 → 原值", ref failures, ref total);
            CheckStrip("S#", "", "S# 后为空 → 空字符串", ref failures, ref total);
            CheckStrip("S", "S", "无 # 单字符 → 原值", ref failures, ref total);
            CheckStrip("SSS", "SSS", "无 # 多字符 → 原值", ref failures, ref total);
            CheckStrip("", "", "空字符串 → 空字符串", ref failures, ref total);
            CheckStrip(null, null, "null → null", ref failures, ref total);

            // ── HasPrefix 边界：无 # / # 首位 / 前缀超长 / 非法前缀 / 空 / null ──
            CheckHasPrefix("王五", false, "无 # → false", ref failures, ref total);
            CheckHasPrefix("#王五", false, "# 在首位 → false", ref failures, ref total);
            CheckHasPrefix("XXXX#王五", false, "前缀 4 字符超长 → false", ref failures, ref total);
            CheckHasPrefix("ABC#王五", false, "3 字符非档次前缀 → false", ref failures, ref total);
            CheckHasPrefix("Z#王五", false, "1 字符非档次前缀 → false", ref failures, ref total);
            CheckHasPrefix("S", false, "无 # 单字符 → false", ref failures, ref total);
            CheckHasPrefix("S#", true, "S# 后为空 → true（前缀合法）", ref failures, ref total);
            CheckHasPrefix("", false, "空字符串 → false", ref failures, ref total);
            CheckHasPrefix(null, false, "null → false", ref failures, ref total);

            Console.WriteLine($"[TierTagHelperTests] {total - failures}/{total} passed");
            return failures;
        }

        private static void CheckStrip(string input, string expected, string label,
            ref int failures, ref int total)
        {
            total++;
            string actual = TierTagHelper.Strip(input);
            if (actual != expected)
            {
                Console.WriteLine($"  FAIL Strip: {label}: expected \"{expected}\", got \"{actual}\"");
                failures++;
            }
        }

        private static void CheckHasPrefix(string input, bool expected, string label,
            ref int failures, ref int total)
        {
            total++;
            bool actual = TierTagHelper.HasPrefix(input);
            if (actual != expected)
            {
                Console.WriteLine($"  FAIL HasPrefix: {label}: expected {expected}, got {actual}");
                failures++;
            }
        }
    }
}
