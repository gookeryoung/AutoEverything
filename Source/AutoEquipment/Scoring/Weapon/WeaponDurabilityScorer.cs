using RimWorld;
using Verse;

namespace AutoEquipment.Scoring.Weapon
{
    /// <summary>
    /// 武器耐久修正：损坏的武器按 HP 比例扣分。
    /// 此 Scorer 应在管线最后执行，作为乘法修正。
    /// </summary>
    public class WeaponDurabilityScorer : IScorer<Thing>
    {
        public string Name => "耐久";

        public void Score(Pawn pawn, Thing gear, Role role, GearContext context,
                          GearWeights weights, ScoreBreakdown breakdown)
        {
            if (gear.HitPoints < gear.MaxHitPoints)
            {
                float hpPct = (float)gear.HitPoints / gear.MaxHitPoints;
                breakdown.ApplyMultiplier(Name, $"HP {gear.HitPoints}/{gear.MaxHitPoints} (×{hpPct:F2})", hpPct);
            }
        }
    }
}
