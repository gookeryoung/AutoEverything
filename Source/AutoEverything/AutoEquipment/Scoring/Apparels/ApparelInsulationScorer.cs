using RimWorld;
using Verse;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.AutoEquipment.Scoring.Apparels
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
            float cold = gear.GetStatValue(StatDefOf.Insulation_Cold);
            float heat = gear.GetStatValue(StatDefOf.Insulation_Heat);
            float insulation = cold + heat;
            float score = insulation * weights.w_insulation;
            breakdown.AddScore(Name, breakdown.CollectItems ? $"冷{cold:F1}+热{heat:F1} × {weights.w_insulation:F0}" : null, score);
        }
    }
}