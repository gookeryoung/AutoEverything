using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;
using AutoEverything.Core;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.AutoWork
{
    /// <summary>
    /// 全局工作优先级分配器。
    /// 按工作分类（紧急/重要专业/普通专业/次级专业/研究/辅助）多遍协调分配，
    /// 引入工作计数跟踪使「同等兴趣下优先安排其他工作少的」可执行。
    /// 所有技能类工作复用统一 AssignWorkType + WorkAllocationConfig 四大原则分配。
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

        // 狩猎排序缓存：IsBackRow 结果预计算，避免 Sort 比较器内重复调用 DetectRole
        // 每次狩猎分配前 Clear+重填，仅在该次 Sort 内有效
        private static readonly Dictionary<Pawn, bool> backRowCache = new Dictionary<Pawn, bool>();

        // 辅助工作评级缓存：CombatTier 预计算，避免重复调用 GetCombatTier
        // 每次辅助工作分配前 Clear+重填
        private static readonly Dictionary<Pawn, CombatTier> tierCache = new Dictionary<Pawn, CombatTier>();

        // 候选标记缓存：末尾清零非候选旧优先级时快速判断 pawn 是否在 workCandidates 内
        // 替代全局清零：只对非候选调用 SetPriority(0)，候选者 SetPriority(intended) 由 RimWorld no-op 优化避免重评估
        private static readonly HashSet<Pawn> inWorkCandidates = new HashSet<Pawn>();

        // workCount 硬上限：每人最多承担 N 项 priority≤2 的专业工作，超限者跳过候选
        // 候选不足保证人数时回退放宽，保证小殖民地工作有人做
        private const int MaxCoreWorkCount = 3;

        // ════════════════════════════════════════════════════════════
        // 工作分配配置（静态只读，避免每次分配重复构造）
        // ════════════════════════════════════════════════════════════

        // 重要专业工作（Doctor/Warden/Childcare/Cooking/PlantCutting）：保底2，双火1/单火2/无火3(保底)/超出无火0
        private static readonly WorkAllocationConfig KeyWorkConfig = new WorkAllocationConfig
        {
            GuaranteeCount = 2,
            GuaranteeMajorPriority = 1,
            GuaranteeMinorPriority = 2,
            GuaranteeNonPassionatePriority = 3,
            FloorMajorPriority = 1,
            FloorMinorPriority = 2,
            FloorNonPassionatePriority = 0,
            RequireRangedWeapon = false,
            UseBackRowSort = false
        };

        // 普通专业工作（Construction/Mining/Growing/Smithing/Tailoring/Crafting/Art）：保底2，双火2/单火3/无火3(保底)/超出无火0
        private static readonly WorkAllocationConfig OtherSkillConfig = new WorkAllocationConfig
        {
            GuaranteeCount = 2,
            GuaranteeMajorPriority = 2,
            GuaranteeMinorPriority = 3,
            GuaranteeNonPassionatePriority = 3,
            FloorMajorPriority = 2,
            FloorMinorPriority = 3,
            FloorNonPassionatePriority = 0,
            RequireRangedWeapon = false,
            UseBackRowSort = false
        };

        // 次级专业工作-普通类（Handling/Fishing）：保底2，双火2/单火4/无火3(保底)/超出无火0
        private static readonly WorkAllocationConfig SecondaryConfig = new WorkAllocationConfig
        {
            GuaranteeCount = 2,
            GuaranteeMajorPriority = 2,
            GuaranteeMinorPriority = 4,
            GuaranteeNonPassionatePriority = 3,
            FloorMajorPriority = 2,
            FloorMinorPriority = 4,
            FloorNonPassionatePriority = 0,
            RequireRangedWeapon = false,
            UseBackRowSort = false
        };

        // 次级专业工作-远程类（Hunting）：保底2，双火2/单火4/无火3(保底)/超出无火0，需远程武器+后排排序
        private static readonly WorkAllocationConfig SecondaryRangedConfig = new WorkAllocationConfig
        {
            GuaranteeCount = 2,
            GuaranteeMajorPriority = 2,
            GuaranteeMinorPriority = 4,
            GuaranteeNonPassionatePriority = 3,
            FloorMajorPriority = 2,
            FloorMinorPriority = 4,
            FloorNonPassionatePriority = 0,
            RequireRangedWeapon = true,
            UseBackRowSort = true
        };

        // 研究工作（Research/DarkStudy）：保底1，双火2/单火3/无火0/超出无火0，最后分配
        private static readonly WorkAllocationConfig ResearchConfig = new WorkAllocationConfig
        {
            GuaranteeCount = 1,
            GuaranteeMajorPriority = 2,
            GuaranteeMinorPriority = 3,
            GuaranteeNonPassionatePriority = 0,
            FloorMajorPriority = 2,
            FloorMinorPriority = 3,
            FloorNonPassionatePriority = 0,
            RequireRangedWeapon = false,
            UseBackRowSort = false
        };

        // ════════════════════════════════════════════════════════════
        // 分配阶段：按顺序执行的工作类型+配置列表（数据驱动，消除多个 Assign*Priorities 方法）
        // ════════════════════════════════════════════════════════════

        private struct WorkPhase
        {
            public WorkTypeDef WorkType;            // 单工作类型（WorkTypes 为 null 时使用）
            public List<WorkTypeDef> WorkTypes;     // 工作类型组（非 null 时组分配：一次排序，同时分配，共享 1 个 workCount）
            public WorkAllocationConfig Config;
        }

        private static List<WorkPhase> skillWorkPhases;

        // WorkTypeDef 缓存与分类（懒加载，避免静态字段初始化器跨线程调用 DefDatabase）
        private static List<WorkTypeDef> cachedWorkTypes;
        private static readonly List<WorkTypeDef> keyWorkDefs = new List<WorkTypeDef>();
        private static readonly List<WorkTypeDef> otherSkillWorkDefs = new List<WorkTypeDef>();
        private static WorkTypeDef cachedHuntingDef;
        private static WorkTypeDef cachedFishingDef;
        private static WorkTypeDef cachedHandlingDef;
        private static WorkTypeDef cachedResearchDef;
        private static WorkTypeDef cachedDarkStudyDef;
        private static WorkTypeDef cachedPlantCuttingDef;
        private static WorkTypeDef cachedCookingDef;

        /// <summary>
        /// 工作分配配置：统一描述各工作类型的优先级规则，编码四大原则。
        /// 1. GuaranteeCount：保证至少 N 人承担（无论有无火），top N 内双火/单火/无火分别给 GuaranteeMajor/Minor/NonPassionate
        /// 2. 三因子排序选 guarantee 人选（passion desc → skill desc → workCount asc）
        /// 3. FloorMajorPriority/FloorMinorPriority：双火/单火者超出 guarantee 部分给此保底优先级
        /// 4. FloorNonPassionatePriority：无火者超出 guarantee 部分给此优先级（通常为0，规则要求无火优先级0）
        /// 注：奴隶与殖民者同流程，按兴趣/技能参与分配，无特殊优先级。
        /// </summary>
        private struct WorkAllocationConfig
        {
            public int GuaranteeCount;                  // 保证人数
            public int GuaranteeMajorPriority;          // top N 内双火优先级
            public int GuaranteeMinorPriority;          // top N 内单火优先级
            public int GuaranteeNonPassionatePriority;  // top N 内无火优先级（保底3）
            public int FloorMajorPriority;              // 超出 top N 的双火保底优先级
            public int FloorMinorPriority;              // 超出 top N 的单火保底优先级
            public int FloorNonPassionatePriority;      // 超出 top N 的无火优先级（0）
            public bool RequireRangedWeapon;            // 是否要求持有远程武器（狩猎类）
            public bool UseBackRowSort;                 // 是否启用后排优先排序（狩猎类）
        }

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
            // 奴隶也参与工作分配：Biotech DLC 的 SlavesOfColonySpawned 返回空列表时不影响
            // 医疗守卫：跳过正在执行医疗 Job/休养的 Pawn——SetPriority 会触发 Job 重评估，
            // 取消医生正在执行的 TendPatient/DoBill(Bill_Medical)，导致手术死循环或伤员失救
            candidatePawns.Clear();
            foreach (Map map in Find.Maps)
            {
                // 殖民者（FreeColonistsSpawned 不含奴隶）
                foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
                {
                    if (DLCCompat.IsGhoul(pawn)) continue;
                    if (!PawnSuitabilityChecker.CanManageGear(pawn)) continue;
                    if (pawn.Dead || pawn.Downed) continue;
                    if (pawn.workSettings == null) continue;
                    if (PawnJobGuard.ShouldSkipForMedical(pawn)) continue;
                    candidatePawns.Add(pawn);
                }
                // 奴隶（Biotech DLC 才有，无 DLC 时 SlavesOfColonySpawned 返回空列表）
                foreach (Pawn pawn in map.mapPawns.SlavesOfColonySpawned)
                {
                    if (DLCCompat.IsGhoul(pawn)) continue;
                    if (!PawnSuitabilityChecker.CanManageGear(pawn)) continue;
                    if (pawn.Dead || pawn.Downed) continue;
                    if (pawn.workSettings == null) continue;
                    if (PawnJobGuard.ShouldSkipForMedical(pawn)) continue;
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

            AEDebug.Log(() => $"[WorkAllocator] === ReallocateAll start (pawns={candidatePawns.Count}) ===");

            // 5. 多遍分配（顺序严格固定，前排分配结果影响后排候选排序）
            // 阶段列表驱动：紧急 → 重要专业 → 普通专业 → 次级专业 → 研究
            // 手工类使用组分配：Smithing/Tailoring/Crafting 一次排序同时分配，共享 1 个 workCount
            AssignEmergencyPriorities();          // 第 1 遍：紧急工作
            for (int i = 0; i < skillWorkPhases.Count; i++)  // 第 2-N 遍：技能工作
            {
                WorkPhase phase = skillWorkPhases[i];
                if (phase.WorkTypes != null)
                    AssignWorkGroup(phase.WorkTypes, phase.Config);
                else
                    AssignWorkType(phase.WorkType, phase.Config);
            }
            AssignServiceWorkPriorities();        // 最后一遍：辅助工作（搬运/清洁/非技能）

            // 调试：dump 最终 workCount，便于分析硬上限拦截是否影响双火 pawn
            AEDebug.Log(() =>
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("[WorkAllocator] Final workCount: ");
                for (int i = 0; i < candidatePawns.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(AEDebug.Label(candidatePawns[i])).Append('=').Append(workCount[candidatePawns[i]]);
                }
                sb.Append("\n[WorkAllocator] === ReallocateAll end ===");
                return sb.ToString();
            });

            return candidatePawns.Count;
        }

        /// <summary>
        /// 懒加载并按 defName/WorkTags 分类 WorkTypeDef（仅执行一次）。
        /// 分类：紧急（运行时判定）/ 重要专业 / 普通专业 / 次级专业 / 研究 / 辅助（搬运/清洁/非技能）。
        /// </summary>
        private static void CacheAndClassifyWorkTypes()
        {
            if (cachedWorkTypes != null) return;
            cachedWorkTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading;

            for (int i = 0; i < cachedWorkTypes.Count; i++)
            {
                WorkTypeDef wt = cachedWorkTypes[i];

                // 重要专业工作白名单（Doctor/Warden/Childcare，defName 判断避免 WorkTags 漏掉 Warden）
                if (wt.defName == "Doctor" || wt.defName == "Warden" || wt.defName == "Childcare")
                {
                    keyWorkDefs.Add(wt);
                    continue;
                }
                // 重要专业工作（Cooking/PlantCutting）单独缓存以便控制阶段顺序
                if (wt.defName == "Cooking") { cachedCookingDef = wt; continue; }
                if (wt.defName == "PlantCutting") { cachedPlantCuttingDef = wt; continue; }

                // 次级专业工作（Handling/Hunting/Fishing）
                if (wt.defName == "Handling") { cachedHandlingDef = wt; continue; }
                if (wt.defName == "Hunting") { cachedHuntingDef = wt; continue; }
                if (wt.defName == "Fishing") { cachedFishingDef = wt; continue; }

                // 研究工作（Research/DarkStudy）
                if (wt.defName == "Research") { cachedResearchDef = wt; continue; }
                if (wt.defName == "DarkStudy") { cachedDarkStudyDef = wt; continue; }

                // 辅助工作（最后一遍运行时按 defName 判定，不进列表）
                if (wt.defName == "Hauling" || wt.defName == "Cleaning") continue;

                // 普通专业工作：有 relevantSkills 且非上述类别（Construction/Mining/Growing/Smithing/Tailoring/Crafting/Art 等）
                if (wt.relevantSkills != null && wt.relevantSkills.Count > 0)
                {
                    otherSkillWorkDefs.Add(wt);
                }
                // 非技能工作（如 BasicWorker）由最后一遍运行时遍历 cachedWorkTypes 兜底处理
            }

            // 构建分配阶段列表（顺序决定 workCount 累加次序，影响后续硬上限拦截）
            BuildSkillWorkPhases();
        }

        /// <summary>
        /// 构建技能工作分配阶段列表。
        /// 顺序：重要专业 → 普通专业 → 次级专业 → 研究
        /// 手工类（Smithing/Tailoring/Crafting）使用组分配：一次排序、同时分配、共享 1 个 workCount，
        /// 避免分三次排序导致 workCount 变化影响后续排序、手工工作分散给不同人。
        /// </summary>
        private static void BuildSkillWorkPhases()
        {
            skillWorkPhases = new List<WorkPhase>();

            // 1. 重要专业工作（Doctor/Warden/Childcare/Cooking/PlantCutting）：保底2，三因素高者1低者3
            for (int i = 0; i < keyWorkDefs.Count; i++)
                skillWorkPhases.Add(new WorkPhase { WorkType = keyWorkDefs[i], Config = KeyWorkConfig });
            if (cachedCookingDef != null)
                skillWorkPhases.Add(new WorkPhase { WorkType = cachedCookingDef, Config = KeyWorkConfig });
            if (cachedPlantCuttingDef != null)
                skillWorkPhases.Add(new WorkPhase { WorkType = cachedPlantCuttingDef, Config = KeyWorkConfig });

            // 2. 普通专业工作（Construction/Mining/Growing/Smithing/Tailoring/Crafting/Art）：保底2，三因素高者2低者3
            // 手工组（Smithing/Tailoring/Crafting）使用组分配（共享 Crafting 技能 + 1 workCount）
            List<WorkTypeDef> craftingGroup = new List<WorkTypeDef>();
            for (int i = 0; i < otherSkillWorkDefs.Count; i++)
            {
                WorkTypeDef wt = otherSkillWorkDefs[i];
                if (IsCraftingSkillWork(wt))
                    craftingGroup.Add(wt);
                else
                    skillWorkPhases.Add(new WorkPhase { WorkType = wt, Config = OtherSkillConfig });
            }
            if (craftingGroup.Count > 0)
                skillWorkPhases.Add(new WorkPhase { WorkTypes = craftingGroup, Config = OtherSkillConfig });

            // 3. 次级专业工作（Handling/Fishing/Hunting）：保底1，三因素高者2低者4
            // Hunting 需远程武器+后排排序；Handling/Fishing 不需要（Fishing 关联 Animals 技能，非远程武器工作）
            if (cachedHandlingDef != null)
                skillWorkPhases.Add(new WorkPhase { WorkType = cachedHandlingDef, Config = SecondaryConfig });
            if (cachedFishingDef != null)
                skillWorkPhases.Add(new WorkPhase { WorkType = cachedFishingDef, Config = SecondaryConfig });
            if (cachedHuntingDef != null)
                skillWorkPhases.Add(new WorkPhase { WorkType = cachedHuntingDef, Config = SecondaryRangedConfig });

            // 4. 研究工作（Research/DarkStudy）：保底1，专业工作<3的三因素最高者1，其他有火者3
            // 最后分配，让其他专家先累加 workCount，研究时硬上限拦截
            if (cachedResearchDef != null)
                skillWorkPhases.Add(new WorkPhase { WorkType = cachedResearchDef, Config = ResearchConfig });
            if (cachedDarkStudyDef != null)
                skillWorkPhases.Add(new WorkPhase { WorkType = cachedDarkStudyDef, Config = ResearchConfig });

            // 调试：dump 阶段列表，确认所有工作类型（如 Mining）被正确纳入
            AEDebug.Log(() =>
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("[WorkAllocator] Phases(").Append(skillWorkPhases.Count).Append("): ");
                for (int i = 0; i < skillWorkPhases.Count; i++)
                {
                    if (i > 0) sb.Append(" → ");
                    WorkPhase p = skillWorkPhases[i];
                    if (p.WorkTypes != null)
                    {
                        for (int j = 0; j < p.WorkTypes.Count; j++)
                        {
                            if (j > 0) sb.Append('+');
                            sb.Append(p.WorkTypes[j].defName);
                        }
                    }
                    else
                    {
                        sb.Append(p.WorkType.defName);
                    }
                }
                return sb.ToString();
            });
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

        /// <summary>
        /// 判断工作类型是否关联 Crafting 技能（Crafting/Smithing/Tailoring 共享同一技能）。
        /// </summary>
        private static bool IsCraftingSkillWork(WorkTypeDef wt)
        {
            return wt.relevantSkills != null && wt.relevantSkills.Contains(SkillDefOf.Crafting);
        }

        /// <summary>
        /// 构造工作类型组标签（如 Smithing+Tailoring+Crafting），用于调试日志。
        /// 仅在 debug 开启时调用。
        /// </summary>
        private static string BuildWorkGroupLabel(List<WorkTypeDef> workTypes)
        {
            StringBuilder lb = new StringBuilder();
            for (int j = 0; j < workTypes.Count; j++)
            {
                if (j > 0) lb.Append('+');
                lb.Append(workTypes[j].defName);
            }
            return lb.ToString();
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

            // 预计算评级缓存，避免重复调用 GetCombatTier
            tierCache.Clear();
            for (int i = 0; i < workCandidates.Count; i++)
            {
                Pawn p = workCandidates[i];
                tierCache[p] = CombatEvaluator.GetCombatTier(p);
            }

            // 新规则：评级 S+→4，A→3，其余→1（无需排序，直接按评级分档）
            for (int i = 0; i < workCandidates.Count; i++)
            {
                Pawn pawn = workCandidates[i];
                CombatTier tier = tierCache[pawn];
                int priority;
                switch (tier)
                {
                    case CombatTier.SSS:
                    case CombatTier.SS:
                    case CombatTier.S: priority = 4; break;
                    case CombatTier.A: priority = 3; break;
                    default: priority = 1; break; // B/C/D/X
                }
                // 豁免：搬运 priority=1 会先于研究(priority>=2)执行，打断研究；研究员的搬运降为4
                if (priority == 1 && workType.defName == "Hauling" && GetMaxResearchPriority(pawn) >= 2)
                {
                    priority = 4;
                }
                // 豁免：清洁 priority=1 会先于研究(priority>=2)执行，打断研究；研究员的清洁降为4
                if (priority == 1 && workType.defName == "Cleaning" && GetMaxResearchPriority(pawn) >= 2)
                {
                    priority = 4;
                }

                pawn.workSettings.SetPriority(workType, priority);
                // 辅助工作不计入 workCount
            }
        }

        /// <summary>
        /// 返回 Pawn 当前 Research/DarkStudy 工作优先级的最大值（0 表示未承担研究）。
        /// 用于辅助工作豁免判定：研究优先级>=2 时搬运降级，避免打断研究。
        /// </summary>
        private static int GetMaxResearchPriority(Pawn pawn)
        {
            int p = 0;
            if (cachedResearchDef != null)
                p = Math.Max(p, pawn.workSettings.GetPriority(cachedResearchDef));
            if (cachedDarkStudyDef != null)
                p = Math.Max(p, pawn.workSettings.GetPriority(cachedDarkStudyDef));
            return p;
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
        /// </summary>
        private static void AssignWorkType(WorkTypeDef workType, WorkAllocationConfig config)
        {
            // 候选收集：包含满载者，让满载者参与排序
            // 满载者在 top N 内走 Floor 保底（不抢占 Guarantee），无火者落在 top N 外走 Floor(=0)
            // 避免满载者被跳过后，候选无火者错误获得 Guarantee 保底导致重复承担（保底人数超标）
            workCandidates.Clear();
            for (int i = 0; i < candidatePawns.Count; i++)
            {
                Pawn pawn = candidatePawns[i];
                if (pawn.WorkTagIsDisabled(workType.workTags)) continue;
                // 狩猎类需远程武器：避免无远程武器者被分配
                if (config.RequireRangedWeapon && pawn.equipment?.Primary?.def.IsRangedWeapon != true) continue;
                workCandidates.Add(pawn);
            }
            // 回退放宽：候选不足保证人数时（小殖民地人手不足），满载者走 Guarantee 逻辑保证保底
            // 候选已包含满载者，无需重新收集，仅设标志
            bool fallbackRelaxed = workCandidates.Count < config.GuaranteeCount;
            if (workCandidates.Count == 0)
            {
                AEDebug.Log(() => $"[WorkAllocator] {workType.defName}: SKIP (no candidates, total={candidatePawns.Count})");
                return;
            }

            AEDebug.Log(() =>
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("[WorkAllocator] ").Append(workType.defName)
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
                workCandidates.Sort((a, b) => ComparePawnsForHunting(a, b, workType.relevantSkills));
            }
            else
            {
                workCandidates.Sort((a, b) => ComparePawnsByPassionWorkCountSkill(a, b, workType.relevantSkills));
            }

            // 调试：dump 排序后候选清单（passion/skill/workCount）
            AEDebug.Log(() =>
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("[WorkAllocator] ").Append(workType.defName).Append(" sorted: ");
                for (int i = 0; i < workCandidates.Count; i++)
                {
                    Pawn p = workCandidates[i];
                    if (i > 0) sb.Append(", ");
                    sb.Append(AEDebug.Label(p))
                      .Append("[p=").Append(GetMaxPassionForSkills(p, workType.relevantSkills))
                      .Append(" s=").Append(GetMaxSkillLevelForSkills(p, workType.relevantSkills))
                      .Append(" wc=").Append(workCount[p])
                      .Append(workCount[p] >= MaxCoreWorkCount ? "!" : "")
                      .Append(']');
                }
                return sb.ToString();
            });

            // 按配置分配优先级
            for (int i = 0; i < workCandidates.Count; i++)
            {
                Pawn pawn = workCandidates[i];
                int passionLevel = GetMaxPassionForSkills(pawn, workType.relevantSkills);
                // 满载者（回退放宽加入的）不抢占 Guarantee 优先级，只给 Floor 保底
                // 避免工作很多的专家被回退放宽后仍获得 priority=1
                // 例外：回退放宽模式（人手不足）下，满载者必须走 Guarantee 逻辑保证保底人数，
                // 否则无火满载者会走 Floor(=0) 导致 GuaranteeCount 保底失效
                bool isOverloaded = workCount[pawn] >= MaxCoreWorkCount;
                int priority;

                if (i < config.GuaranteeCount && (!isOverloaded || fallbackRelaxed))
                {
                    // 原则 1+2：保证 N 人承担（仅未满载者），双火/单火/无火分别给优先级
                    if (passionLevel >= (int)Passion.Major)
                        priority = config.GuaranteeMajorPriority;
                    else if (passionLevel >= (int)Passion.Minor)
                        priority = config.GuaranteeMinorPriority;
                    else
                        priority = config.GuaranteeNonPassionatePriority;
                }
                else
                {
                    // 原则 3+4：超出 guarantee 或满载者降级，双火/单火/无火分别给 Floor 保底
                    if (passionLevel >= (int)Passion.Major)
                        priority = config.FloorMajorPriority;
                    else if (passionLevel >= (int)Passion.Minor)
                        priority = config.FloorMinorPriority;
                    else
                        priority = config.FloorNonPassionatePriority;
                }

                // 技能等级保底：相关技能≥8 者 priority 不低于 3
                priority = ApplySkillFloor(priority, pawn, workType.relevantSkills);

                pawn.workSettings.SetPriority(workType, priority);

                // 调试：记录每个候选的最终优先级与归类（GM=Guarantee双火/Gi=Guarantee单火/FM=Floor双火/Fi=Floor单火/N=无火）
                AEDebug.Log(() =>
                {
                    int finalIdx = i;
                    string bucket;
                    if (finalIdx < config.GuaranteeCount && (!isOverloaded || fallbackRelaxed))
                    {
                        bucket = passionLevel >= (int)Passion.Major ? "GM"
                            : (passionLevel >= (int)Passion.Minor ? "Gi" : "N");
                    }
                    else
                    {
                        bucket = passionLevel >= (int)Passion.Major ? "FM"
                            : (passionLevel >= (int)Passion.Minor ? "Fi" : "N");
                    }
                    int wcBefore = workCount[pawn];
                    return $"[WorkAllocator] {workType.defName}[{finalIdx}] {AEDebug.Label(pawn)} p={passionLevel} s={GetMaxSkillLevelForSkills(pawn, workType.relevantSkills)} wc={wcBefore}{(isOverloaded ? "!" : "")} [{bucket}] → prio={priority}{(priority <= 2 ? " (+wc)" : "")}";
                });

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
                if (pawn.WorkTagIsDisabled(workType.workTags))
                {
                    pawn.workSettings.SetPriority(workType, 0);
                    continue;
                }
                if (config.RequireRangedWeapon && pawn.equipment?.Primary?.def.IsRangedWeapon != true)
                {
                    pawn.workSettings.SetPriority(workType, 0);
                    continue;
                }

                // 硬上限跳过的满载者：有火者给 Floor 保底，无火者给0
                int passionLevel = GetMaxPassionForSkills(pawn, workType.relevantSkills);
                int priority;
                if (passionLevel >= (int)Passion.Major)
                    priority = config.FloorMajorPriority;
                else if (passionLevel >= (int)Passion.Minor)
                    priority = config.FloorMinorPriority;
                else
                    priority = 0;
                // 技能等级保底：相关技能≥8 者 priority 不低于 3
                priority = ApplySkillFloor(priority, pawn, workType.relevantSkills);
                pawn.workSettings.SetPriority(workType, priority);
            }
        }

        /// <summary>
        /// 工作类型组分配：一次排序、同时为所有工作类型分配相同优先级、共享 1 个 workCount。
        /// 用于手工类（Smithing/Tailoring/Crafting）：三者关联同一 Crafting 技能，
        /// 应视为 1 个专业工作，避免分三次排序导致 workCount 变化影响后续排序、手工工作分散给不同人。
        /// 实现复用 AssignWorkType 的候选收集/排序/优先级逻辑，仅末尾同时 SetPriority 多个工作类型。
        /// </summary>
        private static void AssignWorkGroup(List<WorkTypeDef> workTypes, WorkAllocationConfig config)
        {
            // 手工类三者 workTags 相同，用第一个过滤即可
            WorkTypeDef firstWork = workTypes[0];
            List<SkillDef> skills = firstWork.relevantSkills;

            // 调试：构造工作类型组标签（如 Smithing+Tailoring+Crafting），仅在 debug 开启时构造
            string groupLabel = AEDebug.IsActive ? BuildWorkGroupLabel(workTypes) : null;

            // 候选收集：包含满载者，让满载者参与排序（与 AssignWorkType 一致）
            workCandidates.Clear();
            for (int i = 0; i < candidatePawns.Count; i++)
            {
                Pawn pawn = candidatePawns[i];
                if (pawn.WorkTagIsDisabled(firstWork.workTags)) continue;
                if (config.RequireRangedWeapon && pawn.equipment?.Primary?.def.IsRangedWeapon != true) continue;
                workCandidates.Add(pawn);
            }
            // 回退放宽：候选不足保证人数时，满载者走 Guarantee 逻辑保证保底
            bool fallbackRelaxed = workCandidates.Count < config.GuaranteeCount;
            if (workCandidates.Count == 0)
            {
                AEDebug.Log(() => $"[WorkAllocator] {groupLabel ?? "Group"}: SKIP (no candidates, total={candidatePawns.Count})");
                return;
            }

            AEDebug.Log(() =>
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("[WorkAllocator] ").Append(groupLabel)
                  .Append(": candidates=").Append(workCandidates.Count).Append('/').Append(candidatePawns.Count)
                  .Append(" guarantee=").Append(config.GuaranteeCount);
                if (fallbackRelaxed) sb.Append(" [FALLBACK RELAXED]");
                return sb.ToString();
            });

            // 排序：passion desc → skill desc → workCount asc（手工类无需后排排序）
            workCandidates.Sort((a, b) => ComparePawnsByPassionWorkCountSkill(a, b, skills));

            // 调试：dump 排序后候选清单
            AEDebug.Log(() =>
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

            // 按配置分配优先级：对所有工作类型设置相同优先级
            for (int i = 0; i < workCandidates.Count; i++)
            {
                Pawn pawn = workCandidates[i];
                int passionLevel = GetMaxPassionForSkills(pawn, skills);
                bool isOverloaded = workCount[pawn] >= MaxCoreWorkCount;
                int priority;

                if (i < config.GuaranteeCount && (!isOverloaded || fallbackRelaxed))
                {
                    if (passionLevel >= (int)Passion.Major)
                        priority = config.GuaranteeMajorPriority;
                    else if (passionLevel >= (int)Passion.Minor)
                        priority = config.GuaranteeMinorPriority;
                    else
                        priority = config.GuaranteeNonPassionatePriority;
                }
                else
                {
                    if (passionLevel >= (int)Passion.Major)
                        priority = config.FloorMajorPriority;
                    else if (passionLevel >= (int)Passion.Minor)
                        priority = config.FloorMinorPriority;
                    else
                        priority = config.FloorNonPassionatePriority;
                }

                // 技能等级保底：相关技能≥8 者 priority 不低于 3
                priority = ApplySkillFloor(priority, pawn, skills);

                // 对所有工作类型设置相同优先级（一次排序结果同时分配）
                for (int j = 0; j < workTypes.Count; j++)
                {
                    pawn.workSettings.SetPriority(workTypes[j], priority);
                }

                // 调试：记录每个候选的最终优先级（GM=Guarantee双火/Gi=Guarantee单火/FM=Floor双火/Fi=Floor单火/N=无火）
                AEDebug.Log(() =>
                {
                    int finalIdx = i;
                    string bucket;
                    if (finalIdx < config.GuaranteeCount && (!isOverloaded || fallbackRelaxed))
                    {
                        bucket = passionLevel >= (int)Passion.Major ? "GM"
                            : (passionLevel >= (int)Passion.Minor ? "Gi" : "N");
                    }
                    else
                    {
                        bucket = passionLevel >= (int)Passion.Major ? "FM"
                            : (passionLevel >= (int)Passion.Minor ? "Fi" : "N");
                    }
                    int wcBefore = workCount[pawn];
                    return $"[WorkAllocator] {groupLabel}[{finalIdx}] {AEDebug.Label(pawn)} p={passionLevel} s={GetMaxSkillLevelForSkills(pawn, skills)} wc={wcBefore}{(isOverloaded ? "!" : "")} [{bucket}] → prio={priority}{(priority <= 2 ? " (+wc)" : "")}";
                });

                // 整组只加 1 个 workCount（手工类视为 1 个专业工作）
                if (priority <= 2)
                {
                    workCount[pawn]++;
                }
            }

            // 末尾清理非候选的所有 workTypes 旧优先级（替代全局清零）
            // 硬上限跳过的满载者：有火者给 Floor 保底，无火者/WorkTagIsDisabled/无远程武器者给0
            inWorkCandidates.Clear();
            for (int i = 0; i < workCandidates.Count; i++)
            {
                inWorkCandidates.Add(workCandidates[i]);
            }
            for (int i = 0; i < candidatePawns.Count; i++)
            {
                Pawn pawn = candidatePawns[i];
                if (inWorkCandidates.Contains(pawn)) continue;

                // WorkTagIsDisabled 或无远程武器者给0
                if (pawn.WorkTagIsDisabled(firstWork.workTags))
                {
                    for (int j = 0; j < workTypes.Count; j++)
                        pawn.workSettings.SetPriority(workTypes[j], 0);
                    continue;
                }
                if (config.RequireRangedWeapon && pawn.equipment?.Primary?.def.IsRangedWeapon != true)
                {
                    for (int j = 0; j < workTypes.Count; j++)
                        pawn.workSettings.SetPriority(workTypes[j], 0);
                    continue;
                }

                // 硬上限跳过的满载者：有火者给 Floor 保底，无火者给0
                int passionLevel = GetMaxPassionForSkills(pawn, skills);
                int priority;
                if (passionLevel >= (int)Passion.Major)
                    priority = config.FloorMajorPriority;
                else if (passionLevel >= (int)Passion.Minor)
                    priority = config.FloorMinorPriority;
                else
                    priority = 0;
                // 技能等级保底：相关技能≥8 者 priority 不低于 3
                priority = ApplySkillFloor(priority, pawn, skills);
                for (int j = 0; j < workTypes.Count; j++)
                    pawn.workSettings.SetPriority(workTypes[j], priority);
            }
        }

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
        /// 返回该 Pawn 在指定技能集上的最高 Passion 量化值（None=0, Minor=1, Major=2）。
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
                int v = (int)sr.passion;
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
        /// 技能等级保底：相关技能≥8 者 priority 不低于 3（即 priority 为 0 或 >3 时提升到 3）。
        /// 设计意图：高技能无火者仍以 priority=3 参与工作，不被完全排除。
        /// 注意：priority=1/2/3 时不触发保底（已满足"不低于 3"）。
        /// </summary>
        private static int ApplySkillFloor(int priority, Pawn pawn, List<SkillDef> skills)
        {
            if (priority == 0 || priority > 3)
            {
                if (GetMaxSkillLevelForSkills(pawn, skills) >= 8)
                    return 3;
            }
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
