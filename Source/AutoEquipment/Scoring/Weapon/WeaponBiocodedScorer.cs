using RimWorld;
using Verse;

namespace AutoEquipment.Scoring.Weapon
{
    /// <summary>
    /// 生物编码/人格绑定武器评分：硬性约束。
    /// CompBladelinkWeapon 继承自 CompBiocodable，一次检查覆盖两类。
    /// 归属者得 +9000，绑定他人直接否决（-9000）。
    /// 此 Scorer 必须在管线最前面执行，以短路后续评分。
    /// </summary>
    public class WeaponBiocodedScorer : IScorer<Thing>
    {
        public string Name => "生物编码";

        public void Score(Pawn pawn, Thing gear, Role role, GearContext context,
                          GearWeights weights, ScoreBreakdown breakdown)
        {
            var biocomp = gear.TryGetComp<CompBiocodable>();
            if (biocomp == null || !biocomp.Biocoded) return;

            if (biocomp.CodedPawn == pawn)
            {
                // 归属者：永远保留，加分极高确保不被替换
                breakdown.AddScore(Name, "归属者", 9000f);
            }
            else
            {
                // 已绑定他人：否决，绝不装备
                breakdown.Veto(-9000f);
                breakdown.AddScore(Name, "已绑定他人", -9000f);
            }
        }
    }
}
