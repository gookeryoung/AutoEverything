using RimWorld;
using Verse;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.AutoEquipment.Scoring.Apparels
{
    /// <summary>
    /// 防具情境评分：根据温度情境调整保温/护甲权重。
    /// </summary>
    public class ApparelContextScorer : IScorer<Apparel>
    {
        public string Name => "情境";

        public void Score(Pawn pawn, Apparel gear, Role role, GearContext context,
                          GearWeights weights, ScoreBreakdown breakdown)
        {
            switch (context)
            {
                case GearContext.Cold:
                    {
                        float cold = gear.GetStatValue(StatDefOf.Insulation_Cold);
                        if (cold > 0f)
                        {
                            float score = cold * 50f;
                            breakdown.AddScore(Name, breakdown.CollectItems ? $"寒冷+冷保温{cold:F1} × 50" : null, score);
                        }
                        float armor = gear.GetStatValue(StatDefOf.ArmorRating_Sharp);
                        if (armor > 0f)
                        {
                            float score = armor * 20f;
                            breakdown.AddScore(Name, breakdown.CollectItems ? $"寒冷+护甲{armor:F1} × 20" : null, score);
                        }
                        break;
                    }

                case GearContext.Hot:
                    {
                        float heat = gear.GetStatValue(StatDefOf.Insulation_Heat);
                        if (heat > 0f)
                        {
                            float score = heat * 50f;
                            breakdown.AddScore(Name, breakdown.CollectItems ? $"炎热+热保温{heat:F1} × 50" : null, score);
                        }
                        // 炎热情境惩罚重甲
                        float armor = gear.GetStatValue(StatDefOf.ArmorRating_Sharp);
                        if (armor > 0f)
                        {
                            float score = -armor * 30f;
                            breakdown.AddScore(Name, breakdown.CollectItems ? $"炎热+重甲惩罚 -{armor:F1} × 30" : null, score);
                        }
                        break;
                    }

                case GearContext.Combat:
                    // 战斗情境：护甲权重放大由 ApparelArmorScorer 的基础值 + 情境加成体现
                    // 此处仅记录情境标记
                    breakdown.AddScore(Name, "战斗情境", 0f);
                    break;
            }
        }
    }
}