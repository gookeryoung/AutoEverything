using RimWorld;
using Verse;
using AutoEverything.Core;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.AutoEquipment.Scoring.Weapon
{
    /// <summary>
    /// 武器意识形态评分：检查文化戒律对武器的偏好（Noble）或鄙夷（Despised）。
    /// Noble 加 w_ideology_noble 分；Despised 减 w_ideology_despised 分（大额负分，不用 Veto）。
    /// 需 Ideology DLC；未加载或 Pawn 无 Ideo 时跳过。
    /// </summary>
    public class WeaponIdeologyScorer : IScorer<Thing>
    {
        public string Name => "意识形态";

        public void Score(Pawn pawn, Thing gear, Role role, GearContext context,
                          GearWeights weights, ScoreBreakdown breakdown)
        {
            // DLCCompat 内部已守卫 ModsConfig.IdeologyActive 与 pawn.Ideo==null
            int disposition = DLCCompat.GetWeaponDisposition(pawn, gear.def);
            if (disposition == 0) return;  // None：无态度，跳过

            if (disposition == 1)  // Noble：尊崇
            {
                breakdown.AddScore(Name,
                    breakdown.CollectItems ? "文化尊崇" : null,
                    weights.w_ideology_noble);
            }
            else  // disposition == 2, Despised：鄙夷
            {
                breakdown.AddScore(Name,
                    breakdown.CollectItems ? "文化鄙夷" : null,
                    -weights.w_ideology_despised);
            }
        }
    }
}
