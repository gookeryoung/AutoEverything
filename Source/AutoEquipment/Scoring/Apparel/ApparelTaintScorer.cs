using RimWorld;
using Verse;

namespace AutoEquipment.Scoring.Apparel
{
    /// <summary>
    /// 防具沾染评分：沾染尸体的防具大幅扣分。
    /// </summary>
    public class ApparelTaintScorer : IScorer<Apparel>
    {
        public string Name => "沾染";

        public void Score(Pawn pawn, global::Verse.Apparel gear, Role role, GearContext context,
                          GearWeights weights, ScoreBreakdown breakdown)
        {
            if (gear.WornByCorpse)
            {
                breakdown.AddScore(Name, "沾染尸体", -100f);
            }
        }
    }
}
