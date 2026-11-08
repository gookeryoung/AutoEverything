using System;
using AutoEverything.AutoMarkPawn;

namespace AutoEverything.Tests
{
    /// <summary>
    /// ScarMarkRules 伤痕判定纯函数测试。
    /// 覆盖两类线上回归：
    /// 1. 新鲜伤口（IsPermanent=True 但尚未愈合）不应显示条纹；
    /// 2. 仿生/假肢替换造成的 MissingPart 不应算作缺失部位。
    /// 同时验证疤痕显现边界与条数封顶契约。
    /// </summary>
    public static class ScarMarkTests
    {
        public static int RunAll()
        {
            int failures = 0;
            int total = 0;

            // ── 疤痕显现：IsPermanent 只是"将来会留疤"，不等于已显现 ──
            CheckScar(false, 6f, 2.5f, false, "非永久伤口 → 不计", ref failures, ref total);
            CheckScar(true, 6f, 2.5f, false, "新鲜永久伤（未愈合到阈值）→ 不计", ref failures, ref total);
            CheckScar(true, 2.5f, 2.5f, true, "愈合到阈值（等于）→ 计为疤痕", ref failures, ref total);
            CheckScar(true, 0.8f, 1.2f, true, "低于阈值（已愈合静止）→ 计为疤痕", ref failures, ref total);
            CheckScar(true, 0f, 0f, true, "脆弱部位愈合到 0 → 计为疤痕", ref failures, ref total);
            CheckScar(true, 0.3f, 0f, false, "脆弱部位仍在愈合 → 不计", ref failures, ref total);
            CheckScar(true, 30f, ScarMarkRules.InstantPermanentThreshold, true,
                "InstantPermanentInjury（哨兵阈值）→ 立即计为疤痕", ref failures, ref total);

            // ── 缺失部位：被 AddedPart 覆盖视为替换，不算伤痕 ──
            CheckMissing(true, false, "仿生/假肢覆盖的缺失部位 → 不计", ref failures, ref total);
            CheckMissing(false, true, "真实缺失部位 → 计", ref failures, ref total);

            // ── 条数封顶：0~3 ──
            CheckCap(-1, 0, "负数 → 0", ref failures, ref total);
            CheckCap(0, 0, "0 → 0", ref failures, ref total);
            CheckCap(1, 1, "1 → 1", ref failures, ref total);
            CheckCap(3, 3, "3 → 3", ref failures, ref total);
            CheckCap(4, 3, "4 → 封顶 3", ref failures, ref total);
            CheckCap(99, 3, "99 → 封顶 3", ref failures, ref total);

            Console.WriteLine($"[ScarMarkTests] {total - failures}/{total} passed");
            return failures;
        }

        private static void CheckScar(bool isPermanent, float severity, float threshold,
            bool expected, string label, ref int failures, ref int total)
        {
            total++;
            bool actual = ScarMarkRules.IsScarRevealed(isPermanent, severity, threshold);
            if (actual != expected)
            {
                Console.WriteLine($"  FAIL IsScarRevealed: {label}: expected {expected}, got {actual}");
                failures++;
            }
        }

        private static void CheckMissing(bool coveredByAddedPart, bool expected, string label,
            ref int failures, ref int total)
        {
            total++;
            bool actual = ScarMarkRules.ShouldCountMissingPart(coveredByAddedPart);
            if (actual != expected)
            {
                Console.WriteLine($"  FAIL ShouldCountMissingPart: {label}: expected {expected}, got {actual}");
                failures++;
            }
        }

        private static void CheckCap(int count, int expected, string label,
            ref int failures, ref int total)
        {
            total++;
            int actual = ScarMarkRules.CapBars(count);
            if (actual != expected)
            {
                Console.WriteLine($"  FAIL CapBars: {label}: expected {expected}, got {actual}");
                failures++;
            }
        }
    }
}
