using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using AutoEverything.Core;

namespace AutoEverything.RoleEvaluation
{
    /// <summary>
    /// 基于 Pawn 技能与特质自动判定的角色枚举。
    /// 角色用于 ITab 角色徽章显示与狩猎分配判定。
    /// </summary>
    public enum Role
    {
        Default,
        Shooter,    // 高射击技能
        Brawler,    // 高近战技能或格斗者特质
        Doctor,     // 高医疗技能
        Hunter,     // 被指派狩猎
        Worker,     // 通用工人
        Pacifist,   // 无法进行暴力工作
        Leader      // 拥有意识形态角色（仅作标签，不直接驱动评分）
    }

    /// <summary>
    /// 护甲偏好（用于 ITab 护甲偏好徽章显示与狩猎后排判定）。
    /// 设计意图：不同角色对护甲的需求不同——前排战士需要重甲承担伤害，
    /// 后排可选重甲（有盈余时），工人/猎人需要轻甲保持工作效率。
    /// </summary>
    public enum ArmorPreference : byte
    {
        Heavy,          // 前排：强制选重甲，仅在没有重甲时才退而求其次
        Flexible,       // 后排：按原评分自由选择
        Light           // 工人/猎人等：强制选轻甲以保持工作效率
    }

    /// <summary>
    /// 殖民者栏分组用的五类角色（顺序即分组顺序）：
    /// 前排 → 射手 → 工人 → 贸易 → 其他。
    /// 与 <see cref="Role"/> 的区别：Role 面向徽章/护甲/狩猎等细分类，
    /// 本枚举只用于"按角色分组"这一视觉功能，判定阈值可由【自动】面板调整。
    /// </summary>
    public enum RoleGroup : byte
    {
        Front = 0,   // 前排：近战为主（格斗者特质/近战阈值/近战有火）
        Shooter = 1, // 射手：远程为主（射击阈值/射击有火/被指派狩猎）
        Worker = 2,  // 工人：战斗技能偏低或无法施暴
        Trade = 3,   // 贸易：社交技能突出
        Other = 4    // 其他：不满足以上规则者
    }

    /// <summary>
    /// 角色分组判定输入（纯数据，便于单元测试）。
    /// </summary>
    public struct RoleGroupInput
    {
        public int Shooting;
        public int Melee;
        public int Social;
        public bool HasBrawlerTrait;
        public bool MeleePassion;
        public bool ShootingPassion;
        public bool IsHunter;
        public bool ViolentDisabled;

        // 强制判定条件（在【自动】→ 人员自动面板逐角色勾选）
        public bool IsGhoul;                    // 食尸鬼 → 前排
        public bool IsMeleeSpecialist;          // 格斗专家职位 → 前排
        public bool IsShootingSpecialist;       // 射击专家职位 → 射手
        public bool IsProductionSpecialist;     // 生产专家职位 → 工人
        public bool IsSocialSpecialist;         // 道德家职位 → 贸易
    }

    /// <summary>
    /// 角色分组判定阈值（纯数据，不依赖 ModSettings）。
    /// 生产路径由 FromSettings 读取【自动】面板；测试路径使用 Default。
    /// </summary>
    public struct RoleGroupRules
    {
        public int FrontSkillMin;
        public int ShooterSkillMin;
        public int TradeSkillMin;
        public int WorkerMaxCombatSkill;
        public bool PassionPriority;
        public bool BrawlerTraitToFront;
        public bool HunterAsShooter;

        // 强制判定开关：勾选后符合条件者直接归入对应角色，优先于技能/兴趣判定
        public bool GhoulToFront;
        public bool MeleeSpecialistToFront;
        public bool ShootingSpecialistToShooter;
        public bool PacifistToWorker;
        public bool ProductionSpecialistToWorker;
        public bool SocialSpecialistToTrade;

        public static RoleGroupRules Default
        {
            get
            {
                RoleGroupRules rules = new RoleGroupRules();
                rules.FrontSkillMin = 8;
                rules.ShooterSkillMin = 8;
                rules.TradeSkillMin = 8;
                rules.WorkerMaxCombatSkill = 5;
                rules.PassionPriority = true;
                rules.BrawlerTraitToFront = true;
                rules.HunterAsShooter = true;
                rules.GhoulToFront = true;
                rules.MeleeSpecialistToFront = true;
                rules.ShootingSpecialistToShooter = true;
                rules.PacifistToWorker = true;
                rules.ProductionSpecialistToWorker = true;
                rules.SocialSpecialistToTrade = true;
                return rules;
            }
        }

        /// <summary>
        /// 比较两份规则是否不同（任一字段变化即视为需要重新分组）。
        /// 纯函数，便于 UI 在面板绘制前后做快照对比，也便于单元测试。
        /// </summary>
        public bool DiffersFrom(RoleGroupRules other)
        {
            return FrontSkillMin != other.FrontSkillMin
                || ShooterSkillMin != other.ShooterSkillMin
                || TradeSkillMin != other.TradeSkillMin
                || WorkerMaxCombatSkill != other.WorkerMaxCombatSkill
                || PassionPriority != other.PassionPriority
                || BrawlerTraitToFront != other.BrawlerTraitToFront
                || HunterAsShooter != other.HunterAsShooter
                || GhoulToFront != other.GhoulToFront
                || MeleeSpecialistToFront != other.MeleeSpecialistToFront
                || ShootingSpecialistToShooter != other.ShootingSpecialistToShooter
                || PacifistToWorker != other.PacifistToWorker
                || ProductionSpecialistToWorker != other.ProductionSpecialistToWorker
                || SocialSpecialistToTrade != other.SocialSpecialistToTrade;
        }

        public static RoleGroupRules FromSettings()
        {
            RoleGroupRules rules = new RoleGroupRules();
            rules.FrontSkillMin = AESettings.groupFrontSkillMin;
            rules.ShooterSkillMin = AESettings.groupShooterSkillMin;
            rules.TradeSkillMin = AESettings.groupTradeSkillMin;
            rules.WorkerMaxCombatSkill = AESettings.groupWorkerMaxCombatSkill;
            rules.PassionPriority = AESettings.groupPassionPriority;
            rules.BrawlerTraitToFront = AESettings.groupBrawlerTraitFront;
            rules.HunterAsShooter = AESettings.groupHunterAsShooter;
            rules.GhoulToFront = AESettings.groupFrontGhoul;
            rules.MeleeSpecialistToFront = AESettings.groupFrontMeleeSpecialist;
            rules.ShootingSpecialistToShooter = AESettings.groupShooterSpecialist;
            rules.PacifistToWorker = AESettings.groupWorkerPacifist;
            rules.ProductionSpecialistToWorker = AESettings.groupWorkerProductionSpecialist;
            rules.SocialSpecialistToTrade = AESettings.groupTradeMoralist;
            return rules;
        }
    }

    public static class RoleDetector
    {
        /// <summary>
        /// 基于技能、特质与工作指派检测 Pawn 最合适的角色。
        /// </summary>
        public static Role DetectRole(Pawn pawn)
        {
            if (pawn?.skills == null || pawn?.story == null) return Role.Default;
            if (!pawn.IsColonistPlayerControlled) return Role.Default;

            Role result;
            string reason;

            // 和平主义者：无法进行暴力工作
            if (pawn.WorkTagIsDisabled(WorkTags.Violent))
            {
                result = Role.Pacifist;
                reason = "无法暴力";
            }
            else
            {
                // 意识形态角色（如祭司、领袖）：仅作标签，不直接驱动评分
                // 优化：原代码在条件判断与分支体中两次调用 Ideo.GetRole(pawn)，合并为单次查询
                var ideoRole = pawn.Ideo?.GetRole(pawn);
                if (ideoRole != null)
                {
                    // 意识形态角色按其原有技能倾向二次分类
                    int shooting = pawn.skills.GetSkill(SkillDefOf.Shooting)?.Level ?? 0;
                    int melee = pawn.skills.GetSkill(SkillDefOf.Melee)?.Level ?? 0;
                    int medicine = pawn.skills.GetSkill(SkillDefOf.Medicine)?.Level ?? 0;

                    if (medicine >= 8 && medicine >= shooting && medicine >= melee)
                    {
                        result = Role.Doctor;
                        reason = $"意识形态角色({ideoRole.def.label})+医疗={medicine}";
                    }
                    else if (shooting >= 8 && shooting > melee)
                    {
                        result = Role.Shooter;
                        reason = $"意识形态角色({ideoRole.def.label})+射击={shooting}";
                    }
                    else if (melee >= 8 && melee > shooting)
                    {
                        result = Role.Brawler;
                        reason = $"意识形态角色({ideoRole.def.label})+近战={melee}";
                    }
                    else
                    {
                        result = Role.Leader;
                        reason = $"意识形态角色={ideoRole.def.label}";
                    }
                }
                // 格斗者特质：直接判定为格斗者角色
                else if (pawn.story.traits?.HasTrait(TraitDefOf.Brawler) == true)
                {
                    result = Role.Brawler;
                    reason = "格斗者特质";
                }
                // 猎人：狩猎工作优先级为 1
                else if (pawn.workSettings != null && pawn.workSettings.EverWork
                    && pawn.workSettings.GetPriority(WorkTypeDefOf.Hunting) == 1)
                {
                    result = Role.Hunter;
                    reason = "狩猎优先级 1";
                }
                else
                {
                    int shooting = pawn.skills.GetSkill(SkillDefOf.Shooting)?.Level ?? 0;
                    int melee = pawn.skills.GetSkill(SkillDefOf.Melee)?.Level ?? 0;
                    int medicine = pawn.skills.GetSkill(SkillDefOf.Medicine)?.Level ?? 0;

                    // 兴趣组合优先判定（天赋倾向比当前技能等级更能反映长期定位）
                    Passion shootingPassion = pawn.skills.GetSkill(SkillDefOf.Shooting)?.passion ?? Passion.None;
                    Passion meleePassion = pawn.skills.GetSkill(SkillDefOf.Melee)?.passion ?? Passion.None;

                    // VSE 兼容：用 PassionTier 判定，Apathy 视为无火及以下，Natural/Critical 按 Major 处理
                    PassionHelper.PassionTier shootingTier = PassionHelper.GetPassionTier(shootingPassion);
                    PassionHelper.PassionTier meleeTier = PassionHelper.GetPassionTier(meleePassion);

                    // 近战有火且射击无火 → Brawler（无论技能等级）
                    if (meleeTier > PassionHelper.PassionTier.None && shootingTier == PassionHelper.PassionTier.None)
                    {
                        result = Role.Brawler;
                        reason = $"近战{PassionHelper.GetPassionName(meleePassion)}+射击无火 (兴趣优先)";
                    }
                    // 射击有火且近战无火 → Shooter
                    else if (shootingTier > PassionHelper.PassionTier.None && meleeTier == PassionHelper.PassionTier.None)
                    {
                        result = Role.Shooter;
                        reason = $"射击{PassionHelper.GetPassionName(shootingPassion)}+近战无火 (兴趣优先)";
                    }
                    // 医生：医疗是其最佳战斗相关技能且 >= 8
                    else if (medicine >= 8 && medicine >= shooting && medicine >= melee)
                    {
                        result = Role.Doctor;
                        reason = $"医疗={medicine} >= 射击={shooting}, 近战={melee}";
                    }
                    // 射手 vs 格斗者：谁的技能更高
                    else if (shooting >= 8 && shooting > melee)
                    {
                        result = Role.Shooter;
                        reason = $"射击={shooting} > 近战={melee}";
                    }
                    else if (melee >= 8 && melee > shooting)
                    {
                        result = Role.Brawler;
                        reason = $"近战={melee} > 射击={shooting}";
                    }
                    // 两项战斗技能都低：判断是否主要为工人
                    else if (shooting < 5 && melee < 5)
                    {
                        result = Role.Worker;
                        reason = $"战斗技能偏低 (射击={shooting}, 近战={melee})";
                    }
                    // 中等战斗技能：默认为射手（远程通常更安全）
                    else if (shooting >= melee)
                    {
                        result = Role.Shooter;
                        reason = $"射击={shooting} >= 近战={melee} (中等)";
                    }
                    else
                    {
                        result = Role.Brawler;
                        reason = $"近战={melee} > 射击={shooting} (中等)";
                    }
                }
            }

            // 角色检测完成：debug 模式下输出（接受重复日志，简化代码，去重字典已移除）
            if (AEDebug.IsActive) AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(pawn)} 角色: {result} ({reason})");

            return result;
        }

        /// <summary>
        /// 检测 Pawn 所属的五类分组（前排/射手/工人/贸易/其他）。
        /// 仅用于殖民者栏按角色分组，判定阈值来自【自动】面板。
        /// </summary>
        public static RoleGroup DetectRoleGroup(Pawn pawn)
        {
            if (pawn == null) return RoleGroup.Other;

            RoleGroupRules rules = RoleGroupRules.FromSettings();

            // 食尸鬼不是殖民者，勾选"食尸鬼 → 前排"时允许其进入分组判定
            if (rules.GhoulToFront && DLCCompat.IsGhoul(pawn)) return RoleGroup.Front;

            if (pawn.skills == null || pawn.story == null) return RoleGroup.Other;
            if (!pawn.IsColonistPlayerControlled) return RoleGroup.Other;

            RoleGroupInput input = new RoleGroupInput();
            input.Shooting = pawn.skills.GetSkill(SkillDefOf.Shooting)?.Level ?? 0;
            input.Melee = pawn.skills.GetSkill(SkillDefOf.Melee)?.Level ?? 0;
            input.Social = pawn.skills.GetSkill(SkillDefOf.Social)?.Level ?? 0;
            input.HasBrawlerTrait = pawn.story.traits?.HasTrait(TraitDefOf.Brawler) == true;

            // 意识形态职位：专家/道德家按职位强制归组（开关由玩家控制）
            input.IsMeleeSpecialist = HasIdeoRole(pawn, "IdeoRole_MeleeSpecialist");
            input.IsShootingSpecialist = HasIdeoRole(pawn, "IdeoRole_ShootingSpecialist");
            input.IsProductionSpecialist = HasIdeoRole(pawn, "IdeoRole_ProductionSpecialist");
            input.IsSocialSpecialist = HasIdeoRole(pawn, "IdeoRole_Moralist");

            // 兴趣优先：用 PassionHelper 兼容 VSE（Apathy 视为无火及以下）
            Passion shootingPassion = pawn.skills.GetSkill(SkillDefOf.Shooting)?.passion ?? Passion.None;
            Passion meleePassion = pawn.skills.GetSkill(SkillDefOf.Melee)?.passion ?? Passion.None;
            input.ShootingPassion = PassionHelper.GetPassionTier(shootingPassion) > PassionHelper.PassionTier.None;
            input.MeleePassion = PassionHelper.GetPassionTier(meleePassion) > PassionHelper.PassionTier.None;

            input.IsHunter = pawn.workSettings != null && pawn.workSettings.EverWork
                && pawn.workSettings.GetPriority(WorkTypeDefOf.Hunting) == 1;
            input.ViolentDisabled = pawn.WorkTagIsDisabled(WorkTags.Violent);

            return EvaluateRoleGroupCore(input, rules);
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

        /// <summary>
        /// 分组判定纯逻辑核心（不依赖 Pawn/ModSettings，便于单元测试）。
        /// 规则按优先级自上而下，命中即返回：
        ///
        /// 强制判定（面板逐角色勾选，优先于技能/兴趣）：
        /// 0. 无法施暴 → 工人（可关闭）
        /// 1. 食尸鬼 → 前排（可关闭）
        /// 2. 格斗者特质 → 前排（可关闭）
        /// 3. 格斗专家职位 → 前排（可关闭）
        /// 4. 射击专家职位 → 射手（可关闭）
        /// 5. 被指派狩猎 → 射手（可关闭）
        /// 6. 生产专家职位 → 工人（可关闭）
        /// 7. 道德家职位 → 贸易（可关闭）
        ///
        /// 技能/兴趣补充判定（强制条件未命中时）：
        /// 8. 兴趣优先：近战有火且射击无火 → 前排；反之 → 射手（可关闭）
        /// 9. 近战 ≥ 前排阈值且高于射击 → 前排
        /// 10. 射击 ≥ 射手阈值且高于近战 → 射手
        /// 11. 社交 ≥ 贸易阈值且不低于射击/近战 → 贸易
        /// 12. 射击与近战均低于工人阈值 → 工人
        /// 13. 其余 → 其他
        /// </summary>
        public static RoleGroup EvaluateRoleGroupCore(RoleGroupInput input, RoleGroupRules rules)
        {
            // ── 强制判定条件 ──
            if (rules.PacifistToWorker && input.ViolentDisabled) return RoleGroup.Worker;
            if (rules.GhoulToFront && input.IsGhoul) return RoleGroup.Front;
            if (rules.BrawlerTraitToFront && input.HasBrawlerTrait) return RoleGroup.Front;
            if (rules.MeleeSpecialistToFront && input.IsMeleeSpecialist) return RoleGroup.Front;
            if (rules.ShootingSpecialistToShooter && input.IsShootingSpecialist) return RoleGroup.Shooter;
            if (rules.HunterAsShooter && input.IsHunter) return RoleGroup.Shooter;
            if (rules.ProductionSpecialistToWorker && input.IsProductionSpecialist) return RoleGroup.Worker;
            if (rules.SocialSpecialistToTrade && input.IsSocialSpecialist) return RoleGroup.Trade;

            // ── 技能/兴趣补充判定 ──
            if (rules.PassionPriority)
            {
                if (input.MeleePassion && !input.ShootingPassion) return RoleGroup.Front;
                if (input.ShootingPassion && !input.MeleePassion) return RoleGroup.Shooter;
            }

            if (input.Melee >= rules.FrontSkillMin && input.Melee > input.Shooting) return RoleGroup.Front;
            if (input.Shooting >= rules.ShooterSkillMin && input.Shooting > input.Melee) return RoleGroup.Shooter;

            if (input.Social >= rules.TradeSkillMin
                && input.Social >= input.Shooting
                && input.Social >= input.Melee)
            {
                return RoleGroup.Trade;
            }

            if (input.Shooting < rules.WorkerMaxCombatSkill && input.Melee < rules.WorkerMaxCombatSkill)
            {
                return RoleGroup.Worker;
            }

            return RoleGroup.Other;
        }

        /// <summary>
        /// 分组排序值：分组枚举本身即顺序（前排 0 → 其他 4）。
        /// </summary>
        public static int GetRoleGroupOrder(RoleGroup group)
        {
            return (int)group;
        }

        /// <summary>
        /// 默认分组显示顺序：前排 → 射手 → 工人 → 贸易 → 其他。
        /// 纯数据函数，供 AESettings 与单元测试复用。
        /// </summary>
        public static List<int> DefaultRoleGroupOrder()
        {
            return new List<int>
            {
                (int)RoleGroup.Front,
                (int)RoleGroup.Shooter,
                (int)RoleGroup.Worker,
                (int)RoleGroup.Trade,
                (int)RoleGroup.Other
            };
        }

        /// <summary>
        /// 校验顺序数组必须是 0..4 的完整排列（无缺失、无重复、无越界）。
        /// 存档损坏或旧版本数据不合法时由调用方回退默认顺序。
        /// </summary>
        public static bool IsValidRoleGroupOrder(List<int> order)
        {
            if (order == null || order.Count != 5) return false;

            bool[] seen = new bool[5];
            for (int i = 0; i < order.Count; i++)
            {
                int value = order[i];
                if (value < 0 || value >= 5 || seen[value]) return false;
                seen[value] = true;
            }
            return true;
        }

        /// <summary>
        /// 在顺序数组中把 from 移到 to 的位置（插入到 to 之前）。
        /// 返回是否实际发生变化；由 AESettings.MoveRoleGroup 负责写盘与重排。
        /// </summary>
        public static bool MoveRoleGroupInOrder(List<int> order, int from, int to)
        {
            if (order == null) return false;

            int fromIdx = order.IndexOf(from);
            int toIdx = order.IndexOf(to);
            if (fromIdx < 0 || toIdx < 0 || fromIdx == toIdx) return false;

            order.RemoveAt(fromIdx);
            // 移除后重新定位目标，实现"插入到目标分组之前"
            int insertIdx = order.IndexOf(to);
            if (insertIdx < 0) insertIdx = order.Count;
            order.Insert(insertIdx, from);
            return true;
        }

        /// <summary>
        /// 按自定义顺序取分组序号：顺序数组中的下标即可视序号，缺失时回退枚举顺序。
        /// </summary>
        public static int GetRoleGroupOrderIn(List<int> order, int group)
        {
            int idx = order != null ? order.IndexOf(group) : -1;
            return idx >= 0 ? idx : group;
        }

        /// <summary>
        /// 获取角色对应的护甲偏好（用于护甲偏好徽章显示与狩猎后排判定）。
        /// - Brawler：前排战士，Heavy 重甲
        /// - Shooter/Hunter/Leader：后排，Flexible 自由
        /// - Worker/Doctor/Pacifist/Default：工人/医疗，Light 轻甲
        /// </summary>
        public static ArmorPreference GetArmorPreference(Role role)
        {
            switch (role)
            {
                case Role.Brawler:
                    return ArmorPreference.Heavy;
                case Role.Shooter:
                case Role.Hunter:
                case Role.Leader:
                    // 领袖改用 Flexible：原 Light 偏好会让领袖被强制分配轻甲，
                    // 与玩家期望（领袖穿重甲显眼）不符；Flexible 让领袖按评分自由选择
                    return ArmorPreference.Flexible;
                // Worker/Doctor/Pacifist/Default 均使用轻甲
                default:
                    return ArmorPreference.Light;
            }
        }

        /// <summary>
        /// 判定角色是否为后排（用于狩猎分配优先级）。
        /// 后排 = Flexible 护甲偏好：Shooter/Hunter/Leader。
        /// 设计意图：后排角色应优先承担狩猎以练习射击，前排近战角色不应被分配狩猎。
        /// 注意：本方法不依赖 Hunting 工作优先级（避免循环依赖），仅看护甲偏好。
        /// </summary>
        public static bool IsBackRow(Role role)
        {
            return GetArmorPreference(role) == ArmorPreference.Flexible;
        }

        /// <summary>
        /// 获取角色排序优先级：数字小的排前面。
        /// 顺序设计：前排战斗（Brawler/Shooter）→ 后勤（Doctor/Worker/Pacifist）→ 其他（Hunter/Leader）→ 默认。
        /// 用于殖民者栏按角色分组重排（AESettings.ReorderColonistBar）。
        /// 注：原实现位于 AESettings.GetRoleOrder（private），因 AESettings 继承 ModSettings，
        /// 测试项目无 Assembly-CSharp 引用无法直接调用，故迁移至此处便于单元测试。
        /// </summary>
        public static int GetRoleOrder(Role role)
        {
            switch (role)
            {
                case Role.Brawler: return 0;
                case Role.Shooter: return 1;
                case Role.Doctor: return 2;
                case Role.Worker: return 3;
                case Role.Pacifist: return 4;
                case Role.Hunter: return 5;
                case Role.Leader: return 6;
                default: return 99;
            }
        }
    }
}
