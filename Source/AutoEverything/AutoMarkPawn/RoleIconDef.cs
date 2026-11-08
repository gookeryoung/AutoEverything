using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using AutoEverything.Core;

namespace AutoEverything.AutoMarkPawn
{
    /// <summary>
    /// 角色定位图标判定：为殖民者栏头像上的角色定位图标提供判定与取色。
    ///
    /// 4 种角色定位（形状区分，颜色统一深红）：
    /// - <see cref="RoleIconType.Tough"/>（坚韧，盾）：坚韧（Tough）特质
    /// - <see cref="RoleIconType.Ranged"/>（远程，弓箭）：白名单条件（射击专家 / 乱开枪 / 射击双火 / 射击单火）
    /// - <see cref="RoleIconType.Worker"/>（工人，扳手）：白名单条件（无法施暴 / 生产专家 / 工作狂 / 神经质 / 生产双火）
    /// - <see cref="RoleIconType.Trader"/>（贸易，钱袋）：白名单条件（道德家 / 俊俏 / 社交双火 / 社交等级 ≥ 8）
    ///
    /// 设计变更（2026-11-08）：
    /// - 原 Frontline 图标删除：前排分组框已用文字 + 红边标识，两套"前排"定义不一致易误导
    /// - 原 Crafter 图标并入 Worker：Crafter(工作狂+神经质) 恒为 Worker 的子集，必然同时出现
    /// - 判定从"硬编码 AND 组合"改为与分组一致的白名单制：勾选且命中任一条件即显示
    ///
    /// 与角色分组的联动：角色分组开启与否都始终显示全部图标。
    /// 分组框的边界/标签只表达"分组归属"，头像图标表达"个体能力特征"，两者信息不重复。
    ///
    /// 一个殖民者可同时符合多个角色定位（如坚韧 + 工人），图标横向排列显示。
    /// </summary>
    public static class RoleIconDef
    {
        /// <summary>
        /// 角色定位图标类型：形状区分（颜色统一深红）。
        /// </summary>
        public enum RoleIconType : byte
        {
            Tough,   // 坚韧（盾）
            Ranged,  // 远程（弓箭）
            Worker,  // 工人（扳手螺丝刀）
            Trader   // 贸易（钱袋）
        }

        /// <summary>
        /// 统一图标颜色：深红色。
        /// 用户决策（2026-07-21）：原橙/绿/粉三色在殖民者栏小尺寸下看不清，统一深红提升可读性。
        /// </summary>
        public static readonly Color IconColor = new Color(0.6f, 0.0f, 0.0f);

        // 复用缓冲区：避免每帧分配（殖民者栏每帧绘制多个 Pawn，调用频繁）
        // 单线程主线程使用，无需并发保护
        private static readonly List<RoleIconType> buffer = new List<RoleIconType>(4);

        // 社交技能等级阈值：达到此等级视为"高社交"，配合贸易白名单条件触发 Trader 图标
        private const int SocialLevelThreshold = 8;

        // ===================== 角色图标掩码缓存 =====================
        // 缓解卡滞（2026-08-15）：殖民者栏 DrawColonist Postfix 每帧对每个殖民者调用
        // GetRoleIcons——多次特质/技能/职位查询，12 殖民者即每帧百余次列表遍历。
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
        /// 清空图标判定缓存：白名单条件变化后立即调用，让新规则下一帧生效，无需等待 TTL。
        /// </summary>
        public static void ClearIconCache()
        {
            iconCache.Clear();
        }

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
            // 展开时按显示开关过滤：切换后下一帧即生效，无需清缓存。
            // 角色分组开启时也照常显示全部图标（用户决策 2026-11-08），分组框不替代头像标识。
            if ((mask & (1 << (int)RoleIconType.Tough)) != 0 && AESettings.showRoleIconTough)
                buffer.Add(RoleIconType.Tough);
            if ((mask & (1 << (int)RoleIconType.Ranged)) != 0 && AESettings.showRoleIconRanged)
                buffer.Add(RoleIconType.Ranged);
            if ((mask & (1 << (int)RoleIconType.Worker)) != 0 && AESettings.showRoleIconWorker)
                buffer.Add(RoleIconType.Worker);
            if ((mask & (1 << (int)RoleIconType.Trader)) != 0 && AESettings.showRoleIconTrader)
                buffer.Add(RoleIconType.Trader);
            return buffer;
        }

        /// <summary>
        /// 计算角色定位掩码（缓存 miss 时执行）。
        /// 白名单制：勾选的条件任一命中即置位对应图标，不做额外的 AND 组合。
        /// </summary>
        private static byte ComputeRoleMask(Pawn pawn)
        {
            if (pawn.story?.traits == null) return 0;
            if (pawn.skills == null) return 0;

            var traits = pawn.story.traits;
            byte mask = 0;

            // 坚韧：独立维度，分组规则不覆盖
            // 显示开关只在展开阶段过滤，不参与掩码计算，避免开关切换后缓存残留
            if (TraitDefCache.Tough != null && traits.HasTrait(TraitDefCache.Tough))
            {
                mask |= (byte)(1 << (int)RoleIconType.Tough);
            }

            // 远程：射击专家 / 乱开枪 / 射击双火 / 射击单火，命中任一即标识
            bool ranged =
                (AESettings.iconRangedShootingSpecialist && HasIdeoRole(pawn, "IdeoRole_ShootingSpecialist"))
                || (AESettings.iconRangedTriggerHappy && TraitDefCache.ShootingAccuracy != null
                    && traits.DegreeOfTrait(TraitDefCache.ShootingAccuracy) == -1)
                || (AESettings.iconRangedShootingMajor && IsMajorPassion(pawn, SkillDefOf.Shooting))
                || (AESettings.iconRangedShootingMinor && IsMinorPassion(pawn, SkillDefOf.Shooting));
            if (ranged) mask |= (byte)(1 << (int)RoleIconType.Ranged);

            // 工人（原手工图标已并入）：无法施暴 / 生产专家 / 工作狂 / 神经质 / 生产双火
            bool worker =
                (AESettings.iconWorkerPacifist && pawn.WorkTagIsDisabled(WorkTags.Violent))
                || (AESettings.iconWorkerProductionSpecialist && HasIdeoRole(pawn, "IdeoRole_ProductionSpecialist"))
                || (AESettings.iconWorkerIndustrious && TraitDefCache.Industriousness != null
                    && traits.DegreeOfTrait(TraitDefCache.Industriousness) >= 1)
                || (AESettings.iconWorkerNeurotic && TraitDefCache.Neurotic != null
                    && traits.DegreeOfTrait(TraitDefCache.Neurotic) >= 1)
                || (AESettings.iconWorkerCraftingMajor && IsMajorPassion(pawn, SkillDefOf.Crafting));
            if (worker) mask |= (byte)(1 << (int)RoleIconType.Worker);

            // 贸易：道德家 / 俊俏特质 / 社交双火 / 社交等级 ≥ 8
            bool trader =
                (AESettings.iconTraderMoralist && HasIdeoRole(pawn, "IdeoRole_Moralist"))
                || (AESettings.iconTraderBeauty && TraitDefCache.Beauty != null
                    && traits.DegreeOfTrait(TraitDefCache.Beauty) >= 1)
                || (AESettings.iconTraderSocialMajor && IsMajorPassion(pawn, SkillDefOf.Social))
                || (AESettings.iconTraderSocialLevel8
                    && GetSkillLevel(pawn, SkillDefOf.Social) >= SocialLevelThreshold);
            if (trader) mask |= (byte)(1 << (int)RoleIconType.Trader);

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

        // ===================== 心灵纽带标识判定 =====================
        // 心灵纽带（Biotech 心灵纽带基因产生）在伴侣双方各有一个 Hediff_PsychicBond，
        // 其 target 指向伴侣本人，因此一对伴侣的两个头像可算出同一个配色索引。
        // 不做 TTL 缓存：纽带建立/断裂需要即时反映，HediffSet 查询本身开销极低。

        private static HediffDef psychicBondDef;
        private static bool psychicBondDefResolved;

        /// <summary>
        /// 心灵纽带配对配色：同色 = 同一对伴侣，便于在殖民者栏一眼配对。
        /// 颜色取高饱和色相，深色背景下仍可区分；>8 对时按索引循环复用。
        /// </summary>
        private static readonly Color[] BondColors =
        {
            new Color(0.95f, 0.25f, 0.30f), // 红
            new Color(0.98f, 0.60f, 0.15f), // 橙
            new Color(0.95f, 0.85f, 0.20f), // 黄
            new Color(0.35f, 0.80f, 0.35f), // 绿
            new Color(0.25f, 0.75f, 0.80f), // 青
            new Color(0.35f, 0.55f, 0.95f), // 蓝
            new Color(0.70f, 0.45f, 0.95f), // 紫
            new Color(0.95f, 0.45f, 0.70f)  // 粉
        };

        /// <summary>
        /// 获取心灵纽带伴侣；无纽带、未启用 Biotech 或数据异常时返回 null。
        /// </summary>
        public static Pawn GetBondPartner(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null) return null;
            if (!ModsConfig.BiotechActive) return null;
            HediffDef def = GetPsychicBondDef();
            if (def == null) return null;
            return (pawn.health.hediffSet.GetFirstHediffOfDef(def) as Hediff_PsychicBond)?.target as Pawn;
        }

        /// <summary>
        /// 获取一对伴侣的配对颜色：以双方 thingIDNumber 的无序组合做键，
        /// 双方算出同一个索引，因此同色爱心即为同一对伴侣。
        /// </summary>
        public static Color GetBondColor(Pawn pawn, Pawn partner)
        {
            if (partner == null)
                return BondColors[0];

            int a = Mathf.Min(pawn.thingIDNumber, partner.thingIDNumber);
            int b = Mathf.Max(pawn.thingIDNumber, partner.thingIDNumber);
            uint key = (uint)a * 397u ^ (uint)b;
            return BondColors[(int)(key % (uint)BondColors.Length)];
        }

        /// <summary>
        /// 延迟解析 DLC HediffDef：Biotech 未启用时 DefDatabase 中不存在该 Def，
        /// 不能依赖原生 DefOf 字段（可能为 null），用 GetNamed 带 false 静默失败。
        /// </summary>
        private static HediffDef GetPsychicBondDef()
        {
            if (!psychicBondDefResolved)
            {
                psychicBondDefResolved = true;
                psychicBondDef = DefDatabase<HediffDef>.GetNamed("PsychicBond", false);
            }
            return psychicBondDef;
        }

        /// <summary>
        /// 判断 Pawn 是否持有指定 defName 的意识形态职位/专家。
        /// 未启用 Ideology 或无 Ideoligion 时返回 false（pawn.Ideo 为 null）。
        /// </summary>
        private static bool HasIdeoRole(Pawn pawn, string defName)
        {
            Precept_Role role = pawn.Ideo?.GetRole(pawn);
            return role?.def != null && role.def.defName == defName;
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
