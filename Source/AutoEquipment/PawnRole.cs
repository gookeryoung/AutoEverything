using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace AutoEquipment
{
    /// <summary>
    /// 基于 Pawn 技能与特质自动判定的角色枚举。
    /// 角色决定其应当偏好何种装备。
    /// </summary>
    public enum Role
    {
        Default,
        Shooter,    // 高射击技能，偏好远程武器
        Brawler,    // 高近战技能或格斗者特质，偏好近战武器
        Doctor,     // 高医疗技能，携带药品、穿戴医疗装备
        Hunter,     // 被指派狩猎，需要狩猎武器
        Worker,     // 通用工人，偏好工作属性服装
        Pacifist,   // 无法进行暴力工作
        Leader      // 拥有意识形态角色（仅作标签，不直接驱动评分）
    }

    public static class RoleDetector
    {
        // 记录每个 Pawn 上一次检测到的角色，仅在变化时输出日志以减少噪音
        private static readonly Dictionary<int, Role> lastLoggedRole = new Dictionary<int, Role>();

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
            // 意识形态角色（如祭司、领袖）：仅作标签，不直接驱动评分
            // 由 GearPolicyEngine 调度时保留其原有装备偏好
            else if (pawn.Ideo != null && pawn.Ideo.GetRole(pawn) != null)
            {
                var ideoRole = pawn.Ideo.GetRole(pawn);
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

                // 医生：医疗是其最佳战斗相关技能且 >= 8
                if (medicine >= 8 && medicine >= shooting && medicine >= melee)
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

            // 仅在角色变化时记录日志，避免刷屏
            int pawnId = pawn.thingIDNumber;
            if (lastLoggedRole.TryGetValue(pawnId, out Role prev))
            {
                if (prev != result)
                {
                    Log.Message($"[AutoEquipment] {pawn.LabelShort} 角色变化: {prev} -> {result} ({reason})");
                    lastLoggedRole[pawnId] = result;
                }
            }
            else
            {
                Log.Message($"[AutoEquipment] {pawn.LabelShort} 初始角色: {result} ({reason})");
                lastLoggedRole[pawnId] = result;
            }

            return result;
        }

        /// <summary>
        /// 获取角色对应的主战斗属性。
        /// </summary>
        public static StatDef GetPrimaryWeaponStat(Role role)
        {
            switch (role)
            {
                case Role.Shooter:
                case Role.Hunter:
                    return StatDefOf.RangedWeapon_DamageMultiplier;
                case Role.Brawler:
                    return StatDefOf.MeleeWeapon_AverageDPS;
                default:
                    return StatDefOf.RangedWeapon_DamageMultiplier;
            }
        }

        /// <summary>
        /// 该角色是否应当偏好近战武器。
        /// 仅格斗者返回 true，用于在武器评分中阻止格斗者装备远程武器。
        /// </summary>
        public static bool PrefersMelee(Role role)
        {
            return role == Role.Brawler;
        }
    }
}
