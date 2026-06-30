using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using AutoEverything.Core;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.AutoWork
{
    /// <summary>
    /// 全局工作优先级分配器。
    /// 按工作分类（紧急/关键/狩猎/研究/普通技能/杂物/非技能）多遍协调分配，
    /// 引入工作计数跟踪使「同等兴趣下优先安排其他工作少的」可执行。
    /// 触发方式：ITab 底部"全局工作重配"按钮手动调用。
    /// </summary>
    public static class WorkAllocator
    {
        // 候选殖民者缓存（复用避免 GC）
        private static readonly List<Pawn> candidatePawns = new List<Pawn>();

        // 单工作候选复用列表（每遍分配前 Clear+重填）
        private static readonly List<Pawn> workCandidates = new List<Pawn>();

        // 工作计数：跟踪每 Pawn 的 priority <= 2 的专业工作数量
        // 用于「同等兴趣下优先安排其他工作少的」均衡负载
        private static readonly Dictionary<Pawn, int> workCount = new Dictionary<Pawn, int>();

        // WorkTypeDef 缓存与分类（懒加载，避免静态字段初始化器跨线程调用 DefDatabase）
        private static List<WorkTypeDef> cachedWorkTypes;
        private static readonly List<WorkTypeDef> keyWorkDefs = new List<WorkTypeDef>();
        private static readonly List<WorkTypeDef> otherSkillWorkDefs = new List<WorkTypeDef>();
        private static WorkTypeDef cachedHuntingDef;
        private static WorkTypeDef cachedFishingDef;
        private static WorkTypeDef cachedResearchDef;

        /// <summary>
        /// 全局工作优先级重配入口。
        /// 自动启用自定义优先级开关，遍历所有殖民者按规则分配工作优先级。
        /// 返回受影响的殖民者数量。
        /// </summary>
        public static int ReallocateAll()
        {
            // 1. 自动启用"自定义优先级"开关（玩家若未开启则 1-4 优先级不生效）
            if (!Find.PlaySettings.useWorkPriorities)
            {
                Find.PlaySettings.useWorkPriorities = true;
            }

            // 2. 收集候选殖民者（复用 BeltAllocator/GlobalAllocator 的过滤链）
            candidatePawns.Clear();
            foreach (Map map in Find.Maps)
            {
                foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
                {
                    if (DLCCompat.IsGhoul(pawn)) continue;
                    if (!PawnSuitabilityChecker.CanManageGear(pawn)) continue;
                    if (pawn.Dead || pawn.Downed) continue;
                    if (DLCCompat.IsSlave(pawn)) continue;
                    // 未成年可参与工作，保留
                    candidatePawns.Add(pawn);
                }
            }

            if (candidatePawns.Count == 0) return 0;

            // 3. 懒加载并分类 WorkTypeDef
            CacheAndClassifyWorkTypes();

            // 4. 重置工作计数（每次重配都从 0 开始，避免脏数据）
            workCount.Clear();
            for (int i = 0; i < candidatePawns.Count; i++)
            {
                workCount[candidatePawns[i]] = 0;
            }

            // 5. 多遍分配（顺序严格固定，前排分配结果影响后排候选排序）
            AssignEmergencyPriorities();          // 第 1 遍：紧急工作
            AssignKeyWorkPriorities();            // 第 2 遍：关键工作（Doctor/Warden/Childcare）
            AssignHuntingPriorities();            // 第 3 遍：狩猎（含 Fishing）
            AssignResearchPriorities();           // 第 4 遍：研究
            AssignOtherSkillWorkPriorities();     // 第 5 遍：其他技能工作
            AssignHaulingCleaningPriorities();    // 第 6 遍：搬运/清洁
            AssignNonSkillWorkPriorities();       // 第 7 遍：非技能工作

            return candidatePawns.Count;
        }

        /// <summary>
        /// 懒加载并按 defName/WorkTags 分类 WorkTypeDef（仅执行一次）。
        /// 分类：紧急（运行时判定）/ 关键 / 狩猎 / 研究 / 普通技能 / 杂务 / 非技能。
        /// </summary>
        private static void CacheAndClassifyWorkTypes()
        {
            if (cachedWorkTypes != null) return;
            cachedWorkTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading;

            for (int i = 0; i < cachedWorkTypes.Count; i++)
            {
                WorkTypeDef wt = cachedWorkTypes[i];

                // 关键工作白名单（defName 判断，避免 WorkTags 漏掉 Warden——其 workTags 仅 Social/AllWork 无 Caring）
                if (wt.defName == "Doctor" || wt.defName == "Warden" || wt.defName == "Childcare")
                {
                    keyWorkDefs.Add(wt);
                    continue;
                }

                // 狩猎：Hunting + Fishing（Fishing 多由 mod 添加，不存在则跳过）
                if (wt.defName == "Hunting") { cachedHuntingDef = wt; continue; }
                if (wt.defName == "Fishing") { cachedFishingDef = wt; continue; }

                // 研究
                if (wt.defName == "Research") { cachedResearchDef = wt; continue; }

                // 搬运/清洁（第 6 遍运行时按 defName 判定，不进列表）
                if (wt.defName == "Hauling" || wt.defName == "Cleaning") continue;

                // 其他技能工作：有 relevantSkills 且非上述类别
                if (wt.relevantSkills != null && wt.relevantSkills.Count > 0)
                {
                    otherSkillWorkDefs.Add(wt);
                }
                // 非技能工作（如 BasicWorker）由第 7 遍运行时遍历 cachedWorkTypes 兜底处理
            }
        }

        /// <summary>
        /// 判定 WorkTypeDef 是否为紧急工作。
        /// Firefighter 用 WorkTags 检测；Patient/PatientBedRest 的 workTags 为 None，必须用 defName 判断。
        /// </summary>
        private static bool IsEmergencyWork(WorkTypeDef wt)
        {
            return (wt.workTags & WorkTags.Firefighting) != 0
                || wt.defName == "Patient"
                || wt.defName == "PatientBedRest";
        }

        // ════════════════════════════════════════════════════════════
        // 第 1 遍：紧急工作（全部 priority=1，不计入 workCount）
        // ════════════════════════════════════════════════════════════

        private static void AssignEmergencyPriorities()
        {
            for (int i = 0; i < cachedWorkTypes.Count; i++)
            {
                WorkTypeDef wt = cachedWorkTypes[i];
                if (!IsEmergencyWork(wt)) continue;

                for (int j = 0; j < candidatePawns.Count; j++)
                {
                    Pawn pawn = candidatePawns[j];
                    if (pawn.WorkTagIsDisabled(wt.workTags)) continue;
                    pawn.workSettings.SetPriority(wt, 1);
                }
            }
        }

        // ════════════════════════════════════════════════════════════
        // 第 2 遍：关键工作（Doctor/Warden/Childcare）
        //   - 有兴趣 → priority=1，计入 workCount
        //   - 保证至少 2 人 priority >= 1（不足时按技能等级补到 1）
        //   - 其余无兴趣者 → priority=0（禁用，不备选）
        // ════════════════════════════════════════════════════════════

        private static void AssignKeyWorkPriorities()
        {
            for (int i = 0; i < keyWorkDefs.Count; i++)
            {
                AssignKeyWorkType(keyWorkDefs[i]);
            }
        }

        private static void AssignKeyWorkType(WorkTypeDef workType)
        {
            workCandidates.Clear();
            for (int i = 0; i < candidatePawns.Count; i++)
            {
                Pawn pawn = candidatePawns[i];
                if (pawn.WorkTagIsDisabled(workType.workTags)) continue;
                workCandidates.Add(pawn);
            }
            if (workCandidates.Count == 0) return;

            // 分组：passionate（有兴趣） vs nonPassionate
            // 注：此处复用静态列表需在方法栈内分配，因为两组要同时存在
            List<Pawn> passionate = new List<Pawn>();
            List<Pawn> nonPassionate = new List<Pawn>();
            for (int i = 0; i < workCandidates.Count; i++)
            {
                Pawn pawn = workCandidates[i];
                if (HasPassionForAnySkill(pawn, workType.relevantSkills))
                    passionate.Add(pawn);
                else
                    nonPassionate.Add(pawn);
            }

            // passionate 按技能等级降序排序（取技能最高的 top 2）
            passionate.Sort((a, b) =>
                ComputeSkillScore(b, workType.relevantSkills)
                .CompareTo(ComputeSkillScore(a, workType.relevantSkills)));

            // top 2 passionate → priority=1（保证数量内），其余 passionate → priority=4（备选）
            for (int i = 0; i < passionate.Count; i++)
            {
                int priority = i < 2 ? 1 : 4;
                passionate[i].workSettings.SetPriority(workType, priority);
                if (priority <= 2) workCount[passionate[i]]++;
            }

            // 保证至少 2 人 priority >= 1：若 passionate < 2，从 nonPassionate 补足
            if (passionate.Count < 2 && nonPassionate.Count > 0)
            {
                // 按技能等级降序补足（取技能最高的补到 priority=1）
                nonPassionate.Sort((a, b) =>
                    ComputeSkillScore(b, workType.relevantSkills)
                    .CompareTo(ComputeSkillScore(a, workType.relevantSkills)));
                int need = 2 - passionate.Count;
                for (int i = 0; i < nonPassionate.Count; i++)
                {
                    // 补足的 → priority=1，其余无兴趣者 → priority=0（禁用）
                    int priority = i < need ? 1 : 0;
                    nonPassionate[i].workSettings.SetPriority(workType, priority);
                    if (priority <= 2) workCount[nonPassionate[i]]++;
                }
            }
            else
            {
                // 已 >= 2 人 priority >= 1，无兴趣者 → 0（禁用）
                for (int i = 0; i < nonPassionate.Count; i++)
                {
                    nonPassionate[i].workSettings.SetPriority(workType, 0);
                }
            }
        }

        // ════════════════════════════════════════════════════════════
        // 第 3 遍：狩猎（Hunting + Fishing）
        //   - 候选排序：后排优先 → passion desc → skill desc → workCount asc
        //   - top 2 → priority=2，计入 workCount
        //   - 其余 → priority=0（禁用）
        // ════════════════════════════════════════════════════════════

        private static void AssignHuntingPriorities()
        {
            if (cachedHuntingDef != null)
                AssignHuntingType(cachedHuntingDef);
            if (cachedFishingDef != null)
                AssignHuntingType(cachedFishingDef);
        }

        private static void AssignHuntingType(WorkTypeDef workType)
        {
            workCandidates.Clear();
            for (int i = 0; i < candidatePawns.Count; i++)
            {
                Pawn pawn = candidatePawns[i];
                if (pawn.WorkTagIsDisabled(workType.workTags)) continue;
                workCandidates.Add(pawn);
            }
            if (workCandidates.Count == 0) return;

            // 排序：后排优先 → passion desc → skill desc → workCount asc
            workCandidates.Sort((a, b) => ComparePawnsForHunting(a, b, workType.relevantSkills));

            // top 2 → priority=2，其余有兴趣 → priority=4（备选），其余无兴趣 → priority=0（禁用）
            for (int i = 0; i < workCandidates.Count; i++)
            {
                Pawn pawn = workCandidates[i];
                int priority;
                if (i < 2)
                {
                    priority = 2;
                }
                else if (HasPassionForAnySkill(pawn, workType.relevantSkills))
                {
                    priority = 4;
                }
                else
                {
                    priority = 0;
                }
                pawn.workSettings.SetPriority(workType, priority);
                if (priority <= 2) workCount[pawn]++;
            }
        }

        /// <summary>
        /// 狩猎专用比较器：在通用三因子排序前增加「后排优先」判定。
        /// 后排 = ArmorPreference.Flexible（Shooter/Hunter/Leader）。
        /// 设计意图：后排角色应优先承担狩猎以练习射击能力。
        /// </summary>
        private static int ComparePawnsForHunting(Pawn a, Pawn b, List<SkillDef> skills)
        {
            // 后排优先（true 排前）
            bool backA = RoleDetector.IsBackRow(RoleDetector.DetectRole(a));
            bool backB = RoleDetector.IsBackRow(RoleDetector.DetectRole(b));
            if (backA != backB) return backB.CompareTo(backA);

            // 其余因子复用通用比较
            return ComparePawnsByPassionWorkCountSkill(a, b, skills);
        }

        // ════════════════════════════════════════════════════════════
        // 第 4 遍：研究
        //   - 候选排序：passion desc → skill desc → workCount asc
        //   - guarantee 1：top 1 → priority=2，计入 workCount
        //   - 其余 → priority=0（禁用，不备选）
        // ════════════════════════════════════════════════════════════

        private static void AssignResearchPriorities()
        {
            if (cachedResearchDef == null) return;
            WorkTypeDef workType = cachedResearchDef;

            workCandidates.Clear();
            for (int i = 0; i < candidatePawns.Count; i++)
            {
                Pawn pawn = candidatePawns[i];
                if (pawn.WorkTagIsDisabled(workType.workTags)) continue;
                workCandidates.Add(pawn);
            }
            if (workCandidates.Count == 0) return;

            // 排序：passion desc → skill desc → workCount asc
            workCandidates.Sort((a, b) => ComparePawnsByPassionWorkCountSkill(a, b, workType.relevantSkills));

            // guarantee 1：top 1 → priority=2，其余有兴趣 → priority=4（备选），其余无兴趣 → priority=0（禁用）
            for (int i = 0; i < workCandidates.Count; i++)
            {
                Pawn pawn = workCandidates[i];
                int priority;
                if (i < 1)
                {
                    priority = 2;
                }
                else if (HasPassionForAnySkill(pawn, workType.relevantSkills))
                {
                    priority = 4;
                }
                else
                {
                    priority = 0;
                }
                pawn.workSettings.SetPriority(workType, priority);
                if (priority <= 2) workCount[pawn]++;
            }
        }

        // ════════════════════════════════════════════════════════════
        // 第 5 遍：其他技能工作（Cooking/Growing/Mining/Crafting 等）
        //   - 候选排序：passion desc → skill desc → workCount asc
        //   - guarantee 2：top 2 → priority=2，计入 workCount
        //   - 其余 → priority=0（禁用，不备选）
        // ════════════════════════════════════════════════════════════

        private static void AssignOtherSkillWorkPriorities()
        {
            for (int i = 0; i < otherSkillWorkDefs.Count; i++)
            {
                AssignOtherSkillWorkType(otherSkillWorkDefs[i]);
            }
        }

        private static void AssignOtherSkillWorkType(WorkTypeDef workType)
        {
            workCandidates.Clear();
            for (int i = 0; i < candidatePawns.Count; i++)
            {
                Pawn pawn = candidatePawns[i];
                if (pawn.WorkTagIsDisabled(workType.workTags)) continue;
                workCandidates.Add(pawn);
            }
            if (workCandidates.Count == 0) return;

            // 排序：passion desc → skill desc → workCount asc
            workCandidates.Sort((a, b) => ComparePawnsByPassionWorkCountSkill(a, b, workType.relevantSkills));

            // guarantee 2：top 2 → priority=2，其余有兴趣 → priority=4（备选），其余无兴趣 → priority=0（禁用）
            for (int i = 0; i < workCandidates.Count; i++)
            {
                Pawn pawn = workCandidates[i];
                int priority;
                if (i < 2)
                {
                    priority = 2;
                }
                else if (HasPassionForAnySkill(pawn, workType.relevantSkills))
                {
                    priority = 4;
                }
                else
                {
                    priority = 0;
                }
                pawn.workSettings.SetPriority(workType, priority);
                if (priority <= 2) workCount[pawn]++;
            }
        }

        // ════════════════════════════════════════════════════════════
        // 第 6 遍：搬运/清洁（按 CombatTier 分档，不计入 workCount）
        //   S=4, A/B/C=3, D/X=1
        // ════════════════════════════════════════════════════════════

        private static void AssignHaulingCleaningPriorities()
        {
            for (int i = 0; i < cachedWorkTypes.Count; i++)
            {
                WorkTypeDef wt = cachedWorkTypes[i];
                if (wt.defName != "Hauling" && wt.defName != "Cleaning") continue;

                for (int j = 0; j < candidatePawns.Count; j++)
                {
                    Pawn pawn = candidatePawns[j];
                    if (pawn.WorkTagIsDisabled(wt.workTags)) continue;
                    CombatTier tier = CombatEvaluator.GetCombatTier(pawn);
                    int priority;
                    switch (tier)
                    {
                        case CombatTier.S: priority = 4; break;
                        case CombatTier.D:
                        case CombatTier.X: priority = 1; break;
                        default: priority = 3; break; // A/B/C
                    }
                    pawn.workSettings.SetPriority(wt, priority);
                }
            }
        }

        // ════════════════════════════════════════════════════════════
        // 第 7 遍：非技能工作（BasicWorker 等，全部 priority=3，不计入 workCount）
        // ════════════════════════════════════════════════════════════

        private static void AssignNonSkillWorkPriorities()
        {
            for (int i = 0; i < cachedWorkTypes.Count; i++)
            {
                WorkTypeDef wt = cachedWorkTypes[i];
                // 跳过已在前 6 遍处理的类别
                if (IsEmergencyWork(wt)) continue;
                if (wt.defName == "Hauling" || wt.defName == "Cleaning") continue;
                // 仅处理无 relevantSkills 的非技能工作
                if (wt.relevantSkills != null && wt.relevantSkills.Count > 0) continue;

                for (int j = 0; j < candidatePawns.Count; j++)
                {
                    Pawn pawn = candidatePawns[j];
                    if (pawn.WorkTagIsDisabled(wt.workTags)) continue;
                    pawn.workSettings.SetPriority(wt, 3);
                }
            }
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
        /// 返回该 Pawn 在指定技能集上的最高 Passion 量化值（None=0, Minor=1, Major=2）。
        /// </summary>
        private static int GetMaxPassionForSkills(Pawn pawn, List<SkillDef> skills)
        {
            if (pawn?.skills == null) return 0;
            int max = 0;
            for (int i = 0; i < skills.Count; i++)
            {
                SkillRecord sr = pawn.skills.GetSkill(skills[i]);
                if (sr == null) continue;
                int v = (int)sr.passion;
                if (v > max) max = v;
            }
            return max;
        }

        /// <summary>
        /// 检查殖民者是否对任一指定技能有兴趣（Minor 或 Major）。
        /// </summary>
        private static bool HasPassionForAnySkill(Pawn pawn, List<SkillDef> skills)
        {
            if (pawn?.skills == null) return false;
            for (int i = 0; i < skills.Count; i++)
            {
                SkillRecord sr = pawn.skills.GetSkill(skills[i]);
                if (sr != null && sr.passion != Passion.None)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 计算殖民者在指定技能集上的总等级（用于兜底排序）。
        /// </summary>
        private static float ComputeSkillScore(Pawn pawn, List<SkillDef> skills)
        {
            if (pawn?.skills == null) return 0f;
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
