using RimWorld;
using Verse;
using AutoEverything.RoleEvaluation;
using AutoEverything.AutoEquipment;

namespace AutoEverything.AutoEquipment.Scoring.Weapon
{
    /// <summary>
    /// 禁止类武器硬约束：手榴弹（含燃烧瓶/毒气手雷）+ 火箭发射器（末日/三连）。
    /// 这些武器是单次消耗品，作为主武器会在使用后消失，不适合持续装备。
    /// 与 WeaponBiocodedScorer 并列于管线前部，Veto 后短路后续评分。
    /// 注：EMP 手雷作为库存携带特例由 SidearmAllocator 处理，不经过武器评分管线。
    /// </summary>
    public class WeaponForbiddenScorer : IScorer<Thing>
    {
        public string Name => "禁止类武器约束";

        public void Score(Pawn pawn, Thing gear, Role role, GearContext context,
                          GearWeights weights, ScoreBreakdown breakdown)
        {
            if (GearDefClassifier.IsDangerousWeapon(gear))
            {
                breakdown.Veto(-9000f);
                breakdown.AddScore(Name, "手榴弹/火箭发射器=拒绝(单次消耗品)", -9000f);
            }
        }
    }
}
