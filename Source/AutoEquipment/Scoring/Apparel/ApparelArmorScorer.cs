using RimWorld;
using Verse;

namespace AutoEquipment.Scoring.Apparels
{
    /// <summary>
    /// 防具护甲评分：基础防护值。
    /// 锐器护甲 + 钝器护甲 × 0.5，使用 GetStatValueAbstract 保证评分一致。
    /// </summary>
    public class ApparelArmorScorer : IScorer<Apparel>
    {
        public string Name => "护甲";

        public void Score(Pawn pawn, Apparel gear, Role role, GearContext context,
                          GearWeights weights, ScoreBreakdown breakdown)
        {
            ThingDef stuff = gear.Stuff;
            float sharp = gear.def.GetStatValueAbstract(StatDefOf.ArmorRating_Sharp, stuff);
            float blunt = gear.def.GetStatValueAbstract(StatDefOf.ArmorRating_Blunt, stuff);
            float armor = sharp + blunt * 0.5f;
            float score = armor * weights.w_armor;
            breakdown.AddScore(Name, $"锐{sharp:F1}+钝{blunt:F1}×0.5 × {weights.w_armor:F0}", score);
        }
    }
}
