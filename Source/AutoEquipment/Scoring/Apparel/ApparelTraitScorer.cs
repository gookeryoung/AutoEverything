using RimWorld;
using Verse;

namespace AutoEquipment.Scoring.Apparels
{
    /// <summary>
    /// 防具特质评分：勤奋/懒惰/神经质/美感偏好等特质对防具的影响。
    /// </summary>
    public class ApparelTraitScorer : IScorer<Apparel>
    {
        public string Name => "特质";

        public void Score(Pawn pawn, Apparel gear, Role role, GearContext context,
                          GearWeights weights, ScoreBreakdown breakdown)
        {
            if (pawn.story?.traits == null) return;

            // 勤奋：工作中偏好工作加成防具
            var industrious = TraitDef.Named("Industriousness");
            if (industrious != null && context == GearContext.Work)
            {
                if (pawn.story.traits.HasTrait(industrious, 1))
                    breakdown.AddScore(Name, "勤奋+工作情境", 20f);
                else if (pawn.story.traits.HasTrait(industrious, -1))
                    breakdown.AddScore(Name, "懒惰+工作情境", -20f);
            }

            // 神经质：医疗/工作中略微偏好高属性防具
            var neurotic = TraitDef.Named("Neurotic");
            if (neurotic != null && (context == GearContext.Work || role == Role.Doctor))
            {
                if (pawn.story.traits.HasTrait(neurotic, 1))
                    breakdown.AddScore(Name, "神经质+医疗/工作", 10f);
            }

            // 美感偏好（Beauty 特质 degree=2）：高美感防具加成
            var beauty = TraitDef.Named("Beauty");
            if (beauty != null && pawn.story.traits.HasTrait(beauty, 2))
            {
                float beautyValue = gear.GetStatValue(StatDefOf.Beauty);
                if (beautyValue > 0f)
                {
                    float score = beautyValue * 5f;
                    breakdown.AddScore(Name, $"美感偏好+{beautyValue:F1}", score);
                }
            }
        }
    }
}
