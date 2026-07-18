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
    /// 所有技能类工作复用统一 AssignWork + WorkAllocationConfig 四大原则分配，
    /// 单工作与组分配（手工类 Smithing/Tailoring/Crafting 共享 1 个 workCount）走同一路径。
    /// 触发方式：ITab 底部"全局工作重配"按钮手动调用。
    /// </summary>
    public static partial class WorkAllocator
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
            // 工作类型集合：单工作用 1 元素数组，组分配用多元素数组
            // 统一为数组消除 AssignWorkType/AssignWorkGroup 分支，组分配共享 1 个 workCount
            public WorkTypeDef[] WorkTypes;
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

            // 2. 收集候选殖民者
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

            if (AEDebug.IsActive) AEDebug.Log(() => $"[WorkAllocator] === ReallocateAll start (pawns={candidatePawns.Count}) ===");

            // 5. 多遍分配（顺序严格固定，前排分配结果影响后排候选排序）
            // 阶段列表驱动：紧急 → 重要专业 → 普通专业 → 次级专业 → 研究
            // 手工类使用组分配：Smithing/Tailoring/Crafting 一次排序同时分配，共享 1 个 workCount
            AssignEmergencyPriorities();          // 第 1 遍：紧急工作
            for (int i = 0; i < skillWorkPhases.Count; i++)  // 第 2-N 遍：技能工作
            {
                WorkPhase phase = skillWorkPhases[i];
                AssignWork(phase.WorkTypes, phase.Config);
            }
            AssignServiceWorkPriorities();        // 最后一遍：辅助工作（搬运/清洁/非技能）

            // 调试：dump 最终 workCount，便于分析硬上限拦截是否影响双火 pawn
            if (AEDebug.IsActive) AEDebug.Log(() =>
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
                skillWorkPhases.Add(new WorkPhase { WorkTypes = new[] { keyWorkDefs[i] }, Config = KeyWorkConfig });
            if (cachedCookingDef != null)
                skillWorkPhases.Add(new WorkPhase { WorkTypes = new[] { cachedCookingDef }, Config = KeyWorkConfig });
            if (cachedPlantCuttingDef != null)
                skillWorkPhases.Add(new WorkPhase { WorkTypes = new[] { cachedPlantCuttingDef }, Config = KeyWorkConfig });

            // 2. 普通专业工作（Construction/Mining/Growing/Smithing/Tailoring/Crafting/Art）：保底2，三因素高者2低者3
            // 手工组（Smithing/Tailoring/Crafting）使用组分配（共享 Crafting 技能 + 1 workCount）
            List<WorkTypeDef> craftingGroup = new List<WorkTypeDef>();
            for (int i = 0; i < otherSkillWorkDefs.Count; i++)
            {
                WorkTypeDef wt = otherSkillWorkDefs[i];
                if (IsCraftingSkillWork(wt))
                    craftingGroup.Add(wt);
                else
                    skillWorkPhases.Add(new WorkPhase { WorkTypes = new[] { wt }, Config = OtherSkillConfig });
            }
            if (craftingGroup.Count > 0)
                skillWorkPhases.Add(new WorkPhase { WorkTypes = craftingGroup.ToArray(), Config = OtherSkillConfig });

            // 3. 次级专业工作（Handling/Fishing/Hunting）：保底1，三因素高者2低者4
            // Hunting 需远程武器+后排排序；Handling/Fishing 不需要（Fishing 关联 Animals 技能，非远程武器工作）
            if (cachedHandlingDef != null)
                skillWorkPhases.Add(new WorkPhase { WorkTypes = new[] { cachedHandlingDef }, Config = SecondaryConfig });
            if (cachedFishingDef != null)
                skillWorkPhases.Add(new WorkPhase { WorkTypes = new[] { cachedFishingDef }, Config = SecondaryConfig });
            if (cachedHuntingDef != null)
                skillWorkPhases.Add(new WorkPhase { WorkTypes = new[] { cachedHuntingDef }, Config = SecondaryRangedConfig });

            // 4. 研究工作（Research/DarkStudy）：保底1，专业工作<3的三因素最高者1，其他有火者3
            // 最后分配，让其他专家先累加 workCount，研究时硬上限拦截
            if (cachedResearchDef != null)
                skillWorkPhases.Add(new WorkPhase { WorkTypes = new[] { cachedResearchDef }, Config = ResearchConfig });
            if (cachedDarkStudyDef != null)
                skillWorkPhases.Add(new WorkPhase { WorkTypes = new[] { cachedDarkStudyDef }, Config = ResearchConfig });

            // 调试：dump 阶段列表，确认所有工作类型（如 Mining）被正确纳入
            if (AEDebug.IsActive) AEDebug.Log(() =>
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("[WorkAllocator] Phases(").Append(skillWorkPhases.Count).Append("): ");
                for (int i = 0; i < skillWorkPhases.Count; i++)
                {
                    if (i > 0) sb.Append(" → ");
                    WorkPhase p = skillWorkPhases[i];
                    for (int j = 0; j < p.WorkTypes.Length; j++)
                    {
                        if (j > 0) sb.Append('+');
                        sb.Append(p.WorkTypes[j].defName);
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

        /// <summary>
        /// 判断工作类型是否关联 Crafting 技能（Crafting/Smithing/Tailoring 共享同一技能）。
        /// </summary>
        private static bool IsCraftingSkillWork(WorkTypeDef wt)
        {
            return wt.relevantSkills != null && wt.relevantSkills.Contains(SkillDefOf.Crafting);
        }

        /// <summary>
        /// 构造工作类型组标签（如 Smithing+Tailoring+Crafting，或单工作 Doctor），用于调试日志。
        /// 仅在 debug 开启时调用。
        /// </summary>
        private static string BuildWorkGroupLabel(IReadOnlyList<WorkTypeDef> workTypes)
        {
            StringBuilder lb = new StringBuilder();
            for (int j = 0; j < workTypes.Count; j++)
            {
                if (j > 0) lb.Append('+');
                lb.Append(workTypes[j].defName);
            }
            return lb.ToString();
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

    }
}
