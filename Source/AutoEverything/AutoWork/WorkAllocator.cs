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
    /// 按工作分类（紧急/关键/狩猎类/研究/普通技能/杂物/非技能）多遍协调分配，
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

        // 服务类工作排序缓存：CombatTier 预计算，避免 Sort 比较器内重复调用 GetCombatTier
        // 每次服务类分配前 Clear+重填，仅在该次 Sort 内有效
        private static readonly Dictionary<Pawn, CombatTier> tierCache = new Dictionary<Pawn, CombatTier>();

        // WorkTypeDef 缓存与分类（懒加载，避免静态字段初始化器跨线程调用 DefDatabase）
        private static List<WorkTypeDef> cachedWorkTypes;
        private static readonly List<WorkTypeDef> keyWorkDefs = new List<WorkTypeDef>();
        private static readonly List<WorkTypeDef> otherSkillWorkDefs = new List<WorkTypeDef>();
        private static WorkTypeDef cachedHuntingDef;
        private static WorkTypeDef cachedFishingDef;
        private static WorkTypeDef cachedResearchDef;
        private static WorkTypeDef cachedPlantCuttingDef;
        private static WorkTypeDef cachedGrowingDef;

        /// <summary>
        /// 工作分配配置：统一描述各工作类型的优先级规则，编码四大原则。
        /// 1. GuaranteeCount：保证至少 N 人承担（无论有无火），top N 内有火/无火分别给 GuaranteePassionate/GuaranteeNonPassionate
        /// 2. 三因子排序选 guarantee 人选（passion desc → skill desc → workCount asc）
        /// 3. FloorPassionatePriority：有火者超出 guarantee 部分至少给此保底优先级
        /// 4. UseSkillFloorForNonPassionate：无火者超出 guarantee 部分按技能兜底（≥12→2, ≥8→3, 否则 FloorNonPassionatePriority）
        /// 注：奴隶在专业工作中与殖民者同流程（按兴趣/技能参与分配），
        /// 奴隶的特殊处理仅在服务类工作（搬运/清洁/非技能）中生效，见 AssignServiceWorkType。
        /// </summary>
        private struct WorkAllocationConfig
        {
            public int GuaranteeCount;                  // 保证人数
            public int GuaranteePassionatePriority;     // top N 内有火优先级
            public int GuaranteeNonPassionatePriority;  // top N 内无火优先级
            public int FloorPassionatePriority;         // 超出 top N 的有火保底优先级
            public bool UseSkillFloorForNonPassionate;  // 超出 top N 的无火者是否启用技能兜底
            public int FloorNonPassionatePriority;      // 超出 top N 的无火者优先级（不启用技能兜底时直接用）
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
                    candidatePawns.Add(pawn);
                }
                // 奴隶（Biotech DLC 才有，无 DLC 时 SlavesOfColonySpawned 返回空列表）
                foreach (Pawn pawn in map.mapPawns.SlavesOfColonySpawned)
                {
                    if (DLCCompat.IsGhoul(pawn)) continue;
                    if (!PawnSuitabilityChecker.CanManageGear(pawn)) continue;
                    if (pawn.Dead || pawn.Downed) continue;
                    if (pawn.workSettings == null) continue;
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
            AssignHuntingPriorities();            // 第 3 遍：狩猎类（Hunting/Fishing/PlantCutting/Growing）
            AssignResearchPriorities();           // 第 4 遍：研究
            AssignOtherSkillWorkPriorities();     // 第 5 遍：其他技能工作
            AssignServiceWorkPriorities();        // 第 6 遍：服务类（搬运/清洁/非技能）

            return candidatePawns.Count;
        }

        /// <summary>
        /// 懒加载并按 defName/WorkTags 分类 WorkTypeDef（仅执行一次）。
        /// 分类：紧急（运行时判定）/ 关键 / 狩猎类 / 研究 / 普通技能 / 杂务 / 非技能。
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

                // 狩猎类：Hunting + Fishing + PlantCutting + Growing
                if (wt.defName == "Hunting") { cachedHuntingDef = wt; continue; }
                if (wt.defName == "Fishing") { cachedFishingDef = wt; continue; }
                if (wt.defName == "PlantCutting") { cachedPlantCuttingDef = wt; continue; }
                if (wt.defName == "Growing") { cachedGrowingDef = wt; continue; }

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
        //   保证 2 人，有火 top2→1，无火 top2→3，有火保底 3，无火技能兜底
        // ════════════════════════════════════════════════════════════

        private static void AssignKeyWorkPriorities()
        {
            WorkAllocationConfig config = new WorkAllocationConfig
            {
                GuaranteeCount = 2,
                GuaranteePassionatePriority = 1,
                GuaranteeNonPassionatePriority = 3,
                FloorPassionatePriority = 3,
                UseSkillFloorForNonPassionate = true,
                FloorNonPassionatePriority = 0,
                RequireRangedWeapon = false,
                UseBackRowSort = false
            };
            for (int i = 0; i < keyWorkDefs.Count; i++)
            {
                AssignWorkType(keyWorkDefs[i], config);
            }
        }

        // ════════════════════════════════════════════════════════════
        // 第 3 遍：狩猎类（Hunting/Fishing/PlantCutting/Growing）
        //   Hunting：需远程武器+后排排序，保证 2 人 top2→2，有火保底 4，无火技能兜底
        //   Fishing：需远程武器+后排排序，保证 2 人 top2→3，有火保底 3，无火技能兜底
        //   PlantCutting：保证 2 人，有火 top2→1，有火保底 3，无火→0
        //   Growing：保证 2 人，有火 top2→2，有火保底 3，无火→0
        // ════════════════════════════════════════════════════════════

        private static void AssignHuntingPriorities()
        {
            // Hunting：需远程武器 + 后排排序，top2→2，有火保底 4，无火技能兜底
            WorkAllocationConfig huntingConfig = new WorkAllocationConfig
            {
                GuaranteeCount = 2,
                GuaranteePassionatePriority = 2,
                GuaranteeNonPassionatePriority = 2,
                FloorPassionatePriority = 4,
                UseSkillFloorForNonPassionate = true,
                FloorNonPassionatePriority = 0,
                RequireRangedWeapon = true,
                UseBackRowSort = true
            };
            if (cachedHuntingDef != null)
                AssignWorkType(cachedHuntingDef, huntingConfig);

            // Fishing：需远程武器 + 后排排序，top2→3，有火保底 3，无火技能兜底
            WorkAllocationConfig fishingConfig = new WorkAllocationConfig
            {
                GuaranteeCount = 2,
                GuaranteePassionatePriority = 3,
                GuaranteeNonPassionatePriority = 3,
                FloorPassionatePriority = 3,
                UseSkillFloorForNonPassionate = true,
                FloorNonPassionatePriority = 0,
                RequireRangedWeapon = true,
                UseBackRowSort = true
            };
            if (cachedFishingDef != null)
                AssignWorkType(cachedFishingDef, fishingConfig);

            // PlantCutting（割除）：有火 top2→1，有火保底 3，无火→0
            WorkAllocationConfig cuttingConfig = new WorkAllocationConfig
            {
                GuaranteeCount = 2,
                GuaranteePassionatePriority = 1,
                GuaranteeNonPassionatePriority = 0,
                FloorPassionatePriority = 3,
                UseSkillFloorForNonPassionate = false,
                FloorNonPassionatePriority = 0,
                RequireRangedWeapon = false,
                UseBackRowSort = false
            };
            if (cachedPlantCuttingDef != null)
                AssignWorkType(cachedPlantCuttingDef, cuttingConfig);

            // Growing（种植）：有火 top2→2，有火保底 3，无火→0
            WorkAllocationConfig growingConfig = new WorkAllocationConfig
            {
                GuaranteeCount = 2,
                GuaranteePassionatePriority = 2,
                GuaranteeNonPassionatePriority = 0,
                FloorPassionatePriority = 3,
                UseSkillFloorForNonPassionate = false,
                FloorNonPassionatePriority = 0,
                RequireRangedWeapon = false,
                UseBackRowSort = false
            };
            if (cachedGrowingDef != null)
                AssignWorkType(cachedGrowingDef, growingConfig);
        }

        // ════════════════════════════════════════════════════════════
        // 第 4 遍：研究
        //   保证 1 人，top1→2，有火保底 4，无火技能兜底
        // ════════════════════════════════════════════════════════════

        private static void AssignResearchPriorities()
        {
            if (cachedResearchDef == null) return;
            WorkAllocationConfig config = new WorkAllocationConfig
            {
                GuaranteeCount = 1,
                GuaranteePassionatePriority = 2,
                GuaranteeNonPassionatePriority = 2,
                FloorPassionatePriority = 4,
                UseSkillFloorForNonPassionate = true,
                FloorNonPassionatePriority = 0,
                RequireRangedWeapon = false,
                UseBackRowSort = false
            };
            AssignWorkType(cachedResearchDef, config);
        }

        // ════════════════════════════════════════════════════════════
        // 第 5 遍：其他技能工作（Cooking/Mining/Crafting/Construction 等）
        //   保证 2 人，有火 top2→2，无火 top2→3，有火保底 3，无火技能兜底
        // ════════════════════════════════════════════════════════════

        private static void AssignOtherSkillWorkPriorities()
        {
            WorkAllocationConfig config = new WorkAllocationConfig
            {
                GuaranteeCount = 2,
                GuaranteePassionatePriority = 2,
                GuaranteeNonPassionatePriority = 3,
                FloorPassionatePriority = 3,
                UseSkillFloorForNonPassionate = true,
                FloorNonPassionatePriority = 0,
                RequireRangedWeapon = false,
                UseBackRowSort = false
            };
            for (int i = 0; i < otherSkillWorkDefs.Count; i++)
            {
                AssignWorkType(otherSkillWorkDefs[i], config);
            }
        }

        // ════════════════════════════════════════════════════════════
        // 第 6 遍：服务类工作（搬运/清洁/非技能 BasicWorker 等，不计入 workCount）
        //   排序：奴隶优先 → 评级升序（最低档在前）→ 工作计数升序
        //   规则：1.奴隶均 priority=1
        //         2.保底 1 人 priority=1（排序首位非奴隶，即评级最低者）
        //         3.工作计数 < 3 的 priority=1（均衡负载）
        //         4.以上均不满足时按评级分档（S+=4, A/B/C=3, D/X=1）
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

            // 预计算评级缓存，避免 Sort 比较器内重复调用 GetCombatTier
            tierCache.Clear();
            for (int i = 0; i < workCandidates.Count; i++)
            {
                Pawn p = workCandidates[i];
                tierCache[p] = CombatEvaluator.GetCombatTier(p);
            }

            // 排序：奴隶优先 → 评级升序（最低档在前）→ 工作计数升序
            workCandidates.Sort(ComparePawnsForServiceWork);

            bool guaranteedOne = false;
            for (int i = 0; i < workCandidates.Count; i++)
            {
                Pawn pawn = workCandidates[i];
                int priority;

                if (DLCCompat.IsSlave(pawn))
                {
                    // 规则 1：奴隶均 priority=1
                    priority = 1;
                    guaranteedOne = true;
                }
                else if (!guaranteedOne)
                {
                    // 规则 2+4：保底 1 人 priority=1（排序首位非奴隶，即评级最低者）
                    priority = 1;
                    guaranteedOne = true;
                }
                else if (workCount[pawn] < 3)
                {
                    // 规则 3：其他优先 1/2 工作数量少于 3 的 priority=1（均衡负载）
                    priority = 1;
                }
                else
                {
                    // 兜底：按评级分档（高价值殖民者少做服务类工作）
                    CombatTier tier = tierCache[pawn];
                    switch (tier)
                    {
                        case CombatTier.SSS:
                        case CombatTier.SS:
                        case CombatTier.S: priority = 4; break;
                        case CombatTier.A:
                        case CombatTier.B:
                        case CombatTier.C: priority = 3; break;
                        default: priority = 1; break; // D/X
                    }
                }

                pawn.workSettings.SetPriority(workType, priority);
                // 服务类工作不计入 workCount
            }
        }

        /// <summary>
        /// 服务类工作专用比较器：奴隶优先 → 评级升序（最低档在前）→ 工作计数升序。
        /// 设计意图：奴隶承担服务类工作（受限工作类型多），
        ///   评级低者优先（高价值殖民者应专注技能工作），
        ///   工作计数少者优先（均衡负载）。
        /// 注：评级结果由调用方预计算存入 tierCache，避免比较器内重复调用 GetCombatTier。
        /// </summary>
        private static int ComparePawnsForServiceWork(Pawn a, Pawn b)
        {
            // 1. 奴隶优先（true 排前）
            bool slaveA = DLCCompat.IsSlave(a);
            bool slaveB = DLCCompat.IsSlave(b);
            if (slaveA != slaveB) return slaveB.CompareTo(slaveA);

            // 2. 评级升序（最低档在前，X=0 → SSS=7）
            CombatTier tierA = tierCache.TryGetValue(a, out CombatTier ta) ? ta : CombatTier.X;
            CombatTier tierB = tierCache.TryGetValue(b, out CombatTier tb) ? tb : CombatTier.X;
            int tierCompare = ((int)tierA).CompareTo((int)tierB);
            if (tierCompare != 0) return tierCompare;

            // 3. 工作计数升序（工作少者优先）
            int countA = workCount[a];
            int countB = workCount[b];
            return countA.CompareTo(countB);
        }

        // ════════════════════════════════════════════════════════════
        // 统一分配方法：按 WorkAllocationConfig 四大原则分配工作优先级
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// 统一工作优先级分配方法，按 config 四大原则执行：
        /// 1. 保证 GuaranteeCount 人承担（top N 内有火/无火分别给 GuaranteePassionate/GuaranteeNonPassionate）
        /// 2. 三因子排序选 guarantee 人选（passion desc → skill desc → workCount asc）
        /// 3. 超出 guarantee 的有火者给 FloorPassionatePriority 保底优先级
        /// 4. 超出 guarantee 的无火者：UseSkillFloorForNonPassionate=true 时按技能兜底（≥12→2, ≥8→3, 否则 0），
        ///    false 时直接给 FloorNonPassionatePriority
        /// 注：奴隶在专业工作中与殖民者同流程，按兴趣/技能参与分配，无特殊优先级。
        /// </summary>
        private static void AssignWorkType(WorkTypeDef workType, WorkAllocationConfig config)
        {
            workCandidates.Clear();
            for (int i = 0; i < candidatePawns.Count; i++)
            {
                Pawn pawn = candidatePawns[i];
                if (pawn.WorkTagIsDisabled(workType.workTags)) continue;
                // 狩猎类需远程武器：避免无远程武器者被分配
                if (config.RequireRangedWeapon && pawn.equipment?.Primary?.def.IsRangedWeapon != true) continue;
                workCandidates.Add(pawn);
            }
            if (workCandidates.Count == 0) return;

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

            // 按配置分配优先级
            for (int i = 0; i < workCandidates.Count; i++)
            {
                Pawn pawn = workCandidates[i];
                bool hasPassion = HasPassionForAnySkill(pawn, workType.relevantSkills);
                int priority;

                if (i < config.GuaranteeCount)
                {
                    // 原则 1+2：保证 N 人承担，有火/无火分别给优先级
                    priority = hasPassion ? config.GuaranteePassionatePriority : config.GuaranteeNonPassionatePriority;
                }
                else if (hasPassion)
                {
                    // 原则 3：有火者保底优先级
                    priority = config.FloorPassionatePriority;
                }
                else
                {
                    // 原则 4：无火者技能兜底或固定优先级
                    priority = config.UseSkillFloorForNonPassionate
                        ? GetSkillFloorPriority(pawn, workType.relevantSkills)
                        : config.FloorNonPassionatePriority;
                }

                pawn.workSettings.SetPriority(workType, priority);
                if (priority <= 2) workCount[pawn]++;
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
        /// 技能等级兜底：相关技能最高等级 ≥ 12 时返回 2, ≥ 8 时返回 3，否则返回 0。
        /// 用于"无火但技能高者至少 priority=3"的全局规则，避免高技能奴隶/殖民者被排除在工作外。
        /// </summary>
        private static int GetSkillFloorPriority(Pawn pawn, List<SkillDef> skills)
        {
            int maxLevel = GetMaxSkillLevelForSkills(pawn, skills);
            if (maxLevel >= 12) return 2;
            return maxLevel >= 8 ? 3 : 0;
        }

        /// <summary>
        /// 检查殖民者是否对任一指定技能有兴趣（Minor 或 Major）。
        /// </summary>
        private static bool HasPassionForAnySkill(Pawn pawn, List<SkillDef> skills)
        {
            if (pawn?.skills == null) return false;
            if (skills == null || skills.Count == 0) return false;
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
