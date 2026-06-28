using RimWorld;
using Verse;

namespace AutoEquipment.Scoring.Apparels
{
    /// <summary>
    /// 防具耐久修正：损坏的防具按 HP 比例扣分。
    /// </summary>
    public class ApparelDurabilityScorer : IScorer<Apparel>
    {
        public string Name => "耐久";

        public void Score(Pawn pawn, Apparel gear, Role role, GearContext context,
                          GearWeights weights, ScoreBreakdown breakdown)
        {
            if (gear.HitPoints < gear.MaxHitPoints)
            {
                float hpPct = (float)gear.HitPoints / gear.MaxHitPoints;
                breakdown.ApplyMultiplier(Name, $"HP {gear.HitPoints}/{gear.MaxHitPoints}", hpPct);
            }
        }
    }
}
