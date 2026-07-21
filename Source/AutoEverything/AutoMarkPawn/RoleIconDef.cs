using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using AutoEverything.Core;

namespace AutoEverything.AutoMarkPawn
{
    /// <summary>
    /// 角色定位图标判定：为殖民者栏 Rect 上的角色定位图标提供判定与取色。
    ///
    /// 4 种角色定位（颜色 + 形状双重区分）：
    /// - <see cref="RoleIconType.Frontline"/>（前排，盾）：坚韧（Tough）+ 格斗（Brawler 特质 或 近战 Major）
    ///   设计意图：高生存力的近战单位，应优先装备重甲
    /// - <see cref="RoleIconType.Ranged"/>（远程，弓箭）：乱开枪（ShootingAccuracy degree=-1）+ 射击火（Major）
    ///   设计意图：DPS 突出的远程单位，应优先承担射击任务
    /// - <see cref="RoleIconType.Crafter"/>（手工，锤子铁砧）：工作狂（Industriousness degree≥1）+ 神经质（Neurotic degree≥1）
    ///   设计意图：生产效率突出的单位，应优先承担专业工作
    /// - <see cref="RoleIconType.Trader"/>（贸易，钱袋）：俊俏/沉鱼落雁（Beauty degree≥1）+ 高社交（Social Major 或 Level≥8）
    ///   设计意图：社交优势单位，适合外交与贸易
    ///
    /// 颜色分组（玩家可读，参考 UsefulMarks 设计）：
    /// - 战斗类（Frontline/Ranged）：橙色 RGB(1.0, 0.55, 0.06)
    /// - 工作类（Crafter）：绿色 RGB(0.2, 0.8, 0.2)
    /// - 交易类（Trader）：粉红 RGB(1.0, 0.4, 0.7)
    ///
    /// 一个殖民者可同时符合多个角色定位（如坚韧格斗 + 工作狂神经质），图标横向排列显示。
    ///
    /// 判定依据复用 <see cref="TraitDefCache"/> 与原生 <see cref="TraitDefOf"/>，
    /// 不重复定义 TraitDef 查询。特质 degree 判定与 CombatEvaluator 评级规则保持一致。
    /// </summary>
    public static class RoleIconDef
    {
        /// <summary>
        /// 角色定位图标类型：按战斗/工作/交易三大类分组，颜色由 <see cref="GetColor"/> 提供。
        /// </summary>
        public enum RoleIconType : byte
        {
            Frontline,  // 前排（盾，橙色）
            Ranged,     // 远程（弓箭，橙色）
            Crafter,    // 手工（锤子铁砧，绿色）
            Trader      // 贸易（钱袋，粉红）
        }

        /// <summary>战斗类颜色（前排/远程共用）：橙色</summary>
        public static readonly Color CombatColor = new Color(1.0f, 0.55f, 0.06f);

        /// <summary>工作类颜色（手工）：绿色</summary>
        public static readonly Color WorkColor = new Color(0.2f, 0.8f, 0.2f);

        /// <summary>交易类颜色（贸易）：粉红</summary>
        public static readonly Color TradeColor = new Color(1.0f, 0.4f, 0.7f);

        // 复用缓冲区：避免每帧分配（殖民者栏每帧绘制多个 Pawn，调用频繁）
        // 单线程主线程使用，无需并发保护
        private static readonly List<RoleIconType> buffer = new List<RoleIconType>(4);

        // 社交技能等级阈值：达到此等级视为"高社交"，配合 Beauty 特质触发 Trader 图标
        private const int SocialLevelThreshold = 8;

        /// <summary>
        /// 获取 Pawn 符合的所有角色定位。
        /// 返回的 List 由内部缓冲区复用，调用方应在当帧消费完毕，不应跨帧持有引用。
        /// </summary>
        public static List<RoleIconType> GetRoleIcons(Pawn pawn)
        {
            buffer.Clear();
            if (pawn == null || pawn.Dead) return buffer;
            if (pawn.story?.traits == null) return buffer;
            if (pawn.skills == null) return buffer;

            // 收集特质状态（复用 TraitDefCache 与 TraitDefOf，与 CombatEvaluator 一致）
            var traits = pawn.story.traits;
            bool isTough = TraitDefCache.Tough != null && traits.HasTrait(TraitDefCache.Tough);
            bool isBrawler = traits.HasTrait(TraitDefOf.Brawler);
            bool isTriggerHappy = TraitDefCache.ShootingAccuracy != null
                && traits.DegreeOfTrait(TraitDefCache.ShootingAccuracy) == -1;
            bool hasIndustrious = TraitDefCache.Industriousness != null
                && traits.DegreeOfTrait(TraitDefCache.Industriousness) >= 1;
            bool hasNeurotic = TraitDefCache.Neurotic != null
                && traits.DegreeOfTrait(TraitDefCache.Neurotic) >= 1;
            bool hasBeauty = TraitDefCache.Beauty != null
                && traits.DegreeOfTrait(TraitDefCache.Beauty) >= 1;

            // 收集技能兴趣状态（直接用 Passion 枚举比较，避免 PassionHelper 间接层）
            bool meleeMajor = IsMajorPassion(pawn, SkillDefOf.Melee);
            bool shootingMajor = IsMajorPassion(pawn, SkillDefOf.Shooting);
            bool socialMajor = IsMajorPassion(pawn, SkillDefOf.Social);
            int socialLevel = GetSkillLevel(pawn, SkillDefOf.Social);

            // Frontline：坚韧 + 格斗（Brawler 特质 或 近战 Major）
            // 设计：Tough 提供减伤 50% 是核心，Brawler/MeleeMajor 标识近战倾向
            if (isTough && (isBrawler || meleeMajor))
                buffer.Add(RoleIconType.Frontline);

            // Ranged：乱开枪 + 射击双火
            // 设计：乱开枪 + 双火是 SSS/SS 评级的核心组合，DPS 突出
            if (isTriggerHappy && shootingMajor)
                buffer.Add(RoleIconType.Ranged);

            // Crafter：工作狂 + 神经质
            // 设计：工作狂神经质组合是 S 评级核心，生产效率突出
            if (hasIndustrious && hasNeurotic)
                buffer.Add(RoleIconType.Crafter);

            // Trader：俊俏/沉鱼落雁 + 高社交
            // 设计：Beauty degree≥1 含俊俏(1)/沉鱼落雁(2)，配合高社交（Major 或 Level≥8）触发
            if (hasBeauty && (socialMajor || socialLevel >= SocialLevelThreshold))
                buffer.Add(RoleIconType.Trader);

            return buffer;
        }

        /// <summary>
        /// 获取角色定位对应的颜色（战斗类橙、工作类绿、交易类粉）。
        /// </summary>
        public static Color GetColor(RoleIconType type)
        {
            switch (type)
            {
                case RoleIconType.Frontline:
                case RoleIconType.Ranged:
                    return CombatColor;
                case RoleIconType.Crafter:
                    return WorkColor;
                default:
                    return TradeColor;
            }
        }

        private static bool IsMajorPassion(Pawn pawn, SkillDef skill)
        {
            SkillRecord record = pawn.skills?.GetSkill(skill);
            return record != null && record.passion == Passion.Major;
        }

        private static int GetSkillLevel(Pawn pawn, SkillDef skill)
        {
            SkillRecord record = pawn.skills?.GetSkill(skill);
            return record != null ? record.Level : 0;
        }
    }
}
