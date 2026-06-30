using RimWorld;
using Verse;
using AutoEverything.RoleEvaluation;
using AutoEverything.AutoEquipment;
using AutoEverything.Core;

namespace AutoEverything.AutoEquipment.Scoring.Apparels
{
    /// <summary>
    /// 禁止类防具硬约束：奴隶项圈（非奴隶）+ 死气背包。
    /// 与 ApparelShieldBeltScorer 并列于管线首位，Veto 后短路后续评分。
    /// 设计意图：评分阶段拒绝，已穿纠错由 CompGearManager.RemoveSlaveCollar 处理。
    /// </summary>
    public class ApparelForbiddenScorer : IScorer<Apparel>
    {
        public string Name => "禁止类防具约束";

        public void Score(Pawn pawn, Apparel gear, Role role, GearContext context,
                          GearWeights weights, ScoreBreakdown breakdown)
        {
            // 奴隶项圈：仅奴隶可穿，非奴隶穿着无意义
            if (GearDefClassifier.IsSlaveCollar(gear) && !DLCCompat.IsSlave(pawn))
            {
                breakdown.Veto(-9999f);
                breakdown.AddScore(Name, "非奴隶+奴隶项圈=拒绝", -9999f);
                return;
            }
            // 死气背包：释放毒云伤友军，禁止自动穿
            if (GearDefClassifier.IsDeadlifePack(gear))
            {
                breakdown.Veto(-9999f);
                breakdown.AddScore(Name, "死气背包=拒绝(毒伤友军)", -9999f);
            }
        }
    }
}
