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
    /// 5 种角色定位（形状区分，颜色统一深红）：
    /// - <see cref="RoleIconType.Tough"/>（坚韧，盾）：坚韧（Tough）特质
    ///   设计意图：高生存力单位（减伤 50%），无论是否近战都值得标识
    ///   用户决策（2026-07-21）：带坚韧的角色一律标记 Tough 标识，与 Frontline 解耦
    /// - <see cref="RoleIconType.Frontline"/>（前排，盾）：坚韧（Tough）+ 格斗（Brawler 特质 或 近战 Major）
    ///   设计意图：高生存力的近战单位，应优先装备重甲
    /// - <see cref="RoleIconType.Ranged"/>（远程，弓箭）：乱开枪（ShootingAccuracy degree=-1）+ 射击有火（Major 或 Minor）
    ///   设计意图：DPS 突出的远程单位，应优先承担射击任务
    ///   用户决策（2026-07-21）：扩展到含单火（Minor），覆盖"乱开枪+单火"S 档高价值角色
    /// - <see cref="RoleIconType.Crafter"/>（手工，锤子铁砧）：工作狂（Industriousness degree≥1）+ 神经质（Neurotic degree≥1）
    ///   设计意图：生产效率突出的单位，应优先承担专业工作
    /// - <see cref="RoleIconType.Trader"/>（贸易，钱袋）：俊俏/沉鱼落雁（Beauty degree≥1）+ 高社交（Social Major 或 Level≥8）
    ///   设计意图：社交优势单位，适合外交与贸易
    ///
    /// 颜色策略（用户决策 2026-07-21）：
    /// - 所有图标统一深红色 RGB(0.6, 0.0, 0.0)，避免多色看不清
    /// - 形状本身已足够区分 5 种角色定位，颜色不再做分类
    ///
    /// 一个殖民者可同时符合多个角色定位（如坚韧格斗 + 工作狂神经质），图标横向排列显示。
    ///
    /// 判定依据复用 <see cref="TraitDefCache"/> 与原生 <see cref="TraitDefOf"/>，
    /// 不重复定义 TraitDef 查询。特质 degree 判定与 CombatEvaluator 评级规则保持一致。
    /// </summary>
    public static class RoleIconDef
    {
        /// <summary>
        /// 角色定位图标类型：按战斗/工作/交易三大类分组，形状区分（颜色统一深红）。
        /// </summary>
        public enum RoleIconType : byte
        {
            Tough,      // 坚韧（盾）
            Frontline,  // 前排（盾）
            Ranged,     // 远程（弓箭）
            Crafter,    // 手工（锤子铁砧）
            Trader      // 贸易（钱袋）
        }

        /// <summary>
        /// 统一图标颜色：深红色。
        /// 用户决策（2026-07-21）：原橙/绿/粉三色在殖民者栏小尺寸下看不清，统一深红提升可读性。
        /// </summary>
        public static readonly Color IconColor = new Color(0.6f, 0.0f, 0.0f);

        // 复用缓冲区：避免每帧分配（殖民者栏每帧绘制多个 Pawn，调用频繁）
        // 单线程主线程使用，无需并发保护
        private static readonly List<RoleIconType> buffer = new List<RoleIconType>(5);

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
            bool shootingMinor = IsMinorPassion(pawn, SkillDefOf.Shooting);
            bool socialMajor = IsMajorPassion(pawn, SkillDefOf.Social);
            int socialLevel = GetSkillLevel(pawn, SkillDefOf.Social);

            // Tough：坚韧特质（一律标记，与 Frontline 解耦）
            // 用户决策（2026-07-21）：带坚韧的角色一律标记 Tough 标识
            // 设计：Tough 提供减伤 50% 是高价值特质，无论近战远程都值得标识
            if (isTough)
                buffer.Add(RoleIconType.Tough);

            // Frontline：坚韧 + 格斗（Brawler 特质 或 近战 Major）
            // 设计：Tough 提供减伤 50% 是核心，Brawler/MeleeMajor 标识近战倾向
            if (isTough && (isBrawler || meleeMajor))
                buffer.Add(RoleIconType.Frontline);

            // Ranged：乱开枪 + 射击有火（Major 或 Minor）
            // 设计：乱开枪+双火为 SSS/SS，乱开枪+单火为 S（均为高价值远程单位）
            // 用户决策（2026-07-21）：扩展到含 Minor，覆盖"乱开枪+单火"S 档高价值角色
            if (isTriggerHappy && (shootingMajor || shootingMinor))
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
        /// 获取角色定位对应的颜色（统一深红色，形状区分角色类型）。
        /// </summary>
        public static Color GetColor(RoleIconType type)
        {
            return IconColor;
        }

        private static bool IsMajorPassion(Pawn pawn, SkillDef skill)
        {
            SkillRecord record = pawn.skills?.GetSkill(skill);
            return record != null && record.passion == Passion.Major;
        }

        private static bool IsMinorPassion(Pawn pawn, SkillDef skill)
        {
            SkillRecord record = pawn.skills?.GetSkill(skill);
            return record != null && record.passion == Passion.Minor;
        }

        private static int GetSkillLevel(Pawn pawn, SkillDef skill)
        {
            SkillRecord record = pawn.skills?.GetSkill(skill);
            return record != null ? record.Level : 0;
        }
    }
}
