using RimWorld;
using Verse;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.AutoEquipment.Scoring.Weapon
{
    /// <summary>
    /// 武器伤害输出评分：近战看 DPS + 护甲穿透，远程看伤害倍率与冷却。
    /// 战斗情境下权重放大 1.5 倍。
    /// 护甲穿透解决单分子剑问题：DPS 不高但穿透极高，对高护甲敌人有奇效。
    /// </summary>
    public class WeaponDpsScorer : IScorer<Thing>
    {
        public string Name => "伤害";

        // StatDefOf.MeleeWeapon_AverageArmorPenetration 在 1.6 不存在，用 StatDef.Named 缓存
        private static readonly StatDef MeleeAvgApStat = StatDef.Named("MeleeWeapon_AverageArmorPenetration");

        public void Score(Pawn pawn, Thing gear, Role role, GearContext context,
                          GearWeights weights, ScoreBreakdown breakdown)
        {
            // 战斗情境放大 DPS 权重
            float contextMult = (context == GearContext.Combat) ? 1.5f : 1.0f;

            if (gear.def.IsMeleeWeapon)
            {
                float dps = gear.GetStatValue(StatDefOf.MeleeWeapon_AverageDPS);
                float score = dps * weights.w_dps * contextMult;
                breakdown.AddScore(Name, breakdown.CollectItems ? ($"近战DPS{dps:F1} × {weights.w_dps:F1}" +
                    (contextMult > 1f ? $" × {contextMult:F1}(战斗)" : "")) : null, score);

                // 护甲穿透（0~1 小数）：单分子剑 0.83，长剑 0.33
                float ap = gear.GetStatValue(MeleeAvgApStat);
                if (ap > 0f)
                {
                    float apScore = ap * weights.w_armorPenetration * contextMult;
                    breakdown.AddScore(Name, breakdown.CollectItems ? ($"护甲穿透{ap:F2} × {weights.w_armorPenetration:F0}" +
                        (contextMult > 1f ? $" × {contextMult:F1}(战斗)" : "")) : null, apScore);
                }
            }
            else if (gear.def.IsRangedWeapon)
            {
                float dmgMult = gear.GetStatValue(StatDefOf.RangedWeapon_DamageMultiplier);
                float dmgScore = dmgMult * weights.w_dmg * contextMult;
                breakdown.AddScore(Name, breakdown.CollectItems ? ($"伤害倍率{dmgMult:F2} × {weights.w_dmg:F1}" +
                    (contextMult > 1f ? $" × {contextMult:F1}(战斗)" : "")) : null, dmgScore);

                float cooldown = gear.GetStatValue(StatDefOf.RangedWeapon_Cooldown);
                if (cooldown > 0f)
                {
                    float cdScore = (1f / cooldown) * weights.w_cooldown * contextMult;
                    breakdown.AddScore(Name, breakdown.CollectItems ? $"射速1/{cooldown:F2} × {weights.w_cooldown:F1}" : null, cdScore);
                }
            }
        }
    }
}