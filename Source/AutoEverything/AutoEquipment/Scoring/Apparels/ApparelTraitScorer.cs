using RimWorld;
using Verse;
using AutoEverything.Core;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.AutoEquipment.Scoring.Apparels
{
    /// <summary>
    /// 防具特质评分：勤奋/懒惰/神经质/美感偏好等特质对防具的影响。
    /// </summary>
    public class ApparelTraitScorer : IScorer<Apparel>
    {
        public string Name => "特质";

        // TraitDef 查询统一由 TraitDefCache 提供（集中管理，避免与 CombatEvaluator 重复定义）

        public void Score(Pawn pawn, Apparel gear, Role role, GearContext context,
                          GearWeights weights, ScoreBreakdown breakdown)
        {
            if (pawn.story?.traits == null) return;

            // Industriousness 特质 degree：
            //   2 = industrious（勤奋）、1 = hard worker（努力工作）
            //   -1 = lazy（懒惰）、-2 = slothful（极其懒惰）
            if (TraitDefCache.Industriousness != null && context == GearContext.Work)
            {
                int degree = pawn.story.traits.DegreeOfTrait(TraitDefCache.Industriousness);
                if (degree >= 1)
                    breakdown.AddScore(Name, degree >= 2 ? "勤奋+工作情境" : "努力工作+工作情境", 20f);
                else if (degree < 0)
                    breakdown.AddScore(Name, "懒惰+工作情境", -20f);
            }

            // 神经质：医疗/工作中略微偏好高属性防具
            // Neurotic degree：1 = 神经质、2 = 严重神经质
            if (TraitDefCache.Neurotic != null && (context == GearContext.Work || role == Role.Doctor))
            {
                int degree = pawn.story.traits.DegreeOfTrait(TraitDefCache.Neurotic);
                if (degree >= 1)
                    breakdown.AddScore(Name, "神经质+医疗/工作", 10f);
            }

            // 美感偏好（Beauty 特质 degree=2 美丽）
            if (TraitDefCache.Beauty != null && pawn.story.traits.HasTrait(TraitDefCache.Beauty, 2))
            {
                float beautyValue = gear.GetStatValue(StatDefOf.Beauty);
                if (beautyValue > 0f)
                {
                    float score = beautyValue * 5f;
                    breakdown.AddScore(Name, breakdown.CollectItems ? $"美感偏好+{beautyValue:F1}" : null, score);
                }
            }
        }
    }
}