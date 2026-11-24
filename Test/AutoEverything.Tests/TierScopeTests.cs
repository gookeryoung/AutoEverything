using System;
using AutoEverything.Core;

namespace AutoEverything.Tests
{
    /// <summary>
    /// TierScopeRules 人员评级范围规则的单元测试。
    /// 覆盖分类优先级（囚犯 &gt; 奴隶 &gt; 访问者 &gt; 殖民者）与三组开关的组合真值表。
    /// </summary>
    public static class TierScopeTests
    {
        public static int RunAll()
        {
            int failures = 0;
            int total = 0;

            // ── Classify：分类优先级 ──────────────────────────────
            CheckClassify(false, false, false, PawnScopeKind.Colonist, "普通殖民者 → 殖民者", ref failures, ref total);
            CheckClassify(false, false, true, PawnScopeKind.Visitor, "访客 → 访问者", ref failures, ref total);
            CheckClassify(false, true, false, PawnScopeKind.Slave, "奴隶 → 奴隶", ref failures, ref total);
            CheckClassify(true, false, false, PawnScopeKind.Prisoner, "囚犯 → 囚犯", ref failures, ref total);
            CheckClassify(true, true, true, PawnScopeKind.Prisoner, "囚犯优先于奴隶/访客", ref failures, ref total);
            CheckClassify(false, true, true, PawnScopeKind.Slave, "奴隶优先于访客", ref failures, ref total);

            // ── IsIncluded：单组开关 ──────────────────────────────
            CheckIncluded(true, false, false, PawnScopeKind.Colonist, true, "仅殖民者开 → 殖民者包含", ref failures, ref total);
            CheckIncluded(true, false, false, PawnScopeKind.Visitor, false, "仅殖民者开 → 访问者排除", ref failures, ref total);
            CheckIncluded(false, true, false, PawnScopeKind.Visitor, true, "仅访问者开 → 访问者包含", ref failures, ref total);
            CheckIncluded(false, true, false, PawnScopeKind.Colonist, false, "仅访问者开 → 殖民者排除", ref failures, ref total);
            CheckIncluded(false, false, true, PawnScopeKind.Slave, true, "仅奴隶囚犯开 → 奴隶包含", ref failures, ref total);
            CheckIncluded(false, false, true, PawnScopeKind.Prisoner, true, "仅奴隶囚犯开 → 囚犯包含", ref failures, ref total);
            CheckIncluded(false, false, true, PawnScopeKind.Visitor, false, "仅奴隶囚犯开 → 访问者排除", ref failures, ref total);

            // ── IsIncluded：全开 / 全关 ──────────────────────────
            CheckIncluded(true, true, true, PawnScopeKind.Colonist, true, "全开 → 殖民者包含", ref failures, ref total);
            CheckIncluded(true, true, true, PawnScopeKind.Visitor, true, "全开 → 访问者包含", ref failures, ref total);
            CheckIncluded(true, true, true, PawnScopeKind.Slave, true, "全开 → 奴隶包含", ref failures, ref total);
            CheckIncluded(true, true, true, PawnScopeKind.Prisoner, true, "全开 → 囚犯包含", ref failures, ref total);
            CheckIncluded(false, false, false, PawnScopeKind.Colonist, false, "全关 → 殖民者排除", ref failures, ref total);
            CheckIncluded(false, false, false, PawnScopeKind.Visitor, false, "全关 → 访问者排除", ref failures, ref total);
            CheckIncluded(false, false, false, PawnScopeKind.Slave, false, "全关 → 奴隶排除", ref failures, ref total);
            CheckIncluded(false, false, false, PawnScopeKind.Prisoner, false, "全关 → 囚犯排除", ref failures, ref total);

            Console.WriteLine($"[TierScopeRules] {total - failures}/{total} passed");
            return failures;
        }

        private static void CheckClassify(bool isPrisoner, bool isSlave, bool isVisitor,
            PawnScopeKind expected, string label, ref int failures, ref int total)
        {
            total++;
            PawnScopeKind actual = TierScopeRules.Classify(isPrisoner, isSlave, isVisitor);
            if (actual != expected)
            {
                failures++;
                Console.WriteLine($"  FAIL: {label} 期望 {expected}，实际 {actual}");
            }
        }

        private static void CheckIncluded(bool colonists, bool visitors, bool slavesPrisoners,
            PawnScopeKind kind, bool expected, string label, ref int failures, ref int total)
        {
            total++;
            bool actual = TierScopeRules.IsIncluded(colonists, visitors, slavesPrisoners, kind);
            if (actual != expected)
            {
                failures++;
                Console.WriteLine($"  FAIL: {label} 期望 {expected}，实际 {actual}");
            }
        }
    }
}
