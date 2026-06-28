using RimWorld;
using Verse;

namespace AutoEquipment.Scoring.Apparel
{
    /// <summary>
    /// 防具皇家头衔评分：满足头衔要求的防具加分。
    /// </summary>
    public class ApparelRoyaltyScorer : IScorer<Apparel>
    {
        public string Name => "皇家";

        public void Score(Pawn pawn, global::Verse.Apparel gear, Role role, GearContext context,
                          GearWeights weights, ScoreBreakdown breakdown)
        {
            if (pawn.royalty == null) return;

            // 遍历所有头衔，检查防具是否满足要求
            var titles = pawn.royalty.AllTitlesForReading;
            for (int i = 0; i < titles.Count; i++)
            {
                var title = titles[i];
                if (title.def.requiredApparel == null) continue;

                for (int j = 0; j < title.def.requiredApparel.Count; j++)
                {
                    var req = title.def.requiredApparel[j];
                    if (req.ApparelMeetsRequirement(gear.def, false))
                    {
                        breakdown.AddScore(Name, $"{title.def.label}要求", 40f);
                        return; // 一个头衔满足即可，避免重复加分
                    }
                }
            }
        }
    }
}
