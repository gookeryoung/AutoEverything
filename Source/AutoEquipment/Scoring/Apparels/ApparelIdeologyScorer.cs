using System;
using RimWorld;
using Verse;

namespace AutoEquipment.Scoring.Apparels
{
    /// <summary>
    /// 防具意识形态评分：角色配套防具与风格匹配。
    /// </summary>
    public class ApparelIdeologyScorer : IScorer<Apparel>
    {
        public string Name => "意识形态";

        public void Score(Pawn pawn, Apparel gear, Role role, GearContext context,
                          GearWeights weights, ScoreBreakdown breakdown)
        {
            if (pawn.Ideo == null) return;

            try
            {
                // 拥有意识形态角色的 Pawn 穿戴配套防具有加成
                var ideoRole = pawn.Ideo.GetRole(pawn);
                if (ideoRole != null)
                {
                    breakdown.AddScore(Name, "意识形态角色配套", 20f);
                }

                // 风格匹配
                if (gear.StyleDef != null && pawn.Ideo.style != null)
                {
                    breakdown.AddScore(Name, "风格匹配", 10f);
                }
            }
            catch (Exception ex)
            {
                // 评分失败不应中断流程，记录后继续
                Log.WarningOnce($"[AutoEquipment] 意识形态防具评分失败: {ex.Message}",
                    pawn.thingIDNumber ^ 0x4C7A);
            }
        }
    }
}
