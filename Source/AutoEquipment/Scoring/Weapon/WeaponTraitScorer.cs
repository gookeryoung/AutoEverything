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

        // 缓存 TraitDef 查找，避免 Tick 路径每次重复字典查询
        // 使用 GetNamed(defName, false) 安全查询：未找到返回 null 而非抛异常

        // 远程特质
        // 特质： 乱开枪（TriggerHappy）
        private static readonly TraitDef triggerHappyDef = DefDatabase<TraitDef>.GetNamed("Trigger-happy", false);
        // 特质： 冷枪手（CarefulShooter）
        private static readonly TraitDef carefulShooterDef = DefDatabase<TraitDef>.GetNamed("Careful shooter", false);

        // 近战特质
        // 特质： 格斗者（Brawler）
        private static readonly TraitDef brawlerDef = DefDatabase<TraitDef>.GetNamed("Brawler", false);
        // 特质： 敏捷（Nimble）
        private static readonly TraitDef nimbleDef = DefDatabase<TraitDef>.GetNamed("Nimble", false);
        // 特质： 嗜血（Bloodlust）
        private static readonly TraitDef bloodlustDef = DefDatabase<TraitDef>.GetNamed("Bloodlust", false);
        // 特质： 坚韧（Tough）
        private static readonly TraitDef toughDef = DefDatabase<TraitDef>.GetNamed("Tough", false);

        public void Score(Pawn pawn, Thing gear, Role role, GearContext context,
                          GearWeights weights, ScoreBreakdown breakdown)
        {
            if (pawn.story?.traits == null) return;

            bool isMelee = gear.def.IsMeleeWeapon;
            bool isRanged = gear.def.IsRangedWeapon;

            // 格斗者特质：绝对拒绝远程武器
            if (brawlerDef != null && pawn.story.traits.HasTrait(brawlerDef))
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

            // 敏捷特质：偏好近战
            if (nimbleDef != null && pawn.story.traits.HasTrait(nimbleDef) && isMelee)
            {
                breakdown.AddScore(Name, "敏捷+近战", 30f);
            }

            // 嗜血：偏好近战暴力
            if (bloodlustDef != null && pawn.story.traits.HasTrait(bloodlustDef) && isMelee)
            {
                breakdown.AddScore(Name, "嗜血+近战", 30f);
            }

            // 强健：适合前排近战
            if (toughDef != null && pawn.story.traits.HasTrait(toughDef) && isMelee)
            {
                breakdown.AddScore(Name, "强健+近战", 20f);
            }

            // 乱开枪：偏好高射速短冷却武器
            if (pawn.story.traits.HasTrait(triggerHappyDef))
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
            else if (pawn.story.traits.HasTrait(carefulShooterDef))
            {
                float range = WeaponRangeHelper.GetRange(gear);
                if (range > 25f)
                    breakdown.AddScore(Name, "冷枪手+长射程", 30f);

                float cooldown = gear.GetStatValue(StatDefOf.RangedWeapon_Cooldown);
                if (cooldown < 1.5f)
                    breakdown.AddScore(Name, "冷枪手+短冷却(厌恶)", -20f);
            }
        }
    }
}
