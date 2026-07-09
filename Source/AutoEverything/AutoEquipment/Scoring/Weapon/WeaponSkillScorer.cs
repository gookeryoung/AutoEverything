using RimWorld;
using Verse;
using AutoEverything.Core;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.AutoEquipment.Scoring.Weapon
{
    /// <summary>
    /// 武器技能评分：基于技能等级与兴趣度（火焰）。
    /// 兴趣度加权：无火 ×1.0，单火 ×1.5，双火 ×2.0。
    /// 关键设计：兴趣度反映 Pawn 的天赋倾向，长期看更值得培养。
    /// VSE 兼容：通过 PassionHelper 统一处理 6 种 passion 类型。
    /// </summary>
    public class WeaponSkillScorer : IScorer<Thing>
    {
        public string Name => "技能";

        public void Score(Pawn pawn, Thing gear, Role role, GearContext context,
                          GearWeights weights, ScoreBreakdown breakdown)
        {
            if (pawn.skills == null) return;

            bool isRanged = gear.def.IsRangedWeapon;
            bool isMelee = gear.def.IsMeleeWeapon;

            if (isRanged)
            {
                SkillRecord shooting = pawn.skills.GetSkill(SkillDefOf.Shooting);
                if (shooting != null)
                {
                    float passionMult = GetPassionMultiplier(shooting, weights);
                    float score = shooting.Level * weights.w_skill * passionMult;
                    string desc = breakdown.CollectItems ? $"射击{shooting.Level} × {weights.w_skill:F1} × {passionMult:F1}({PassionHelper.GetPassionName(shooting.passion)})" : null;
                    breakdown.AddScore(Name, desc, score);
                }
            }

            if (isMelee)
            {
                SkillRecord melee = pawn.skills.GetSkill(SkillDefOf.Melee);
                if (melee != null)
                {
                    float passionMult = GetPassionMultiplier(melee, weights);
                    float score = melee.Level * weights.w_skill * passionMult;
                    string desc = breakdown.CollectItems ? $"近战{melee.Level} × {weights.w_skill:F1} × {passionMult:F1}({PassionHelper.GetPassionName(melee.passion)})" : null;
                    breakdown.AddScore(Name, desc, score);
                }
            }
        }

        /// <summary>
        /// 获取兴趣度乘数。
        /// VSE 兼容：
        ///   Apathy   → 1 / w_passionMinor（比无火更低）
        ///   None     → 1.0
        ///   Minor    → w_passionMinor
        ///   Major    → w_passionMajor
        ///   Natural  → w_passionMajor（等同双火）
        ///   Critical → w_passionMajor × 1.5（最高）
        /// </summary>
        private static float GetPassionMultiplier(SkillRecord skill, GearWeights weights)
        {
            var tier = PassionHelper.GetPassionTier(skill.passion);
            switch (tier)
            {
                case PassionHelper.PassionTier.Apathy:
                    return weights.w_passionMinor > 0f ? 1f / weights.w_passionMinor : 0.5f;
                case PassionHelper.PassionTier.Minor:
                    return weights.w_passionMinor;
                case PassionHelper.PassionTier.Major:
                    return weights.w_passionMajor;
                case PassionHelper.PassionTier.Critical:
                    return weights.w_passionMajor * 1.5f;
                default: // None
                    return 1f;
            }
        }
    }
}