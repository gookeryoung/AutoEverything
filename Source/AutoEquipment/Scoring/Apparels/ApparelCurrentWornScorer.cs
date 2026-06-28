using RimWorld;
using Verse;

namespace AutoEquipment.Scoring.Apparels
{
    /// <summary>
    /// 防具当前穿戴加成：小幅加成作为平局决胜，防止近乎相同评分的反复切换。
    /// </summary>
    public class ApparelCurrentWornScorer : IScorer<Apparel>
    {
        public string Name => "当前穿戴";

        public void Score(Pawn pawn, Apparel gear, Role role, GearContext context,
                          GearWeights weights, ScoreBreakdown breakdown)
        {
            if (pawn.apparel?.WornApparel?.Contains(gear) == true)
            {
                breakdown.AddScore(Name, "平局决胜", 5f);
            }
        }
    }
}
