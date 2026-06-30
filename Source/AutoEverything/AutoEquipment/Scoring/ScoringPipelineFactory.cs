using System.Collections.Generic;
using RimWorld;
using Verse;
using AutoEverything.AutoEquipment.Scoring.Weapon;
using AutoEverything.AutoEquipment.Scoring.Apparels;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.AutoEquipment.Scoring
{
    /// <summary>
    /// 评分管线工厂：根据装备类型创建对应的评分管线。
    /// 设计模式：工厂模式——封装管线构建逻辑，便于扩展新类型。
    /// 静态缓存管线实例，避免每次评分重新构建。
    /// </summary>
    public static class ScoringPipelineFactory
    {
        // 静态缓存：管线实例在游戏生命周期内复用
        // 所有 Scorer 内部无状态，可安全共享
        private static ScoringPipeline<Thing> weaponPipeline;
        private static ScoringPipeline<Apparel> apparelPipeline;

        /// <summary>
        /// 获取武器评分管线。
        /// 顺序：生物编码 → 特质 → 技能 → 情境 → 伤害 → 射程 → 品质 → 意识形态 → 耐久
        /// 生物编码在前以短路否决。
        /// </summary>
        public static ScoringPipeline<Thing> GetWeaponPipeline()
        {
            if (weaponPipeline == null)
            {
                var scorers = new List<IScorer<Thing>>
                {
                    new WeaponBiocodedScorer(),     // 硬性约束，先检查
                    new WeaponTraitScorer(),        // 特质（含格斗者否决）
                    new WeaponSkillScorer(),        // 技能+兴趣度
                    new WeaponContextScorer(),      // 情境
                    new WeaponDpsScorer(),          // DPS/伤害
                    new WeaponRangeScorer(),        // 射程
                    new WeaponQualityScorer(),      // 品质
                    new WeaponIdeologyScorer(),     // 意识形态
                    new WeaponDurabilityScorer()    // 耐久修正（最后）
                };
                weaponPipeline = new ScoringPipeline<Thing>(scorers);
            }
            return weaponPipeline;
        }

        /// <summary>
        /// 获取防具评分管线。
        /// 顺序：护盾腰带约束 → 沾染 → 特质 → 工作 → 情境 → 护甲 → 保温 → 移速 → 品质 → 皇家 → 意识形态 → 耐久 → 当前穿戴
        /// 护盾腰带约束放首位：非 Brawler 角色 + 护盾腰带直接 Veto 短路，省后续计算。
        /// 耐久修正放在末尾，作为对最终分数的乘数修正（损坏防具按 HP 比例扣分）。
        /// </summary>
        public static ScoringPipeline<Apparel> GetApparelPipeline()
        {
            if (apparelPipeline == null)
            {
                var scorers = new List<IScorer<Apparel>>
                {
                    new ApparelShieldBeltScorer(),   // 护盾腰带硬约束（非 Brawler 拒绝）
                    new ApparelTaintedScorer(),     // 沾染惩罚
                    new ApparelTraitScorer(),        // 特质
                    new ApparelWorkScorer(),         // 工作加成
                    new ApparelContextScorer(),      // 情境（温度）
                    new ApparelArmorScorer(),       // 护甲
                    new ApparelInsulationScorer(),  // 保温
                    new ApparelMoveSpeedScorer(),   // 移速
                    new ApparelQualityScorer(),     // 品质
                    new ApparelRoyaltyScorer(),     // 皇家头衔
                    new ApparelIdeologyScorer(),     // 意识形态
                    new ApparelDurabilityScorer(),   // 耐久修正（按 HP 比例乘数）
                    new ApparelCurrentWornScorer()  // 平局决胜
                };
                apparelPipeline = new ScoringPipeline<Apparel>(scorers);
            }
            return apparelPipeline;
        }
    }
}