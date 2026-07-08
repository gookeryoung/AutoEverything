using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using AutoEverything.Core;

namespace AutoEverything.RoleEvaluation
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

    /// <summary>
    /// 护甲偏好（用于全局重配的护甲分配）。
    /// 设计意图：不同角色对护甲的需求不同——前排战士需要重甲承担伤害，
    /// 后排可选重甲（有盈余时），工人/猎人需要轻甲保持工作效率。
    /// </summary>
    public enum ArmorPreference : byte
    {
        Heavy,          // 前排：强制选重甲，仅在没有重甲时才退而求其次
        Flexible,       // 后排：按原评分自由选择
        Light           // 工人/猎人等：强制选轻甲以保持工作效率
    }

    public static class RoleDetector
    {
        // 记录每个 Pawn 上一次检测到的角色，仅在变化时输出日志以减少噪音
        private static readonly Dictionary<int, Role> lastLoggedRole = new Dictionary<int, Role>();

        // 字典清理周期：60000 tick（约 1 游戏小时）扫描一次，移除已死亡/消失的 Pawn 条目
        // 避免字典无限增长导致内存泄漏与 thingIDNumber 复用导致的误判
        private const int CleanupInterval = 60000;
        private static int nextCleanupTick = 60000;

        /// <summary>
        /// 清理已死亡/离开地图的 Pawn 在字典中的残留条目。
        /// 由 CompGearManager 的 Tick 路径定期调用。
        /// </summary>
        public static void CleanupDeadPawns()
        {
            int tick = Find.TickManager.TicksGame;
            if (tick < nextCleanupTick) return;
            nextCleanupTick = tick + CleanupInterval;

            PawnStateCleaner.Cleanup(lastLoggedRole);
        }

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

                    // 近战有火且射击无火 → Brawler（无论技能等级）
                    if (meleePassion != Passion.None && shootingPassion == Passion.None)
                    {
                        result = Role.Brawler;
                        reason = $"近战{(meleePassion == Passion.Major ? "双火" : "单火")}+射击无火 (兴趣优先)";
                    }
                    // 射击有火且近战无火 → Shooter
                    else if (shootingPassion != Passion.None && meleePassion == Passion.None)
                    {
                        result = Role.Shooter;
                        reason = $"射击{(shootingPassion == Passion.Major ? "双火" : "单火")}+近战无火 (兴趣优先)";
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

            // 角色变化是调试信息：战斗中频繁切换会刷屏，改用 AEDebug.Log 受 debugLogging 开关控制
            int pawnId = pawn.thingIDNumber;
            if (lastLoggedRole.TryGetValue(pawnId, out Role prev))
            {
                if (prev != result)
                {
                    // 加 if 守卫避免闭包分配（CompTick 路径）
                    if (AEDebug.IsActive) AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(pawn)} 角色变化: {prev} -> {result} ({reason})");
                    lastLoggedRole[pawnId] = result;
                }
            }
            else
            {
                // 首次见到该 Pawn：加 if 守卫避免游戏加载时闭包分配
                if (AEDebug.IsActive) AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(pawn)} 初始角色: {result} ({reason})");
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

        /// <summary>
        /// 获取角色对应的护甲偏好（用于全局重配护甲分配）。
        /// - Brawler：前排战士，优先重甲承担伤害
        /// - Shooter/Hunter：后排，按原评分自由选择（有重甲盈余时使用）
        /// - Worker/Doctor/Pacifist/Leader/Default：工人/医疗/领袖，轻甲提高工作效率
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
    }
}