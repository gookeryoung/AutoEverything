using RimWorld;
using Verse;

namespace AutoEquipment.Scoring.Weapon
{
    /// <summary>
    /// 武器情境评分：根据当前情境调整武器偏好。
    /// 狩猎情境强烈偏好远程武器，战斗情境放大 DPS 权重。
    /// </summary>
    public class WeaponContextScorer : IScorer<Thing>
    {
        public string Name => "情境";

        public void Score(Pawn pawn, Thing gear, Role role, GearContext context,
                          GearWeights weights, ScoreBreakdown breakdown)
        {
            bool isRanged = gear.def.IsRangedWeapon;
            bool isMelee = gear.def.IsMeleeWeapon;

            switch (context)
            {
                case GearContext.Hunting:
                    if (isRanged)
                    {
                        breakdown.AddScore(Name, "狩猎+远程", 80f);
                        float range = WeaponRangeHelper.GetRange(gear);
                        if (range > 0f)
                        {
                            float rangeScore = range * 1.5f;
                            breakdown.AddScore(Name, $"狩猎+射程{range:F0}×1.5", rangeScore);
                        }
                    }
                    else if (isMelee)
                    {
                        breakdown.AddScore(Name, "狩猎+近战(惩罚)", -100f);
                    }
                    break;

                case GearContext.Combat:
                    // 战斗情境：DPS 权重放大由 DpsScorer 内部处理
                    // 此处仅记录情境标记
                    breakdown.AddScore(Name, "战斗情境", 0f);
                    break;

                default:
                    break;
            }
        }
    }
}
