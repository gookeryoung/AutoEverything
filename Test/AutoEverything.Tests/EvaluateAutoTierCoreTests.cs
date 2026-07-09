using System;
using AutoEverything.Core;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.Tests
{
    /// <summary>
    /// EvaluateAutoTierCore 评级纯逻辑核心的单元测试。
    /// 覆盖三大维度（乱开枪/坚韧格斗/工作狂神经质）、特殊天赋、沉鱼落雁、
    /// A/B 判定、负面特质降档等核心评级契约。
    /// </summary>
    public static class EvaluateAutoTierCoreTests
    {
        public static int RunAll()
        {
            int failures = 0;
            int total = 0;

            // ── 基础档：无特质无火 → C ──────────────────────────────
            Check(Empty(), CombatTier.C, "无特质无火 → C", ref failures, ref total);

            // ── 维度1：乱开枪系列 ──────────────────────────────────
            Check(Empty(triggerHappy: true), CombatTier.C, "乱开枪+无射击火 → C（不触发维度1）", ref failures, ref total);
            Check(Empty(triggerHappy: true, shootingMinor: true), CombatTier.S, "乱开枪+射击单火 → S", ref failures, ref total);
            Check(Empty(triggerHappy: true, shootingMajor: true), CombatTier.SS, "乱开枪+射击双火 → SS", ref failures, ref total);
            Check(Empty(triggerHappy: true, tough: true, shootingMajor: true), CombatTier.SSS, "乱开枪+坚韧+射击双火 → SSS", ref failures, ref total);

            // ── 维度2：坚韧格斗系列 ────────────────────────────────
            Check(Empty(tough: true), CombatTier.C, "坚韧+无格斗火 → C（不触发维度2）", ref failures, ref total);
            Check(Empty(tough: true, meleeMinor: true), CombatTier.S, "坚韧+格斗单火 → S", ref failures, ref total);
            Check(Empty(tough: true, meleeMajor: true), CombatTier.SS, "坚韧+格斗双火 → SS", ref failures, ref total);
            Check(Empty(tough: true, meleeMajor: true, nimble: true), CombatTier.SSS, "坚韧+格斗双火+敏捷 → SSS", ref failures, ref total);
            Check(Empty(tough: true, meleeMajor: true, brawler: true), CombatTier.SSS, "坚韧+格斗双火+格斗者 → SSS", ref failures, ref total);

            // ── 维度3：工作狂神经质系列 ────────────────────────────
            Check(Empty(industrious2: true), CombatTier.C, "工作狂+无神经质 → C（不触发维度3）", ref failures, ref total);
            Check(Empty(neurotic2: true), CombatTier.C, "无工作狂+神经质 → C（不触发维度3）", ref failures, ref total);
            Check(Empty(industrious2: true, neurotic2: true, workMajors: 0), CombatTier.C, "工作狂+神经质+0双火 → C（不触发）", ref failures, ref total);
            Check(Empty(industrious2: true, neurotic2: true, workMajors: 1), CombatTier.S, "工作狂+神经质+1双火 → S", ref failures, ref total);
            Check(Empty(industrious2: true, neurotic2: true, workMajors: 2), CombatTier.SS, "工作狂+神经质+2双火 → SS", ref failures, ref total);
            Check(Empty(industrious2: true, neurotic2: true, workMajors: 3), CombatTier.SSS, "工作狂+神经质+3双火 → SSS", ref failures, ref total);

            // ── 特殊天赋 → S ───────────────────────────────────────
            Check(Empty(hasSpecialTalent: true), CombatTier.S, "特殊天赋 → S", ref failures, ref total);

            // ── 沉鱼落雁 + 社交双火 → S ────────────────────────────
            Check(Empty(beauty2: true), CombatTier.C, "沉鱼落雁+无社交火 → C（不触发）", ref failures, ref total);
            Check(Empty(socialMajor: true), CombatTier.C, "非沉鱼落雁+社交双火 → C（不触发）", ref failures, ref total);
            Check(Empty(beauty2: true, socialMajor: true), CombatTier.S, "沉鱼落雁+社交双火 → S", ref failures, ref total);

            // ── A/B 判定（tier==C 时）──────────────────────────────
            Check(Empty(majorCount: 0, minorCount: 3), CombatTier.C, "0双火+3单火 → C（无双火不满足A/B）", ref failures, ref total);
            Check(Empty(majorCount: 1, minorCount: 1), CombatTier.C, "1双火+1单火 → C（合计<3）", ref failures, ref total);
            Check(Empty(majorCount: 1, minorCount: 2), CombatTier.B, "1双火+2单火 → B", ref failures, ref total);
            Check(Empty(majorCount: 2, minorCount: 1), CombatTier.A, "2双火+1单火 → A", ref failures, ref total);
            Check(Empty(majorCount: 3, minorCount: 0), CombatTier.A, "3双火+0单火 → A（合计≥3）", ref failures, ref total);

            // ── 降档：有负面特质且 tier > D ────────────────────────
            Check(Empty(negativeTrait: true), CombatTier.D, "负面特质+基础C → D（C>D降档）", ref failures, ref total);
            var sInput = Empty(hasSpecialTalent: true);
            sInput.HasNegativeTrait = true;
            Check(sInput, CombatTier.A, "特殊天赋S+负面特质 → A（降档）", ref failures, ref total);
            var ssInput = Empty(tough: true, meleeMajor: true);
            ssInput.HasNegativeTrait = true;
            Check(ssInput, CombatTier.S, "坚韧格斗双火SS+负面特质 → S（降档）", ref failures, ref total);

            // ── 维度组合（MaxTier 取最高）──────────────────────────
            // 乱开枪+坚韧+射击双火 → SSS（维度1 SSS 条件，非 SS）
            var comboInput = Empty(triggerHappy: true, shootingMajor: true, tough: true, meleeMajor: true);
            Check(comboInput, CombatTier.SSS, "乱开枪+坚韧+射击双火 → SSS（维度1 SSS）", ref failures, ref total);

            // 乱开枪+射击双火(SS) + 特殊天赋(S) → SS（取最高）
            var combo2 = Empty(triggerHappy: true, shootingMajor: true, hasSpecialTalent: true);
            Check(combo2, CombatTier.SS, "乱开枪SS + 特殊天赋S → SS（取最高）", ref failures, ref total);

            // 工作狂神经质+2双火(SS) + 特殊天赋(S) → SS（取最高）
            var combo3 = Empty(industrious2: true, neurotic2: true, workMajors: 2, hasSpecialTalent: true);
            Check(combo3, CombatTier.SS, "工作狂SS + 特殊天赋S → SS（取最高）", ref failures, ref total);

            // 特殊天赋(S) + A档条件 → S（特殊天赋优先，不走A/B判定）
            var talentWithA = Empty(hasSpecialTalent: true, majorCount: 2, minorCount: 1);
            Check(talentWithA, CombatTier.S, "特殊天赋S + A档条件 → S（不走A/B）", ref failures, ref total);

            Console.WriteLine($"[EvaluateAutoTierCoreTests] {total - failures}/{total} passed");
            return failures;
        }

        /// <summary>
        /// 构造默认输入（全 false/0），按参数覆盖指定字段。
        /// </summary>
        private static CombatEvaluator.TierEvaluationInput Empty(
            bool triggerHappy = false, bool tough = false, bool nimble = false, bool brawler = false,
            bool industrious2 = false, bool neurotic2 = false, bool beauty2 = false,
            bool hasSpecialTalent = false, bool negativeTrait = false,
            bool shootingMajor = false, bool shootingMinor = false,
            bool meleeMajor = false, bool meleeMinor = false, bool socialMajor = false,
            int majorCount = 0, int minorCount = 0, int workMajors = 0)
        {
            return new CombatEvaluator.TierEvaluationInput
            {
                IsTriggerHappy = triggerHappy,
                IsTough = tough,
                IsNimble = nimble,
                IsBrawler = brawler,
                Industrious2 = industrious2,
                Neurotic2 = neurotic2,
                Beauty2 = beauty2,
                HasSpecialTalent = hasSpecialTalent,
                HasNegativeTrait = negativeTrait,
                ShootingMajor = shootingMajor,
                ShootingMinor = shootingMinor,
                MeleeMajor = meleeMajor,
                MeleeMinor = meleeMinor,
                SocialMajor = socialMajor,
                MajorCount = majorCount,
                MinorCount = minorCount,
                WorkMajors = workMajors
            };
        }

        private static void Check(CombatEvaluator.TierEvaluationInput input, CombatTier expected, string label,
            ref int failures, ref int total)
        {
            total++;
            CombatTier actual = CombatEvaluator.EvaluateAutoTierCore(input);
            if (actual != expected)
            {
                Console.WriteLine($"  FAIL: {label}: expected {expected}, got {actual}");
                failures++;
            }
        }
    }
}
