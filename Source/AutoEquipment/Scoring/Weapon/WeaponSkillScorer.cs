using RimWorld;
using Verse;

namespace AutoEquipment.Scoring.Weapon
{
    /// <summary>
    /// 武器技能评分：基于技能等级与兴趣度（火焰）。
    /// 兴趣度加权：无火 ×1.0，单火 ×1.5，双火 ×2.0。
    /// 关键设计：兴趣度反映 Pawn 的天赋倾向，长期看更值得培养。
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
                    string desc = $"射击{shooting.Level} × {weights.w_skill:F1} × {passionMult:F1}({GetPassionName(shooting.passion)})";
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
                    string desc = $"近战{melee.Level} × {weights.w_skill:F1} × {passionMult:F1}({GetPassionName(melee.passion)})";
                    breakdown.AddScore(Name, desc, score);
                }
            }
        }

        /// <summary>
        /// 获取兴趣度乘数。
        /// </summary>
        private static float GetPassionMultiplier(SkillRecord skill, GearWeights weights)
        {
            switch (skill.passion)
            {
                case Passion.Minor: return weights.w_passionMinor;
                case Passion.Major: return weights.w_passionMajor;
                default: return 1.0f;
            }
        }

        /// <summary>
        /// 获取兴趣度的中文名称（用于明细输出）。
        /// </summary>
        private static string GetPassionName(Passion passion)
        {
            switch (passion)
            {
                case Passion.Minor: return "单火";
                case Passion.Major: return "双火";
                default: return "无火";
            }
        }
    }
}
