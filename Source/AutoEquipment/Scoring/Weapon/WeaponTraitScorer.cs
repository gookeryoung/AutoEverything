using RimWorld;
using Verse;

namespace AutoEquipment.Scoring.Weapon
{
    /// <summary>
    /// 武器特质评分：基于 Pawn 的特质直接驱动评分。
    /// 关键设计：取消"角色契合度硬编码"，改为特质直接加分。
    /// 兴趣度（火焰）在此处通过技能分体现，特质层只处理硬性特质偏好。
    /// </summary>
    public class WeaponTraitScorer : IScorer<Thing>
    {
        public string Name => "特质";

        public void Score(Pawn pawn, Thing gear, Role role, GearContext context,
                          GearWeights weights, ScoreBreakdown breakdown)
        {
            if (pawn.story?.traits == null) return;

            bool isMelee = gear.def.IsMeleeWeapon;
            bool isRanged = gear.def.IsRangedWeapon;

            // 格斗者特质：绝对拒绝远程武器
            // 这是特质的设计语义，不数据化，保留极端值
            if (pawn.story.traits.HasTrait(TraitDefOf.Brawler))
            {
                if (isRanged)
                {
                    breakdown.Veto(-9000f);
                    breakdown.AddScore(Name, "格斗者+远程=拒绝", -9000f);
                    return; // 否决后无需继续
                }
                if (isMelee)
                {
                    breakdown.AddScore(Name, "格斗者+近战", 50f);
                }
            }

            // 乱开枪：偏好高射速短冷却武器
            // 需要找到 ShootingAccuracy 特质并检查 degree（-1 = 乱开枪）
            var triggerHappy = DefDatabase<TraitDef>.GetNamedSilent("TriggerHappy");
            if (triggerHappy != null && pawn.story.traits.HasTrait(triggerHappy, -1) && isRanged)
            {
                float cooldown = gear.GetStatValue(StatDefOf.RangedWeapon_Cooldown);
                if (cooldown < 1.5f)
                    breakdown.AddScore(Name, "乱开枪+短冷却", 30f);

                float range = WeaponRangeHelper.GetRange(gear);
                if (range < 15f)
                    breakdown.AddScore(Name, "乱开枪+近距离", 20f);
                if (range > 25f)
                    breakdown.AddScore(Name, "乱开枪+长射程(厌恶)", -20f);
            }

            // 冷枪手：偏好高精度长射程武器
            // degree = 1 表示冷枪手（ShootingAccuracy 的正向）
            var carefulShooter = DefDatabase<TraitDef>.GetNamedSilent("CarefulShooter");
            if (carefulShooter == null)
                carefulShooter = triggerHappy; // 1.6 中可能合并为同一特质 def

            if (carefulShooter != null && pawn.story.traits.HasTrait(carefulShooter, 1) && isRanged)
            {
                float range = WeaponRangeHelper.GetRange(gear);
                if (range > 25f)
                    breakdown.AddScore(Name, "冷枪手+长射程", 30f);

                float cooldown = gear.GetStatValue(StatDefOf.RangedWeapon_Cooldown);
                if (cooldown < 1.5f)
                    breakdown.AddScore(Name, "冷枪手+短冷却(厌恶)", -20f);
            }

            // 嗜血：偏好近战暴力
            var bloodlust = TraitDef.Named("Bloodlust");
            if (bloodlust != null && pawn.story.traits.HasTrait(bloodlust) && isMelee)
            {
                breakdown.AddScore(Name, "嗜血+近战", 30f);
            }

            // 强健：适合前排近战
            var tough = TraitDef.Named("Tough");
            if (tough != null && pawn.story.traits.HasTrait(tough) && isMelee)
            {
                breakdown.AddScore(Name, "强健+近战", 20f);
            }
        }
    }
}
