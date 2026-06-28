using RimWorld;
using Verse;

namespace AutoEquipment.Scoring.Apparels
{
    /// <summary>
    /// 防具移速评分：移动速度偏移（重甲通常为负）。
    /// 用 for 循环替代 LINQ Where + FirstOrDefault。
    /// </summary>
    public class ApparelMoveSpeedScorer : IScorer<Apparel>
    {
        public string Name => "移速";

        public void Score(Pawn pawn, Apparel gear, Role role, GearContext context,
                          GearWeights weights, ScoreBreakdown breakdown)
        {
            float offset = GetMoveSpeedOffset(gear);
            if (offset != 0f)
            {
                float score = offset * weights.w_movespeed;
                breakdown.AddScore(Name, $"偏移{offset:F2} × {weights.w_movespeed:F0}", score);
            }
        }

        /// <summary>
        /// 获取防具的移动速度偏移。
        /// </summary>
        private static float GetMoveSpeedOffset(Apparel apparel)
        {
            if (apparel.def.equippedStatOffsets == null) return 0f;

            for (int i = 0; i < apparel.def.equippedStatOffsets.Count; i++)
            {
                var mod = apparel.def.equippedStatOffsets[i];
                if (mod.stat == StatDefOf.MoveSpeed) return mod.value;
            }
            return 0f;
        }
    }
}
