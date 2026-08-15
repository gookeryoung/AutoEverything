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
    /// 6 种角色定位（形状区分，颜色统一深红）：
    /// - <see cref="RoleIconType.Tough"/>（坚韧，盾）：坚韧（Tough）特质
    ///   设计意图：高生存力单位（减伤 50%），无论是否近战都值得标识
    ///   用户决策（2026-07-21）：带坚韧的角色一律标记 Tough 标识，与 Frontline 解耦
    /// - <see cref="RoleIconType.Frontline"/>（前排，盾）：坚韧（Tough）+ 格斗（Brawler 特质 或 近战 Major）
    ///   设计意图：高生存力的近战单位，应优先装备重甲
    /// - <see cref="RoleIconType.Ranged"/>（远程，弓箭）：乱开枪（ShootingAccuracy degree=-1）+ 射击有火（Major 或 Minor）
    ///   设计意图：DPS 突出的远程单位，应优先承担射击任务
    ///   用户决策（2026-07-21）：扩展到含单火（Minor），覆盖"乱开枪+单火"S 档高价值角色
    /// - <see cref="RoleIconType.Crafter"/>（手工，锤子铁砧）：工作狂（Industriousness degree≥1）+ 神经质（Neurotic degree≥1）
    ///   设计意图：生产效率突出的单位（工作狂+神经质组合），应优先承担专业工作
    /// - <see cref="RoleIconType.Worker"/>（工人，扳手螺丝刀）：工作狂（Industriousness degree≥1）或 严重神经质（Neurotic degree≥1）
    ///   设计意图：工作倾向单位，单方面拥有工作狂或严重神经质即标记
    ///   用户决策（2026-08-13）：新增工人图标，判定为 OR 关系
    ///   与 Crafter 的关系：Crafter 判定为 AND（工作狂+神经质），是 Worker 的子集；
    ///   工作狂+神经质的殖民者会同时显示 Crafter + Worker 两个图标（Crafter 标识精确组合，Worker 标识工作倾向）
    /// - <see cref="RoleIconType.Trader"/>（贸易，钱袋）：俊俏/沉鱼落雁（Beauty degree≥1）+ 高社交（Social Major 或 Level≥8）
    ///   设计意图：社交优势单位，适合外交与贸易
    ///
    /// 颜色策略（用户决策 2026-07-21）：
    /// - 所有图标统一深红色 RGB(0.6, 0.0, 0.0)，避免多色看不清
    /// - 形状本身已足够区分 6 种角色定位，颜色不再做分类
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
            Worker,     // 工人（扳手螺丝刀）
            Trader      // 贸易（钱袋）
        }

        /// <summary>
        /// 统一图标颜色：深红色。
        /// 用户决策（2026-07-21）：原橙/绿/粉三色在殖民者栏小尺寸下看不清，统一深红提升可读性。
        /// </summary>
        public static readonly Color IconColor = new Color(0.6f, 0.0f, 0.0f);

        // 复用缓冲区：避免每帧分配（殖民者栏每帧绘制多个 Pawn，调用频繁）
        // 单线程主线程使用，无需并发保护
        private static readonly List<RoleIconType> buffer = new List<RoleIconType>(6);

        // 社交技能等级阈值：达到此等级视为"高社交"，配合 Beauty 特质触发 Trader 图标
        private const int SocialLevelThreshold = 8;

        // ===================== 角色图标掩码缓存 =====================
        // 缓解卡滞（2026-08-15）：殖民者栏 DrawColonist Postfix 每帧对每个殖民者调用
        // GetRoleIcons——6 次特质查询（HasTrait/DegreeOfTrait 遍历特质列表）
        // + 5 次技能查询（GetSkill 遍历技能列表），12 殖民者即每帧百余次列表遍历。
        // 特质/技能在游戏中极少变化，掩码缓存后命中路径仅 1 次字典查询 + 位展开（零分配）。
        // TTL 2500 tick 与 TierCacheService 评级缓存一致：后天获得特质/激情变化最迟 42 秒内反映。
        private struct IconCacheEntry
        {
            public byte mask;   // 位 i = 1 表示拥有 RoleIconType 枚举值 i 的角色
            public int tick;
        }

        private const int IconCacheTTL = 2500;
        private const int IconCacheCleanupInterval = 60000;
        private static readonly Dictionary<Pawn, IconCacheEntry> iconCache = new Dictionary<Pawn, IconCacheEntry>();
        private static readonly List<Pawn> iconCleanupBuffer = new List<Pawn>();
        private static int lastIconCleanupTick = -9999;

        /// <summary>
        /// 获取 Pawn 符合的所有角色定位。
        /// 返回的 List 由内部缓冲区复用，调用方应在当帧消费完毕，不应跨帧持有引用。
        /// 判定结果按 Pawn 缓存 2500 tick，避免每帧重复查询特质/技能。
        /// </summary>
        public static List<RoleIconType> GetRoleIcons(Pawn pawn)
        {
            buffer.Clear();
            if (pawn == null || pawn.Dead) return buffer;

            int tick = Find.TickManager.TicksGame;
            MaybeCleanupIconCache(tick);

            byte mask;
            if (iconCache.TryGetValue(pawn, out IconCacheEntry e) && tick - e.tick < IconCacheTTL)
            {
                mask = e.mask;
            }
            else
            {
                mask = ComputeRoleMask(pawn);
                iconCache[pawn] = new IconCacheEntry { mask = mask, tick = tick };
            }

            // 掩码展开到共享 buffer（位序与 RoleIconType 枚举值一致）
            if ((mask & (1 << (int)RoleIconType.Tough)) != 0) buffer.Add(RoleIconType.Tough);
            if ((mask & (1 << (int)RoleIconType.Frontline)) != 0) buffer.Add(RoleIconType.Frontline);
            if ((mask & (1 << (int)RoleIconType.Ranged)) != 0) buffer.Add(RoleIconType.Ranged);
            if ((mask & (1 << (int)RoleIconType.Crafter)) != 0) buffer.Add(RoleIconType.Crafter);
            if ((mask & (1 << (int)RoleIconType.Worker)) != 0) buffer.Add(RoleIconType.Worker);
            if ((mask & (1 << (int)RoleIconType.Trader)) != 0) buffer.Add(RoleIconType.Trader);
            return buffer;
        }

        /// <summary>
        /// 计算角色定位掩码（原 GetRoleIcons 判定逻辑，仅缓存 miss 时执行）。
        /// </summary>
        private static byte ComputeRoleMask(Pawn pawn)
        {
            if (pawn.story?.traits == null) return 0;
            if (pawn.skills == null) return 0;

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

            byte mask = 0;

            // Tough：坚韧特质（一律标记，与 Frontline 解耦）
            // 用户决策（2026-07-21）：带坚韧的角色一律标记 Tough 标识
            // 设计：Tough 提供减伤 50% 是高价值特质，无论近战远程都值得标识
            if (isTough)
                mask |= (byte)(1 << (int)RoleIconType.Tough);

            // Frontline：坚韧 + 格斗（Brawler 特质 或 近战 Major）
            // 设计：Tough 提供减伤 50% 是核心，Brawler/MeleeMajor 标识近战倾向
            if (isTough && (isBrawler || meleeMajor))
                mask |= (byte)(1 << (int)RoleIconType.Frontline);

            // Ranged：乱开枪 + 射击有火（Major 或 Minor）
            // 设计：乱开枪+双火为 SSS/SS，乱开枪+单火为 S（均为高价值远程单位）
            // 用户决策（2026-07-21）：扩展到含 Minor，覆盖"乱开枪+单火"S 档高价值角色
            if (isTriggerHappy && (shootingMajor || shootingMinor))
                mask |= (byte)(1 << (int)RoleIconType.Ranged);

            // Crafter：工作狂 + 神经质（AND，精确组合）
            // 设计：工作狂神经质组合是 S 评级核心，生产效率突出
            // 注：Crafter 是 Worker 的子集——工作狂+神经质的殖民者会同时显示 Crafter + Worker
            if (hasIndustrious && hasNeurotic)
                mask |= (byte)(1 << (int)RoleIconType.Crafter);

            // Worker：工作狂 或 严重神经质（OR，工作倾向）
            // 用户决策（2026-08-13）：单方面拥有工作狂或严重神经质即标记为工人
            // 设计：工作狂（degree≥1，含勤奋+非常勤奋）或严重神经质（degree≥1，含神经质+非常神经质）
            // 与 Crafter 关系：Crafter(AND) 是 Worker(OR) 的子集，两者可同时显示
            if (hasIndustrious || hasNeurotic)
                mask |= (byte)(1 << (int)RoleIconType.Worker);

            // Trader：俊俏/沉鱼落雁 + 高社交
            // 设计：Beauty degree≥1 含俊俏(1)/沉鱼落雁(2)，配合高社交（Major 或 Level≥8）触发
            if (hasBeauty && (socialMajor || socialLevel >= SocialLevelThreshold))
                mask |= (byte)(1 << (int)RoleIconType.Trader);

            return mask;
        }

        /// <summary>
        /// 周期清理图标缓存：移除 Dead/!Spawned 的 Pawn，避免引用泄漏。
        /// 模式与 TierCacheService.MaybeCleanup 一致，由 GetRoleIcons 触发。
        /// </summary>
        private static void MaybeCleanupIconCache(int tick)
        {
            if (tick - lastIconCleanupTick < IconCacheCleanupInterval) return;
            lastIconCleanupTick = tick;

            iconCleanupBuffer.Clear();
            foreach (var kvp in iconCache)
            {
                Pawn p = kvp.Key;
                if (p == null || p.Dead || !p.Spawned)
                    iconCleanupBuffer.Add(p);
            }
            for (int i = 0; i < iconCleanupBuffer.Count; i++)
                iconCache.Remove(iconCleanupBuffer[i]);
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
