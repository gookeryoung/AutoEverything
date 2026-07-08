using System;
using AutoEverything.AutoWork;

namespace AutoEverything.Tests
{
    /// <summary>
    /// ApplySkillFloorCore 纯逻辑核心的单元测试。
    /// 覆盖 priority 0/1/2/3/4 × skillLevel 0/7/8/11/12/20 全部边界组合，
    /// 验证「技能≥12 保底 2、≥8 保底 3」的规则契约。
    /// </summary>
    public static class ApplySkillFloorCoreTests
    {
        public static int RunAll()
        {
            int failures = 0;
            int total = 0;

            // ── skillLevel=0（无技能保底）──────────────────────────────
            Check(0, 0, 0, "skill=0 priority=0 → 0", ref failures, ref total);
            Check(1, 0, 1, "skill=0 priority=1 → 1", ref failures, ref total);
            Check(2, 0, 2, "skill=0 priority=2 → 2", ref failures, ref total);
            Check(3, 0, 3, "skill=0 priority=3 → 3", ref failures, ref total);
            Check(4, 0, 4, "skill=0 priority=4 → 4", ref failures, ref total);

            // ── skillLevel=7（<8 无保底）───────────────────────────────
            Check(0, 7, 0, "skill=7 priority=0 → 0", ref failures, ref total);
            Check(1, 7, 1, "skill=7 priority=1 → 1", ref failures, ref total);
            Check(2, 7, 2, "skill=7 priority=2 → 2", ref failures, ref total);
            Check(3, 7, 3, "skill=7 priority=3 → 3", ref failures, ref total);
            Check(4, 7, 4, "skill=7 priority=4 → 4", ref failures, ref total);

            // ── skillLevel=8（≥8 保底 3，覆盖 priority=0/4）───────────
            Check(0, 8, 3, "skill=8 priority=0 → 3 (保底)", ref failures, ref total);
            Check(1, 8, 1, "skill=8 priority=1 → 1 (已满足)", ref failures, ref total);
            Check(2, 8, 2, "skill=8 priority=2 → 2 (已满足)", ref failures, ref total);
            Check(3, 8, 3, "skill=8 priority=3 → 3 (已满足不降级)", ref failures, ref total);
            Check(4, 8, 3, "skill=8 priority=4 → 3 (保底降级)", ref failures, ref total);

            // ── skillLevel=11（≥8 保底 3，边界值）─────────────────────
            Check(0, 11, 3, "skill=11 priority=0 → 3 (保底)", ref failures, ref total);
            Check(1, 11, 1, "skill=11 priority=1 → 1 (已满足)", ref failures, ref total);
            Check(2, 11, 2, "skill=11 priority=2 → 2 (已满足)", ref failures, ref total);
            Check(3, 11, 3, "skill=11 priority=3 → 3 (已满足不降级)", ref failures, ref total);
            Check(4, 11, 3, "skill=11 priority=4 → 3 (保底降级)", ref failures, ref total);

            // ── skillLevel=12（≥12 保底 2，覆盖 priority=0/3/4）───────
            Check(0, 12, 2, "skill=12 priority=0 → 2 (保底)", ref failures, ref total);
            Check(1, 12, 1, "skill=12 priority=1 → 1 (已满足)", ref failures, ref total);
            Check(2, 12, 2, "skill=12 priority=2 → 2 (已满足)", ref failures, ref total);
            Check(3, 12, 2, "skill=12 priority=3 → 2 (保底提升)", ref failures, ref total);
            Check(4, 12, 2, "skill=12 priority=4 → 2 (保底降级)", ref failures, ref total);

            // ── skillLevel=20（≥12 保底 2，满级）──────────────────────
            Check(0, 20, 2, "skill=20 priority=0 → 2 (保底)", ref failures, ref total);
            Check(1, 20, 1, "skill=20 priority=1 → 1 (已满足)", ref failures, ref total);
            Check(2, 20, 2, "skill=20 priority=2 → 2 (已满足)", ref failures, ref total);
            Check(3, 20, 2, "skill=20 priority=3 → 2 (保底提升)", ref failures, ref total);
            Check(4, 20, 2, "skill=20 priority=4 → 2 (保底降级)", ref failures, ref total);

            Console.WriteLine($"[ApplySkillFloorCoreTests] {total - failures}/{total} passed");
            return failures;
        }

        private static void Check(int priority, int skillLevel, int expected, string label,
            ref int failures, ref int total)
        {
            total++;
            int actual = WorkAllocator.ApplySkillFloorCore(priority, skillLevel);
            if (actual != expected)
            {
                Console.WriteLine($"  FAIL: {label}: expected {expected}, got {actual}");
                failures++;
            }
        }
    }
}
