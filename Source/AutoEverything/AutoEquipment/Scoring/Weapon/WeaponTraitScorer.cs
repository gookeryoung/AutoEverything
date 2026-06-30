using System.Collections.Generic;
using RimWorld;
using Verse;
using AutoEverything.Core;
using AutoEverything.RoleEvaluation;
using AutoEverything.AutoEquipment;

namespace AutoEverything.AutoEquipment.Scoring.Weapon
{
    /// <summary>
    /// 武器特质评分：基于 Pawn 的特质直接驱动评分。
    /// 关键设计：取消"角色契合度硬编码"，改为特质直接加分。
    /// 兴趣度（火焰）在此处通过技能分体现，特质层只处理硬性特质偏好。
    /// </summary>
    public class WeaponTraitScorer : IScorer<Thing>
    {
        public string Name => "特质";

        // TraitDef 查询统一由 TraitDefCache 提供（集中管理，避免与 CombatEvaluator 重复定义）
        // Brawler 是原生 DefOf 始终存在，直接引用 TraitDefOf.Brawler

        public void Score(Pawn pawn, Thing gear, Role role, GearContext context, GearWeights weights, ScoreBreakdown breakdown)
        {
            if (pawn.story?.traits == null) return;

            bool isMelee = gear.def.IsMeleeWeapon;
            bool isRanged = gear.def.IsRangedWeapon;

            // 角色定位硬约束：仅 Brawler（重甲前排）允许近战武器
            // 设计意图：Worker/Doctor/Pacifist/Default（Light）轻甲无防护不宜近战，
            // Shooter/Hunter/Leader（Flexible）应优先远程输出，近战武器会让他们失去远程优势
            if (isMelee && role != Role.Brawler)
            {
                breakdown.Veto(-9000f);
                breakdown.AddScore(Name, "非格斗者+近战=拒绝", -9000f);
                return;
            }

            // 护盾腰带：拒绝远程武器（护盾会阻挡所有远程射击，远程武器无效）
            // 设计意图：带护盾腰带的角色只能近战，远程武器对持盾者完全无用
            if (isRanged && IsWearingShieldBelt(pawn))
            {
                breakdown.Veto(-9000f);
                breakdown.AddScore(Name, "护盾腰带+远程=拒绝", -9000f);
                return;
            }

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
            if (TraitDefCache.Nimble != null && pawn.story.traits.HasTrait(TraitDefCache.Nimble) && isMelee)
            {
                breakdown.AddScore(Name, "敏捷+近战", 30f);
            }

            // 嗜血：偏好近战暴力
            if (TraitDefCache.Bloodlust != null && pawn.story.traits.HasTrait(TraitDefCache.Bloodlust) && isMelee)
            {
                breakdown.AddScore(Name, "嗜血+近战", 30f);
            }

            // 强健：适合前排近战
            if (TraitDefCache.Tough != null && pawn.story.traits.HasTrait(TraitDefCache.Tough) && isMelee)
            {
                breakdown.AddScore(Name, "强健+近战", 20f);
            }

            // ShootingAccuracy 多 degree 特质：
            //   degree=-1 = 乱开枪（TriggerHappy）
            //   degree=+1 = 冷枪手（CarefulShooter）
            if (TraitDefCache.ShootingAccuracy != null && isRanged)
            {
                int degree = pawn.story.traits.DegreeOfTrait(TraitDefCache.ShootingAccuracy);

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

        // 检查 Pawn 是否穿戴护盾腰带（用 GearDefClassifier.IsShieldBelt 遍历 apparel）
        // 局部实现避免跨命名空间依赖
        private static bool IsWearingShieldBelt(Pawn pawn)
        {
            if (pawn.apparel?.WornApparel == null) return false;
            List<Apparel> worn = pawn.apparel.WornApparel;
            for (int i = 0; i < worn.Count; i++)
            {
                if (GearDefClassifier.IsShieldBelt(worn[i])) return true;
            }
            return false;
        }
    }
}