using RimWorld;
using Verse;

namespace AutoEquipment.Scoring.Apparels
{
    /// <summary>
    /// 防具保温评分：冷保温 + 热保温。
    /// </summary>
    public class ApparelInsulationScorer : IScorer<Apparel>
    {
        public string Name => "保温";

        public void Score(Pawn pawn, Apparel gear, Role role, GearContext context,
                          GearWeights weights, ScoreBreakdown breakdown)
        {
            ThingDef stuff = gear.Stuff;
            float cold = gear.def.GetStatValueAbstract(StatDefOf.Insulation_Cold, stuff);
            float heat = gear.def.GetStatValueAbstract(StatDefOf.Insulation_Heat, stuff);
            float insulation = cold + heat;
            float score = insulation * weights.w_insulation;
            breakdown.AddScore(Name, $"冷{cold:F1}+热{heat:F1} × {weights.w_insulation:F0}", score);
        }
    }
}
