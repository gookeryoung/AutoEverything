using RimWorld;
using Verse;

namespace AutoEquipment.Scoring.Apparel
{
    /// <summary>
    /// 防具耐久修正：损坏的防具按 HP 比例扣分。
    /// </summary>
    public class ApparelDurabilityScorer : IScorer<Apparel>
    {
        public string Name => "耐久";
    }
}
