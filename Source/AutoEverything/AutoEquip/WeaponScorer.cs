using System;
using AutoEverything.RoleEvaluation;
using RimWorld;
using Verse;

namespace AutoEverything.AutoEquip
{
    /// <summary>
    /// 把游戏内的武器换算成可比较的数值。
    /// 只负责“算分”，优先级规则全部在 <see cref="EquipPolicy"/>，便于单独测试。
    /// </summary>
    public static class WeaponScorer
    {
        public static WeaponScoreInput Build(ThingWithComps weapon, Pawn pawn, RoleGroup role, WeaponKind kind)
        {
            WeaponScoreInput input = new WeaponScoreInput();
            if (weapon == null || weapon.def == null) return input;

            input.DefName = weapon.def.defName;
            input.QualityRank = GetQualityRank(weapon);

            // 只统计战斗特性价值：纯装饰特性（镶金/镶玉）不构成特化优势；
            // 装饰特性价值为 0 时降级为普通武器，与普通武器一起按品质+战斗评分比较
            input.TraitValue = WeaponClassifier.GetCombatTraitValue(weapon);
            input.IsSpecial = input.TraitValue > 0f;
            input.CombatScore = ComputeCombatScore(weapon, role, kind);
            return input;
        }

        public static int GetQualityRank(ThingWithComps weapon)
        {
            CompQuality quality = weapon.TryGetComp<CompQuality>();
            if (quality == null) return (int)QualityCategory.Normal;
            return (int)quality.Quality;
        }

        private static float ComputeCombatScore(ThingWithComps weapon, RoleGroup role, WeaponKind kind)
        {
            if (kind == WeaponKind.Melee)
                return ComputeMeleeScore(weapon, role);
            if (kind == WeaponKind.Ranged)
                return ComputeRangedScore(weapon, role);
            if (kind == WeaponKind.Thrown)
                return ComputeThrownScore(weapon);
            return 0f;
        }

        /// <summary>
        /// 投掷物评分：按弹丸伤害与爆炸范围加权，不使用市价。
        /// 市价会被材质/装饰特性抬高，不代表杀伤力。
        /// </summary>
        public static float ComputeThrownScore(ThingWithComps weapon)
        {
            if (weapon == null || weapon.def == null) return 0f;

            VerbProperties verb = FindRangedVerb(weapon.def);
            if (verb == null || verb.defaultProjectile == null || verb.defaultProjectile.projectile == null)
                return 0f;

            float score = 0f;
            try
            {
                ProjectileProperties projectile = verb.defaultProjectile.projectile;
                score = projectile.GetDamageAmount(weapon, null);
                if (projectile.explosionRadius > 0f)
                    score *= 1f + projectile.explosionRadius * 0.1f;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] 投掷物评分失败: " + ex.Message,
                    weapon.thingIDNumber ^ 0xA922);
            }
            return score;
        }

        private static float ComputeMeleeScore(ThingWithComps weapon, RoleGroup role)
        {
            float dps = 0f;
            float ap = 0f;
            try
            {
                dps = weapon.GetStatValue(StatDefOf.MeleeWeapon_AverageDPS);

                int toolCount = 0;
                if (weapon.def.tools != null)
                {
                    for (int i = 0; i < weapon.def.tools.Count; i++)
                    {
                        Tool tool = weapon.def.tools[i];
                        if (tool == null) continue;
                        ap += tool.armorPenetration;
                        toolCount++;
                    }
                }
                if (toolCount > 0) ap /= toolCount;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] 近战武器评分失败: " + ex.Message,
                    weapon.thingIDNumber ^ 0xA920);
            }

            float score = dps + ap * 0.5f;
            if (role == RoleGroup.Front) score *= 1.35f;
            else if (role == RoleGroup.Worker || role == RoleGroup.Trade) score *= 0.7f;
            return score;
        }

        private static float ComputeRangedScore(ThingWithComps weapon, RoleGroup role)
        {
            VerbProperties verb = FindRangedVerb(weapon.def);
            if (verb == null) return 0f;

            float damage = 0f;
            float ap = 0f;
            float cooldown = 1f;
            try
            {
                if (verb.defaultProjectile != null && verb.defaultProjectile.projectile != null)
                {
                    damage = verb.defaultProjectile.projectile.GetDamageAmount(weapon, null);
                    ap = verb.defaultProjectile.projectile.GetArmorPenetration(weapon, null);
                }
                damage *= Math.Max(0f, weapon.GetStatValue(StatDefOf.RangedWeapon_DamageMultiplier));
                cooldown = Math.Max(0.1f, weapon.GetStatValue(StatDefOf.RangedWeapon_Cooldown));
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] 远程武器评分失败: " + ex.Message,
                    weapon.thingIDNumber ^ 0xA921);
            }

            int burst = Math.Max(1, verb.burstShotCount);
            float burstTime = (burst - 1) * Math.Max(0, verb.ticksBetweenBurstShots) / 60f;
            float cycle = Math.Max(0.05f, verb.warmupTime + cooldown + burstTime);
            float dps = damage * burst / cycle;

            float accuracy;
            if (role == RoleGroup.Shooter)
                accuracy = verb.accuracyTouch * 0.15f + verb.accuracyShort * 0.25f
                    + verb.accuracyMedium * 0.3f + verb.accuracyLong * 0.3f;
            else
                accuracy = verb.accuracyTouch * 0.3f + verb.accuracyShort * 0.3f
                    + verb.accuracyMedium * 0.25f + verb.accuracyLong * 0.15f;

            float rangeFactor = 0.5f + Math.Min(verb.range, 50f) / 50f;
            float score = dps * Math.Max(0.05f, accuracy) * rangeFactor + ap * 0.5f;

            if (role == RoleGroup.Shooter) score *= 1.25f;
            else if (role == RoleGroup.Front) score *= 0.5f;
            return score;
        }

        private static VerbProperties FindRangedVerb(ThingDef def)
        {
            if (def == null || def.Verbs == null) return null;
            for (int i = 0; i < def.Verbs.Count; i++)
            {
                VerbProperties verb = def.Verbs[i];
                if (verb != null && verb.defaultProjectile != null)
                    return verb;
            }
            return null;
        }
    }
}
