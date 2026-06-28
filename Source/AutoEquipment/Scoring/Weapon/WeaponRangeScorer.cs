using RimWorld;
using Verse;

namespace AutoEquipment.Scoring.Weapon
{
    /// <summary>
    /// 远程武器射程评分：射程越长加分越高。
    /// 仅对远程武器生效，近战武器无射程概念。
    /// </summary>
    public class WeaponRangeScorer : IScorer<Thing>
    {
        public string Name => "射程";

        public void Score(Pawn pawn, Thing gear, Role role, GearContext context,
                          GearWeights weights, ScoreBreakdown breakdown)
        {
            if (!gear.def.IsRangedWeapon) return;

            float range = WeaponRangeHelper.GetRange(gear);
            if (range > 0f)
            {
                float score = range * weights.w_range;
                breakdown.AddScore(Name, $"{range:F0} × {weights.w_range:F1}", score);
            }
        }
    }
}
