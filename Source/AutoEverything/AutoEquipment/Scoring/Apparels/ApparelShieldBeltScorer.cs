using RimWorld;
using Verse;
using AutoEverything.RoleEvaluation;
using AutoEverything.AutoEquipment;

namespace AutoEverything.AutoEquipment.Scoring.Apparels
{
    /// <summary>
    /// 护盾腰带硬约束：护盾腰带仅属于重甲前排（Brawler）。
    /// 护盾腰带会阻挡所有远程射击，远程角色（Shooter/Hunter/Leader/Worker/Doctor 等）一律拒绝。
    /// 设计意图：与 BeltAllocator 的 Heavy gate 互为双保险——
    ///   BeltAllocator 在分配阶段 gate，本 Scorer 在评分阶段 Veto，防止任何路径漏网。
    /// </summary>
    public class ApparelShieldBeltScorer : IScorer<Apparel>
    {
        public string Name => "护盾腰带约束";

        public void Score(Pawn pawn, Apparel gear, Role role, GearContext context,
                          GearWeights weights, ScoreBreakdown breakdown)
        {
            if (role == Role.Brawler) return;
            if (GearDefClassifier.IsShieldBelt(gear))
            {
                breakdown.Veto(-9999f);
                breakdown.AddScore(Name, "非格斗者+护盾腰带=拒绝", -9999f);
            }
        }
    }
}
