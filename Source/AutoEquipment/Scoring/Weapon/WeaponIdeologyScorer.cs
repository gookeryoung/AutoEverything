using RimWorld;
using Verse;

namespace AutoEquipment.Scoring.Weapon
{
    /// <summary>
    /// 武器意识形态评分：检查意识形态戒律对武器类型的偏好。
    /// </summary>
    public class WeaponIdeologyScorer : IScorer<Thing>
    {
        public string Name => "意识形态";

        public void Score(Pawn pawn, Thing gear, Role role, GearContext context,
                          GearWeights weights, ScoreBreakdown breakdown)
        {
            if (pawn.Ideo == null) return;

            bool isMelee = gear.def.IsMeleeWeapon;
            bool isRanged = gear.def.IsRangedWeapon;

            // 遍历意识形态戒律，查找武器偏好
            // 注意：戒律列表可能为空，使用 PreceptsListForReading 避免分配
            var precepts = pawn.Ideo.PreceptsListForReading;
            for (int i = 0; i < precepts.Count; i++)
            {
                var precept = precepts[i];
                string preceptName = precept.def.defName;

                // 仅处理武器相关戒律
                if (!preceptName.Contains("Weapon") &&
                    !preceptName.Contains("Melee") &&
                    !preceptName.Contains("Ranged")) continue;

                // 判断是偏好还是厌恶
                bool disapproved = preceptName.Contains("Disapproved")
                    || preceptName.Contains("Despised")
                    || preceptName.Contains("Horrible");

                float preceptScore = disapproved ? -30f : 30f;

                if (isMelee && preceptName.Contains("Melee"))
                {
                    breakdown.AddScore(Name, $"{precept.def.label}+近战", preceptScore);
                }
                else if (isRanged && preceptName.Contains("Ranged"))
                {
                    breakdown.AddScore(Name, $"{precept.def.label}+远程", preceptScore);
                }
            }
        }
    }
}
