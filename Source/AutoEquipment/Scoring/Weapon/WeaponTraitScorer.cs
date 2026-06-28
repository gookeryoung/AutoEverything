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

        // 多 degree 特质：ShootingAccuracy 单一 defName，degree 区分乱开枪(-1)/冷枪手(+1)
        // 禁止把 degree 的 label（"Trigger-happy"/"Careful shooter"）当作 defName 查询
        private static readonly TraitDef shootingAccuracyDef = DefDatabase<TraitDef>.GetNamed("ShootingAccuracy", false);

        // 原生 DefOf（Brawler）始终存在，无需 null 检查
        // Nimble/Bloodlust/Tough 是真实 defName，但不在原生 DefOf 中，需安全查询
        private static readonly TraitDef nimbleDef = DefDatabase<TraitDef>.GetNamed("Nimble", false);
        private static readonly TraitDef bloodlustDef = DefDatabase<TraitDef>.GetNamed("Bloodlust", false);
        private static readonly TraitDef toughDef = DefDatabase<TraitDef>.GetNamed("Tough", false);

        public void Score(Pawn pawn, Thing gear, Role role, GearContext context, GearWeights weights, ScoreBreakdown breakdown)
        {
            if (pawn.story?.traits == null) return;

            bool isMelee = gear.def.IsMeleeWeapon;
            bool isRanged = gear.def.IsRangedWeapon;

            // 格斗者特质（TraitDefOf.Brawler 始终存在）：绝对拒绝远程武器
            // 仅真正的 Brawler 特质才拒绝远程，技能型 Brawler（基于技能判定，无特质）不拒绝
            // 设计意图：技能型 Brawler 近战远程双修，应优先远程武器，贴身时切换近战副武器
            if (pawn.story.traits.HasTrait(TraitDefOf.Brawler))
            {
                if (isRanged)
                {
                    breakdown.Veto(-9000f);
                    breakdown.AddScore(Name, "格斗者特质+远程=拒绝", -9000f);
                    return;
                }

                if (isMelee)
                {
                    breakdown.AddScore(Name, "格斗者特质+近战", 50f);
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

            // ShootingAccuracy 多 degree 特质：
            //   degree=-1 = 乱开枪（TriggerHappy）
            //   degree=+1 = 冷枪手（CarefulShooter）
            if (shootingAccuracyDef != null && isRanged)
            {
                int degree = pawn.story.traits.DegreeOfTrait(shootingAccuracyDef);

                if (degree < 0)
                {
                    // 乱开枪：偏好高射速短冷却、近距离
                    float cooldown = gear.GetStatValue(StatDefOf.RangedWeapon_Cooldown);
                    if (cooldown < 1.5f)
                        breakdown.AddScore(Name, "乱开枪+短冷却", 30f);

                    float range = WeaponRangeHelper.GetRange(gear);
                    if (range < 15f)
                        breakdown.AddScore(Name, "乱开枪+近距离", 20f);
                    else if (range > 25f)
                        breakdown.AddScore(Name, "乱开枪+长射程(厌恶)", -20f);
                }
                else if (degree > 0)
                {
                    // 冷枪手：偏好高精度长射程武器
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
}
