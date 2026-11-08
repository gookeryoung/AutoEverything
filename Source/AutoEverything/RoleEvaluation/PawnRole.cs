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
    /// 本枚举只用于"按角色分组"这一视觉功能，判定完全由玩家勾选的白名单条件决定。
    /// </summary>
    public enum RoleGroup : byte
    {
        Front = 0,   // 前排：食尸鬼 / 格斗者特质 / 格斗专家职位（按勾选）
        Shooter = 1, // 射手：射击专家职位 / 乱开枪（按勾选）
        Worker = 2,  // 工人：无法施暴 / 生产专家 / 工作狂 / 神经质 / 生产双火（按勾选）
        Trade = 3,   // 贸易：道德家职位 / 俊俏特质 / 社交双火（按勾选）
        Other = 4    // 其他：未命中任何已勾选条件者
    }

    /// <summary>
    /// 角色分组判定输入（纯数据，便于单元测试）。
    /// 只承载白名单条件的命中状态，不含技能/兴趣数值。
    /// </summary>
    public struct RoleGroupInput
    {
        public bool HasBrawlerTrait;
        public bool ViolentDisabled;

        // 强制判定条件（在【自动】→ 人员自动面板逐角色勾选）
        public bool IsGhoul;                    // 食尸鬼 → 前排
        public bool IsMeleeSpecialist;          // 格斗专家职位 → 前排
        public bool IsShootingSpecialist;       // 射击专家职位 → 射手
        public bool IsProductionSpecialist;     // 生产专家职位 → 工人
        public bool IsSocialSpecialist;         // 道德家职位 → 贸易
        public bool IsTriggerHappy;             // 乱开枪特质 → 射手
        public bool HasIndustrious;             // 工作狂特质 → 工人
        public bool HasNeurotic;                // 神经质特质 → 工人
        public bool CraftingPassionMajor;       // 生产（手工）兴趣双火 → 工人
        public bool HasBeauty;                  // 俊俏/沉鱼落雁特质 → 贸易
        public bool SocialPassionMajor;         // 社交兴趣双火 → 贸易
    }

    /// <summary>
    /// 角色分组白名单规则（纯数据，不依赖 ModSettings）。
    /// 生产路径由 FromSettings 读取【自动】面板；测试路径使用 Default。
    /// 每个字段对应面板上一个勾选项：勾选且命中才归组，未勾选则完全不参与判定。
    /// </summary>
    public struct RoleGroupRules
    {
        public bool BrawlerTraitToFront;

        // 白名单条件：勾选后符合条件者归入对应角色
        public bool GhoulToFront;
        public bool MeleeSpecialistToFront;
        public bool ShootingSpecialistToShooter;
        public bool PacifistToWorker;
        public bool ProductionSpecialistToWorker;
        public bool SocialSpecialistToTrade;
        public bool TriggerHappyToShooter;
        public bool IndustriousToWorker;
        public bool NeuroticToWorker;
        public bool CraftingMajorToWorker;
        public bool BeautyToTrade;
        public bool SocialMajorToTrade;

        public static RoleGroupRules Default
        {
            get
            {
                RoleGroupRules rules = new RoleGroupRules();
                rules.BrawlerTraitToFront = true;
                rules.GhoulToFront = true;
                rules.MeleeSpecialistToFront = true;
                rules.ShootingSpecialistToShooter = true;
                rules.PacifistToWorker = true;
                rules.ProductionSpecialistToWorker = true;
                rules.SocialSpecialistToTrade = true;
                rules.TriggerHappyToShooter = true;
                rules.IndustriousToWorker = true;
                rules.NeuroticToWorker = true;
                rules.CraftingMajorToWorker = true;
                rules.BeautyToTrade = true;
                rules.SocialMajorToTrade = true;
                return rules;
            }
        }

        /// <summary>
        /// 比较两份规则是否不同（任一字段变化即视为需要重新分组）。
        /// 纯函数，便于 UI 在面板绘制前后做快照对比，也便于单元测试。
        /// </summary>
        public bool DiffersFrom(RoleGroupRules other)
        {
            return BrawlerTraitToFront != other.BrawlerTraitToFront
                || GhoulToFront != other.GhoulToFront
                || MeleeSpecialistToFront != other.MeleeSpecialistToFront
                || ShootingSpecialistToShooter != other.ShootingSpecialistToShooter
                || PacifistToWorker != other.PacifistToWorker
                || ProductionSpecialistToWorker != other.ProductionSpecialistToWorker
                || SocialSpecialistToTrade != other.SocialSpecialistToTrade
                || TriggerHappyToShooter != other.TriggerHappyToShooter
                || IndustriousToWorker != other.IndustriousToWorker
                || NeuroticToWorker != other.NeuroticToWorker
                || CraftingMajorToWorker != other.CraftingMajorToWorker
                || BeautyToTrade != other.BeautyToTrade
                || SocialMajorToTrade != other.SocialMajorToTrade;
        }

        /// <summary>乱开枪：ShootingAccuracy degree = -1；degree 0/1 为冷枪手，不命中。</summary>
        public static bool IsTriggerHappyDegree(int degree)
        {
            return degree == -1;
        }

        /// <summary>工作狂：Industriousness degree ≥ 1（hard worker / industrious），懒惰为负值。</summary>
        public static bool IsIndustriousDegree(int degree)
        {
            return degree >= 1;
        }

        /// <summary>俊俏/沉鱼落雁：Beauty degree ≥ 1（pretty / beautiful），丑陋为负值。</summary>
        public static bool IsBeautyDegree(int degree)
        {
            return degree >= 1;
        }

        /// <summary>
        /// 神经质：Neurotic degree ≥ 1（神经质 / 严重神经质）。
        /// 注意 DegreeOfTrait 在"没有该特质"时返回 0，而原生 Neurotic 的 degree 从 1 起，
        /// 因此阈值必须是 1，用 ≥0 会把所有殖民者都误判为神经质。
        /// </summary>
        public static bool IsNeuroticDegree(int degree)
        {
            return degree >= 1;
        }

        /// <summary>
        /// 参与角色分组的资格判断（纯函数，便于单元测试）。
        /// 只要求是殖民者栏收录的玩家侧人形（含远行队外出、精神崩溃、奴隶），
        /// 不能用 Pawn.IsColonistPlayerControlled：它要求 Spawned 且无精神崩溃状态，
        /// 会把远行队与精神崩溃中的殖民者误判成"其他"。
        /// </summary>
        public static bool IsRoleGroupCandidate(bool isBarColonist, bool hasSkills, bool hasStory)
        {
            return isBarColonist && hasSkills && hasStory;
        }

        /// <summary>兴趣是否达到双火：Major 及以上（VSE Natural 归入 Major，Critical 亦命中）。</summary>
        public static bool IsMajorPassionTier(PassionHelper.PassionTier tier)
        {
            return tier >= PassionHelper.PassionTier.Major;
        }

        public static RoleGroupRules FromSettings()
        {
            RoleGroupRules rules = new RoleGroupRules();
            rules.BrawlerTraitToFront = AESettings.groupBrawlerTraitFront;
            rules.GhoulToFront = AESettings.groupFrontGhoul;
            rules.MeleeSpecialistToFront = AESettings.groupFrontMeleeSpecialist;
            rules.ShootingSpecialistToShooter = AESettings.groupShooterSpecialist;
            rules.PacifistToWorker = AESettings.groupWorkerPacifist;
            rules.ProductionSpecialistToWorker = AESettings.groupWorkerProductionSpecialist;
            rules.SocialSpecialistToTrade = AESettings.groupTradeMoralist;
            rules.TriggerHappyToShooter = AESettings.groupTriggerHappyShooter;
            rules.IndustriousToWorker = AESettings.groupIndustriousWorker;
            rules.NeuroticToWorker = AESettings.groupNeuroticWorker;
            rules.CraftingMajorToWorker = AESettings.groupCraftingPassionWorker;
            rules.BeautyToTrade = AESettings.groupBeautyTrade;
            rules.SocialMajorToTrade = AESettings.groupSocialPassionTrade;
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
        /// 仅用于殖民者栏按角色分组：完全由【自动】→ 人员自动面板的白名单勾选项决定，
        /// 不做任何技能/兴趣推断，未命中任何已勾选条件即归入"其他"。
        /// </summary>
        public static RoleGroup DetectRoleGroup(Pawn pawn)
        {
            if (pawn == null) return RoleGroup.Other;

            RoleGroupRules rules = RoleGroupRules.FromSettings();

            // 食尸鬼不是殖民者，勾选"食尸鬼 → 前排"时允许其进入分组判定
            if (rules.GhoulToFront && DLCCompat.IsGhoul(pawn)) return RoleGroup.Front;

            // 远行队（未 Spawned）、精神崩溃中的殖民者仍应分组，故不能用 IsColonistPlayerControlled；
            // 与原版殖民者栏收录范围保持一致：殖民者 + 玩家侧改造人（食尸鬼等）
            bool isBarColonist = pawn.IsColonist || pawn.IsColonySubhumanPlayerControlled;
            if (!RoleGroupRules.IsRoleGroupCandidate(isBarColonist, pawn.skills != null, pawn.story != null))
            {
                return RoleGroup.Other;
            }

            RoleGroupInput input = new RoleGroupInput();
            input.HasBrawlerTrait = pawn.story.traits?.HasTrait(TraitDefOf.Brawler) == true;

            // 意识形态职位：专家/道德家按职位强制归组（开关由玩家控制）
            input.IsMeleeSpecialist = HasIdeoRole(pawn, "IdeoRole_MeleeSpecialist");
            input.IsShootingSpecialist = HasIdeoRole(pawn, "IdeoRole_ShootingSpecialist");
            input.IsProductionSpecialist = HasIdeoRole(pawn, "IdeoRole_ProductionSpecialist");
            input.IsSocialSpecialist = HasIdeoRole(pawn, "IdeoRole_Moralist");

            input.ViolentDisabled = pawn.WorkTagIsDisabled(WorkTags.Violent);

            // 特质类强制条件：Def 可能因 DLC/模组缺失为 null，查询前逐一判空
            TraitSet traits = pawn.story.traits;
            input.IsTriggerHappy = traits != null && TraitDefCache.ShootingAccuracy != null
                && RoleGroupRules.IsTriggerHappyDegree(traits.DegreeOfTrait(TraitDefCache.ShootingAccuracy));
            input.HasIndustrious = traits != null && TraitDefCache.Industriousness != null
                && RoleGroupRules.IsIndustriousDegree(traits.DegreeOfTrait(TraitDefCache.Industriousness));
            input.HasNeurotic = traits != null && TraitDefCache.Neurotic != null
                && RoleGroupRules.IsNeuroticDegree(traits.DegreeOfTrait(TraitDefCache.Neurotic));
            input.HasBeauty = traits != null && TraitDefCache.Beauty != null
                && RoleGroupRules.IsBeautyDegree(traits.DegreeOfTrait(TraitDefCache.Beauty));

            // 兴趣双火：生产（手工）与社交，VSE 兼容按 Major 及以上处理
            input.CraftingPassionMajor = IsMajorPassion(pawn, SkillDefOf.Crafting);
            input.SocialPassionMajor = IsMajorPassion(pawn, SkillDefOf.Social);

            return EvaluateRoleGroupCore(input, rules);
        }

        /// <summary>
        /// 判断技能兴趣是否达到双火（Major）及以上，兼容 VSE 的 Natural/Critical。
        /// </summary>
        private static bool IsMajorPassion(Pawn pawn, SkillDef skillDef)
        {
            SkillRecord skill = pawn.skills?.GetSkill(skillDef);
            if (skill == null) return false;
            return RoleGroupRules.IsMajorPassionTier(PassionHelper.GetPassionTier(skill.passion));
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
        /// 纯白名单制：每个条件对应面板上一个勾选项，勾选且命中才归入该角色；
        /// 不做任何技能/兴趣兜底推断，未命中任何已勾选项即归入"其他"。
        /// 命中多个条件时按下列顺序取第一个：
        /// 1. 食尸鬼 → 前排
        /// 2. 格斗者特质 → 前排
        /// 3. 格斗专家职位 → 前排
        /// 4. 射击专家职位 → 射手
        /// 5. 乱开枪特质 → 射手
        /// 6. 生产专家职位 → 工人
        /// 7. 工作狂特质 → 工人
        /// 8. 神经质特质 → 工人
        /// 9. 生产（手工）兴趣双火 → 工人
        /// 10. 道德家职位 → 贸易
        /// 11. 俊俏/沉鱼落雁特质 → 贸易
        /// 12. 社交兴趣双火 → 贸易
        /// 13. 无法施暴 → 工人（置于贸易之后：信仰禁暴/无法战斗者不应抢走贸易候选）
        /// 14. 其余 → 其他
        /// </summary>
        public static RoleGroup EvaluateRoleGroupCore(RoleGroupInput input, RoleGroupRules rules)
        {
            if (rules.GhoulToFront && input.IsGhoul) return RoleGroup.Front;
            if (rules.BrawlerTraitToFront && input.HasBrawlerTrait) return RoleGroup.Front;
            if (rules.MeleeSpecialistToFront && input.IsMeleeSpecialist) return RoleGroup.Front;
            if (rules.ShootingSpecialistToShooter && input.IsShootingSpecialist) return RoleGroup.Shooter;
            if (rules.TriggerHappyToShooter && input.IsTriggerHappy) return RoleGroup.Shooter;
            if (rules.ProductionSpecialistToWorker && input.IsProductionSpecialist) return RoleGroup.Worker;
            if (rules.IndustriousToWorker && input.HasIndustrious) return RoleGroup.Worker;
            if (rules.NeuroticToWorker && input.HasNeurotic) return RoleGroup.Worker;
            if (rules.CraftingMajorToWorker && input.CraftingPassionMajor) return RoleGroup.Worker;
            if (rules.SocialSpecialistToTrade && input.IsSocialSpecialist) return RoleGroup.Trade;
            if (rules.BeautyToTrade && input.HasBeauty) return RoleGroup.Trade;
            if (rules.SocialMajorToTrade && input.SocialPassionMajor) return RoleGroup.Trade;
            if (rules.PacifistToWorker && input.ViolentDisabled) return RoleGroup.Worker;

            // 白名单外的人员一律归入"其他"，不再按技能/兴趣推断
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
        /// 校验顺序数组必须是 0..4 的完整排列（无缺失、无重复、无越界），
        /// 且"其他"必须固定在最后一位（未命中白名单者的兜底分组不参与拖拽换序）。
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
            return order[order.Count - 1] == (int)RoleGroup.Other;
        }

        /// <summary>
        /// 在顺序数组中把 from 移到 to 的位置（插入到 to 之前）。
        /// "其他"固定最后：不允许拖动"其他"本身，拖到"其他"上也只是插入其前，
        /// 保证兜底分组永远排在末尾。
        /// 返回是否实际发生变化；由 AESettings.MoveRoleGroup 负责写盘与重排。
        /// </summary>
        public static bool MoveRoleGroupInOrder(List<int> order, int from, int to)
        {
            if (order == null) return false;
            if (from == (int)RoleGroup.Other) return false;

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
        /// 交换顺序数组中相邻两个位置（供设置页"上移/下移"按钮使用）。
        /// "其他"固定最后：涉及"其他"的交换一律拒绝，保证兜底分组永远排在末尾。
        /// 返回是否实际发生变化；由 AESettings.SwapRoleGroupOrder 负责写盘与重排。
        /// </summary>
        public static bool SwapRoleGroupInOrder(List<int> order, int indexA, int indexB)
        {
            if (order == null) return false;
            if (indexA < 0 || indexB < 0 || indexA >= order.Count || indexB >= order.Count) return false;
            if (indexA == indexB) return false;
            if (order[indexA] == (int)RoleGroup.Other || order[indexB] == (int)RoleGroup.Other) return false;

            int tmp = order[indexA];
            order[indexA] = order[indexB];
            order[indexB] = tmp;
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
