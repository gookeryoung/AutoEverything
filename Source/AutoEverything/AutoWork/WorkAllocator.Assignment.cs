using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;
using AutoEverything.Core;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.AutoWork
{
    public static partial class WorkAllocator
    {
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
        // 最后一遍：辅助工作（搬运/清洁/非技能 BasicWorker 等，不计入 workCount）
        //   规则：评级 S+→4，A→3，其余→1（高价值殖民者少做辅助工作）
        // ════════════════════════════════════════════════════════════

        private static void AssignServiceWorkPriorities()
        {
            for (int i = 0; i < cachedWorkTypes.Count; i++)
            {
                WorkTypeDef wt = cachedWorkTypes[i];
                if (IsEmergencyWork(wt)) continue;
                // 搬运/清洁 + 无 relevantSkills 的非技能工作（如 BasicWorker）
                bool isHaulingCleaning = wt.defName == "Hauling" || wt.defName == "Cleaning";
                bool isNonSkill = wt.relevantSkills == null || wt.relevantSkills.Count == 0;
                if (!isHaulingCleaning && !isNonSkill) continue;

                AssignServiceWorkType(wt);
            }
        }

        private static void AssignServiceWorkType(WorkTypeDef workType)
        {
            workCandidates.Clear();
            for (int i = 0; i < candidatePawns.Count; i++)
            {
                Pawn pawn = candidatePawns[i];
                if (pawn.WorkTagIsDisabled(workType.workTags)) continue;
                workCandidates.Add(pawn);
            }
            if (workCandidates.Count == 0) return;

            // 新规则：评级 S+→4，A→3，其余→1（无需排序，直接按评级分档）
            // 评级查询走 TierCacheService（2500 tick 缓存，工作重配周期 10000 tick > TTL 自然过期）
            for (int i = 0; i < workCandidates.Count; i++)
            {
                Pawn pawn = workCandidates[i];
                CombatTier tier = TierCacheService.GetTier(pawn);
                int priority;
                switch (tier)
                {
                    case CombatTier.SSS:
                    case CombatTier.SS:
                    case CombatTier.S: priority = 4; break;
                    case CombatTier.A: priority = 3; break;
                    default: priority = 1; break; // B/C/D/X
                }
                // 豁免：Hauling/Cleaning priority=1 会先于研究(priority>=2)执行打断研究，研究员降为 4
                if (priority == 1 && ShouldDowngradeForResearch(workType, pawn))
                {
                    priority = 4;
                }

                pawn.workSettings.SetPriority(workType, priority);
                // 辅助工作不计入 workCount
            }
        }

        /// <summary>
        /// 服务工作研究豁免：Hauling/Cleaning 的 priority=1 会先于研究(priority>=2)执行，
        /// 中断研究 Job。研究员这两类服务工作降为 4，让研究优先执行。
        /// </summary>
        private static bool ShouldDowngradeForResearch(WorkTypeDef wt, Pawn pawn)
        {
            return (wt.defName == "Hauling" || wt.defName == "Cleaning")
                && GetMaxResearchPriority(pawn) >= 2;
        }

        // ════════════════════════════════════════════════════════════
        // 统一分配方法：按 WorkAllocationConfig 四大原则分配工作优先级
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// 统一工作优先级分配方法，按 config 四大原则执行：
        /// 1. 保证 GuaranteeCount 人承担（top N 内双火/单火/无火分别给 GuaranteeMajor/Minor/NonPassionate）
        /// 2. 三因子排序选 guarantee 人选（passion desc → skill desc → workCount asc）
        /// 3. 超出 guarantee 的双火/单火者分别给 FloorMajorPriority/FloorMinorPriority 保底优先级
        /// 4. 超出 guarantee 的无火者直接给 FloorNonPassionatePriority（通常为0，规则要求无火优先级0）
        /// 注：奴隶在专业工作中与殖民者同流程，按兴趣/技能参与分配，无特殊优先级。
        /// workTypes 数组单元素 = 单工作分配；多元素 = 组分配（手工类，一次排序同时分配，共享 1 个 workCount）。
        /// </summary>
        private static void AssignWork(WorkTypeDef[] workTypes, WorkAllocationConfig config)
        {
            // 组内工作类型 workTags/相关技能一致（手工类 Smithing/Tailoring/Crafting 共享 Crafting），
            // 用第一个过滤与排序即可
            WorkTypeDef firstWork = workTypes[0];
            List<SkillDef> skills = firstWork.relevantSkills;

            // 调试：构造工作类型组标签（单工作返回 defName，组分配返回 Smithing+Tailoring+Crafting）
            string groupLabel = AEDebug.IsActive ? BuildWorkGroupLabel(workTypes) : null;

            // 候选收集：包含满载者，让满载者参与排序
            // 满载者在 top N 内走 Floor 保底（不抢占 Guarantee），无火者落在 top N 外走 Floor(=0)
            // 避免满载者被跳过后，候选无火者错误获得 Guarantee 保底导致重复承担（保底人数超标）
            workCandidates.Clear();
            for (int i = 0; i < candidatePawns.Count; i++)
            {
                Pawn pawn = candidatePawns[i];
                if (pawn.WorkTagIsDisabled(firstWork.workTags)) continue;
                // 狩猎类需远程武器：避免无远程武器者被分配
                if (config.RequireRangedWeapon && pawn.equipment?.Primary?.def.IsRangedWeapon != true) continue;
                workCandidates.Add(pawn);
            }
            // 回退放宽：候选不足保证人数时（小殖民地人手不足），满载者走 Guarantee 逻辑保证保底
            // 候选已包含满载者，无需重新收集，仅设标志
            bool fallbackRelaxed = workCandidates.Count < config.GuaranteeCount;
            if (workCandidates.Count == 0)
            {
                if (AEDebug.IsActive) AEDebug.Log(() => $"[WorkAllocator] {groupLabel}: SKIP (no candidates, total={candidatePawns.Count})");
                return;
            }

            if (AEDebug.IsActive) AEDebug.Log(() =>
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("[WorkAllocator] ").Append(groupLabel)
                  .Append(": candidates=").Append(workCandidates.Count).Append('/').Append(candidatePawns.Count)
                  .Append(" guarantee=").Append(config.GuaranteeCount)
                  .Append(" requireRanged=").Append(config.RequireRangedWeapon);
                if (fallbackRelaxed) sb.Append(" [FALLBACK RELAXED]");
                return sb.ToString();
            });

            // 排序：后排优先（仅狩猎类） → passion desc → skill desc → workCount asc
            if (config.UseBackRowSort)
            {
                // 预计算 IsBackRow 缓存，避免 Sort 比较器内重复调用 DetectRole（性能优化）
                backRowCache.Clear();
                for (int i = 0; i < workCandidates.Count; i++)
                {
                    Pawn p = workCandidates[i];
                    backRowCache[p] = RoleDetector.IsBackRow(RoleDetector.DetectRole(p));
                }
                workCandidates.Sort((a, b) => ComparePawnsForHunting(a, b, skills));
            }
            else
            {
                workCandidates.Sort((a, b) => ComparePawnsByPassionWorkCountSkill(a, b, skills));
            }

            // 调试：dump 排序后候选清单（passion/skill/workCount）
            if (AEDebug.IsActive) AEDebug.Log(() =>
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("[WorkAllocator] ").Append(groupLabel).Append(" sorted: ");
                for (int i = 0; i < workCandidates.Count; i++)
                {
                    Pawn p = workCandidates[i];
                    if (i > 0) sb.Append(", ");
                    sb.Append(AEDebug.Label(p))
                      .Append("[p=").Append(GetMaxPassionForSkills(p, skills))
                      .Append(" s=").Append(GetMaxSkillLevelForSkills(p, skills))
                      .Append(" wc=").Append(workCount[p])
                      .Append(workCount[p] >= MaxCoreWorkCount ? "!" : "")
                      .Append(']');
                }
                return sb.ToString();
            });

            // 按配置分配优先级：对所有工作类型设置相同优先级（组分配共享一次排序结果）
            for (int i = 0; i < workCandidates.Count; i++)
            {
                Pawn pawn = workCandidates[i];
                int passionLevel = GetMaxPassionForSkills(pawn, skills);
                // 满载者（回退放宽加入的）不抢占 Guarantee 优先级，只给 Floor 保底
                // 避免工作很多的专家被回退放宽后仍获得 priority=1
                // 例外：回退放宽模式（人手不足）下，满载者必须走 Guarantee 逻辑保证保底人数，
                // 否则无火满载者会走 Floor(=0) 导致 GuaranteeCount 保底失效
                bool isOverloaded = workCount[pawn] >= MaxCoreWorkCount;
                int priority;

                if (i < config.GuaranteeCount && (!isOverloaded || fallbackRelaxed))
                {
                    // 原则 1+2：保证 N 人承担（仅未满载者），双火/单火/无火分别给优先级
                    if (passionLevel >= (int)PassionHelper.PassionTier.Major)
                        priority = config.GuaranteeMajorPriority;
                    else if (passionLevel >= (int)PassionHelper.PassionTier.Minor)
                        priority = config.GuaranteeMinorPriority;
                    else
                        priority = config.GuaranteeNonPassionatePriority;
                }
                else
                {
                    // 原则 3+4：超出 guarantee 或满载者降级，双火/单火/无火分别给 Floor 保底
                    if (passionLevel >= (int)PassionHelper.PassionTier.Major)
                        priority = config.FloorMajorPriority;
                    else if (passionLevel >= (int)PassionHelper.PassionTier.Minor)
                        priority = config.FloorMinorPriority;
                    else
                        priority = config.FloorNonPassionatePriority;
                }

                // 技能等级保底：相关技能≥8 者 priority 不低于 3
                priority = ApplySkillFloor(priority, pawn, skills);

                // 对所有工作类型设置相同优先级（单工作循环 1 次，组分配循环 N 次共享排序结果）
                for (int j = 0; j < workTypes.Length; j++)
                {
                    pawn.workSettings.SetPriority(workTypes[j], priority);
                }

                // 调试：记录每个候选的最终优先级与归类（GM=Guarantee双火/Gi=Guarantee单火/FM=Floor双火/Fi=Floor单火/N=无火）
                if (AEDebug.IsActive) AEDebug.Log(() =>
                {
                    int finalIdx = i;
                    string bucket;
                    if (finalIdx < config.GuaranteeCount && (!isOverloaded || fallbackRelaxed))
                    {
                        bucket = passionLevel >= (int)PassionHelper.PassionTier.Major ? "GM"
                            : (passionLevel >= (int)PassionHelper.PassionTier.Minor ? "Gi" : "N");
                    }
                    else
                    {
                        bucket = passionLevel >= (int)PassionHelper.PassionTier.Major ? "FM"
                            : (passionLevel >= (int)PassionHelper.PassionTier.Minor ? "Fi" : "N");
                    }
                    int wcBefore = workCount[pawn];
                    return $"[WorkAllocator] {groupLabel}[{finalIdx}] {AEDebug.Label(pawn)} p={passionLevel} s={GetMaxSkillLevelForSkills(pawn, skills)} wc={wcBefore}{(isOverloaded ? "!" : "")} [{bucket}] → prio={priority}{(priority <= 2 ? " (+wc)" : "")}";
                });

                // priority<=2 计入 workCount：组分配只加 1（手工类视为 1 个专业工作，由外层调用方保证 phase 共享）
                if (priority <= 2)
                {
                    workCount[pawn]++;
                }
            }

            // 末尾清理非候选的旧优先级（替代全局清零，避免硬上限跳过者保留旧值）
            // RimWorld SetPriority 对相同值 no-op，候选者已 SetPriority(intended) 不会重复触发重评估
            // 非候选分两类处理：
            //   - WorkTagIsDisabled/无远程武器：给0（不能做此工作）
            //   - 硬上限跳过的满载者：有火者给 Floor 保底（双火/单火），无火者给0
            //     避免高技能有火者被硬上限完全排除，违背"有火者保底"原则
            inWorkCandidates.Clear();
            for (int i = 0; i < workCandidates.Count; i++)
            {
                inWorkCandidates.Add(workCandidates[i]);
            }
            for (int i = 0; i < candidatePawns.Count; i++)
            {
                Pawn pawn = candidatePawns[i];
                if (inWorkCandidates.Contains(pawn)) continue;

                // WorkTagIsDisabled 或无远程武器者给0（不能做此工作）
                if (pawn.WorkTagIsDisabled(firstWork.workTags))
                {
                    for (int j = 0; j < workTypes.Length; j++)
                        pawn.workSettings.SetPriority(workTypes[j], 0);
                    continue;
                }
                if (config.RequireRangedWeapon && pawn.equipment?.Primary?.def.IsRangedWeapon != true)
                {
                    for (int j = 0; j < workTypes.Length; j++)
                        pawn.workSettings.SetPriority(workTypes[j], 0);
                    continue;
                }

                // 硬上限跳过的满载者：有火者给 Floor 保底，无火者给0
                int passionLevel = GetMaxPassionForSkills(pawn, skills);
                int priority;
                if (passionLevel >= (int)PassionHelper.PassionTier.Major)
                    priority = config.FloorMajorPriority;
                else if (passionLevel >= (int)PassionHelper.PassionTier.Minor)
                    priority = config.FloorMinorPriority;
                else
                    priority = 0;
                // 技能等级保底：相关技能≥8 者 priority 不低于 3
                priority = ApplySkillFloor(priority, pawn, skills);
                for (int j = 0; j < workTypes.Length; j++)
                    pawn.workSettings.SetPriority(workTypes[j], priority);
            }
        }
    }
}
