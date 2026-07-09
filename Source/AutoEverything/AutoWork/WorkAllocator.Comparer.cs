using System.Collections.Generic;
using RimWorld;
using Verse;
using AutoEverything.Core;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.AutoWork
{
    public static partial class WorkAllocator
    {
        /// <summary>
        /// 狩猎专用比较器：在通用三因子排序前增加「后排优先」判定。
        /// 后排 = ArmorPreference.Flexible（Shooter/Hunter/Leader）。
        /// 设计意图：后排角色应优先承担狩猎以练习射击能力。
        /// 注：IsBackRow 结果由调用方预计算存入 backRowCache，避免比较器内重复调用 DetectRole。
        /// </summary>
        private static int ComparePawnsForHunting(Pawn a, Pawn b, List<SkillDef> skills)
        {
            // 后排优先（true 排前），查表替代重复 DetectRole 调用
            bool backA = backRowCache.TryGetValue(a, out bool ba) && ba;
            bool backB = backRowCache.TryGetValue(b, out bool bb) && bb;
            if (backA != backB) return backB.CompareTo(backA);

            // 其余因子复用通用比较
            return ComparePawnsByPassionWorkCountSkill(a, b, skills);
        }

        // ════════════════════════════════════════════════════════════
        // 通用工具方法
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// 三因子排序：Passion(desc) → SkillLevel(desc) → WorkCount(asc)。
        /// 设计意图：兴趣双火>兴趣单火>无火（兴趣优先）；
        /// 同等兴趣下按技能等级高低决断（保证数量内选技能最高的）；
        /// 兴趣与技能都相同时，工作少的优先（均衡负载兜底）。
        /// </summary>
        private static int ComparePawnsByPassionWorkCountSkill(Pawn a, Pawn b, List<SkillDef> skills)
        {
            // 1. Passion 降序（Major=2 > Minor=1 > None=0）
            int passionA = GetMaxPassionForSkills(a, skills);
            int passionB = GetMaxPassionForSkills(b, skills);
            if (passionA != passionB) return passionB.CompareTo(passionA);

            // 2. Skill 降序（同兴趣下技能等级高的优先）
            float skillA = ComputeSkillScore(a, skills);
            float skillB = ComputeSkillScore(b, skills);
            if (skillA != skillB) return skillB.CompareTo(skillA);

            // 3. WorkCount 升序（兜底：兴趣与技能都相同时，工作少的优先）
            int countA = workCount[a];
            int countB = workCount[b];
            return countA.CompareTo(countB);
        }

        /// <summary>
        /// 返回该 Pawn 在指定技能集上的最高 Passion 量化值。
        /// VSE 兼容：通过 PassionHelper.GetPassionTier 统一处理 6 种 passion 类型。
        /// tier：Apathy=-1, None=0, Minor=1, Major=2, Natural=2, Critical=3。
        /// </summary>
        private static int GetMaxPassionForSkills(Pawn pawn, List<SkillDef> skills)
        {
            if (pawn?.skills == null) return 0;
            if (skills == null || skills.Count == 0) return 0;
            int max = 0;
            for (int i = 0; i < skills.Count; i++)
            {
                SkillRecord sr = pawn.skills.GetSkill(skills[i]);
                if (sr == null) continue;
                int v = (int)PassionHelper.GetPassionTier(sr.passion);
                if (v > max) max = v;
            }
            return max;
        }

        /// <summary>
        /// 返回该 Pawn 在指定技能集上的最高技能等级（用于"等级≥8 即使无火也至少 priority=3"兜底）。
        /// </summary>
        private static int GetMaxSkillLevelForSkills(Pawn pawn, List<SkillDef> skills)
        {
            if (pawn?.skills == null) return 0;
            if (skills == null || skills.Count == 0) return 0;
            int max = 0;
            for (int i = 0; i < skills.Count; i++)
            {
                SkillRecord sr = pawn.skills.GetSkill(skills[i]);
                if (sr == null) continue;
                if (sr.Level > max) max = sr.Level;
            }
            return max;
        }

        /// <summary>
        /// 技能等级保底：相关技能≥12 者 priority 不低于 2，≥8 者不低于 3。
        /// priority=1/2 已满足所有保底不处理；priority=3 仅≥12 时提升到 2（≥8 已满足不降级）。
        /// 设计意图：高技能无火者即使进了保底也应有更高优先级。
        /// </summary>
        private static int ApplySkillFloor(int priority, Pawn pawn, List<SkillDef> skills)
        {
            return ApplySkillFloorCore(priority, GetMaxSkillLevelForSkills(pawn, skills));
        }

        /// <summary>
        /// 技能等级保底的纯逻辑核心（不依赖 Pawn/SkillDef，便于单元测试）。
        /// </summary>
        internal static int ApplySkillFloorCore(int priority, int skillLevel)
        {
            // 技能≥12：priority 不低于 2（0/3/4 → 2，1/2 不变）
            if (skillLevel >= 12 && (priority == 0 || priority >= 3))
                return 2;
            // 技能≥8：priority 不低于 3（0/4 → 3，1/2/3 不变）
            if (skillLevel >= 8 && (priority == 0 || priority > 3))
                return 3;
            return priority;
        }

        /// <summary>
        /// 计算殖民者在指定技能集上的总等级（用于兜底排序）。
        /// </summary>
        private static float ComputeSkillScore(Pawn pawn, List<SkillDef> skills)
        {
            if (pawn?.skills == null) return 0f;
            if (skills == null || skills.Count == 0) return 0f;
            float total = 0f;
            for (int i = 0; i < skills.Count; i++)
            {
                SkillRecord sr = pawn.skills.GetSkill(skills[i]);
                if (sr == null) continue;
                total += sr.Level;
            }
            return total;
        }
    }
}
