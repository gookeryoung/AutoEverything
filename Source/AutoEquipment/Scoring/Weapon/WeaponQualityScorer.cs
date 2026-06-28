using RimWorld;
using Verse;

namespace AutoEquipment.Scoring.Weapon
{
    /// <summary>
    /// 武器品质评分：品质越高加成越大。
    /// Awful=-2, Poor=-1, Normal=0, Good=+1, Excellent=+2, Masterwork=+3, Legendary=+4
    /// </summary>
    public class WeaponQualityScorer : IScorer<Thing>
    {
        public string Name => "品质";

        public void Score(Pawn pawn, Thing gear, Role role, GearContext context,
                          GearWeights weights, ScoreBreakdown breakdown)
        {
            if (gear.TryGetQuality(out QualityCategory quality))
            {
                int qualityValue = (int)quality - 2; // Normal=0
                float score = qualityValue * weights.w_quality;
                breakdown.AddScore(Name, $"{quality}(等级{qualityValue})", score);
            }
        }
    }
}
