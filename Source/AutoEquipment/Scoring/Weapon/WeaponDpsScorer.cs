using RimWorld;
using Verse;

namespace AutoEquipment.Scoring.Weapon
{
    /// <summary>
    /// 武器伤害输出评分：近战看 DPS，远程看伤害倍率与冷却。
    /// 战斗情境下权重放大 1.5 倍。
    /// </summary>
    public class WeaponDpsScorer : IScorer<Thing>
    {
        public string Name => "伤害";

        public void Score(Pawn pawn, Thing gear, Role role, GearContext context,
                          GearWeights weights, ScoreBreakdown breakdown)
        {
            // 战斗情境放大 DPS 权重
            float contextMult = (context == GearContext.Combat) ? 1.5f : 1.0f;

            if (gear.def.IsMeleeWeapon)
            {
                float dps = gear.GetStatValue(StatDefOf.MeleeWeapon_AverageDPS);
                float score = dps * weights.w_dps * contextMult;
                breakdown.AddScore(Name, $"近战DPS{dps:F1} × {weights.w_dps:F1}" +
                    (contextMult > 1f ? $" × {contextMult:F1}(战斗)" : ""), score);
            }
            else if (gear.def.IsRangedWeapon)
            {
                float dmgMult = gear.GetStatValue(StatDefOf.RangedWeapon_DamageMultiplier);
                float dmgScore = dmgMult * weights.w_dmg * contextMult;
                breakdown.AddScore(Name, $"伤害倍率{dmgMult:F2} × {weights.w_dmg:F1}" +
                    (contextMult > 1f ? $" × {contextMult:F1}(战斗)" : ""), dmgScore);

                float cooldown = gear.GetStatValue(StatDefOf.RangedWeapon_Cooldown);
                if (cooldown > 0f)
                {
                    float cdScore = (1f / cooldown) * weights.w_cooldown * contextMult;
                    breakdown.AddScore(Name, $"射速1/{cooldown:F2} × {weights.w_cooldown:F1}", cdScore);
                }
            }
        }
    }
}
