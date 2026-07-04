using RimWorld;
using Verse;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.AutoEquipment.Scoring.Apparels
{
    /// <summary>
    /// 防具护甲评分：基础防护值。
    /// 锐器护甲 + 钝器护甲 × 0.5，使用实例 API 含 stuff/品质/HP 修正。
    /// </summary>
    public class ApparelArmorScorer : IScorer<Apparel>
    {
        public string Name => "护甲";

        public void Score(Pawn pawn, Apparel gear, Role role, GearContext context,
                          GearWeights weights, ScoreBreakdown breakdown)
        {
            float sharp = gear.GetStatValue(StatDefOf.ArmorRating_Sharp);
            float blunt = gear.GetStatValue(StatDefOf.ArmorRating_Blunt);
            float armor = sharp + blunt * 0.5f;
            float score = armor * weights.w_armor;
            breakdown.AddScore(Name, breakdown.CollectItems ? $"锐{sharp:F1}+钝{blunt:F1}×0.5 × {weights.w_armor:F0}" : null, score);
        }
    }
}