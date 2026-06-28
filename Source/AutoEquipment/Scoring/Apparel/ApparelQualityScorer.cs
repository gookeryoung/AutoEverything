using RimWorld;
using Verse;

namespace AutoEquipment.Scoring.Apparels
{
    /// <summary>
    /// 防具品质评分。
    /// </summary>
    public class ApparelQualityScorer : IScorer<Apparel>
    {
        public string Name => "品质";

        public void Score(Pawn pawn, Apparel gear, Role role, GearContext context,
                          GearWeights weights, ScoreBreakdown breakdown)
        {
            if (gear.TryGetQuality(out QualityCategory quality))
            {
                int qualityValue = (int)quality - 2; // Normal=0
                float score = qualityValue * 15f; // 防具品质权重固定 15
                breakdown.AddScore(Name, $"{quality}(等级{qualityValue})", score);
            }
        }
    }
}
