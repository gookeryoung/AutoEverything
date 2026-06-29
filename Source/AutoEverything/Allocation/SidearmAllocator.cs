using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using AutoEverything.Core;
using AutoEverything.RoleEvaluation;
using AutoEverything.AutoEquipment;

namespace AutoEverything.Allocation
{
    /// <summary>
    /// 副武器全局分配器：按战斗价值优先级为殖民者分配副武器。
    ///
    /// 设计目的：
    /// - 高战斗价值角色（双火高技能）优先获得副武器
    /// - 地图副武器数量不足时，低价值角色不抢占
    /// - 避免单 Pawn 各自拾取导致低价值角色先拿到武器
    ///
    /// 战斗价值分 = 射击等级 × 兴趣乘数 + 近战等级 × 兴趣乘数
    /// 兴趣乘数：无火 1.0，单火 1.5，双火 2.0
    /// 双火高技能角色得分最高，优先分配。
    /// </summary>
    public static class SidearmAllocator
    {
        // 全局分配间隔：比单 Pawn 评估间隔长，避免每个 Pawn 触发全局扫描
        // 2000 tick ≈ 33 秒，副武器非紧急操作，延迟可接受
        private const int AllocationInterval = 2000;
        private static int lastAllocationTick = -9999;

        // 候选缓存（Tick 路径禁止 new List，复用静态字段）
        private static readonly List<Pawn> candidatePawns = new List<Pawn>();
        private static readonly List<Thing> candidateWeapons = new List<Thing>();

        /// <summary>
        /// 为单个 Pawn 触发副武器分配。
        /// 受全局周期控制：仅当距离上次全局分配超过 AllocationInterval 时才重新分配。
        /// 第一个触发该周期的 Pawn 承担全局分配成本，其余 Pawn 跳过。
        /// </summary>
        public static void AllocateForPawn(Pawn pawn)
        {
            int tick = Find.TickManager.TicksGame;
            if (tick - lastAllocationTick < AllocationInterval) return;
            lastAllocationTick = tick;

            AllocateAllColonists();
        }

        /// <summary>
        /// 全局分配：收集所有需要副武器的 Pawn 与可用武器，
        /// 按 Pawn 战斗价值降序排序后依次分配。
        /// </summary>
        private static void AllocateAllColonists()
        {
            candidatePawns.Clear();
            candidateWeapons.Clear();

            foreach (Map map in Find.Maps)
            {
                CollectCandidatePawns(map);
                CollectCandidateWeapons(map);
            }

            if (candidatePawns.Count == 0 || candidateWeapons.Count == 0) return;

            // 按战斗价值降序排序（高价值优先）——List.Sort 非 LINQ，Tick 路径允许
            // 预计算缓存：List.Sort 是 O(n log n) 次比较，避免每次比较重复调用
            // CombatEvaluator.ComputeCombatValue（涉及技能查询与特质查询），50 人约省 300 次重复计算
            var combatValueCache = new Dictionary<Pawn, float>();
            for (int i = 0; i < candidatePawns.Count; i++)
            {
                combatValueCache[candidatePawns[i]] = CombatEvaluator.ComputeCombatValue(candidatePawns[i]);
            }
            candidatePawns.Sort((a, b) => combatValueCache[b].CompareTo(combatValueCache[a]));

            // 依次为高价值 Pawn 分配，分配后从候选池移除（设为 null）
            for (int i = 0; i < candidatePawns.Count; i++)
            {
                Pawn pawn = candidatePawns[i];
                Thing primary = pawn.equipment?.Primary;
                if (primary == null) continue;

                bool needMelee = primary.def.IsRangedWeapon;
                bool needRanged = primary.def.IsMeleeWeapon;

                // 护盾腰带约束：护盾会阻挡所有远程武器射击
                // 带护盾的 Pawn 拿远程/EMP 副武器毫无意义，跳过远程副武器分配
                // 带消防背包的 Pawn 不受此限制，可正常配远程/EMP
                bool hasShieldBelt = IsWearingShieldBelt(pawn);
                if (needRanged && hasShieldBelt) continue;

                Thing best = null;
                float bestScore = 0f;
                int bestIdx = -1;

                for (int j = 0; j < candidateWeapons.Count; j++)
                {
                    Thing w = candidateWeapons[j];
                    if (w == null) continue;
                    if (needMelee && !w.def.IsMeleeWeapon) continue;
                    if (needRanged && !w.def.IsRangedWeapon) continue;

                    CompGearManager comp = pawn.GetComp<CompGearManager>();
                    if (comp == null) continue;

                    float score = GearScorer.ScoreSidearm(pawn, w, comp.CurrentRole);

                    // 纯近战角色（射击无火）：副武器优先 EMP，应对机械族/护盾
                    // 设计意图：纯近战小人远程射击天赋不足，普通远程武器收益低
                    // EMP 武器能瘫痪机械族与护盾，贴身近战时提供战术价值
                    // 注意：带护盾腰带的 Pawn 已在上方跳过，此处无需再判断
                    if (needRanged && PawnCombatProfile.IsPureMeleeShooter(pawn) && GearDefClassifier.IsEmpWeapon(w))
                    {
                        score += 1000f;
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = w;
                        bestIdx = j;
                    }
                }

                if (best != null)
                {
                    AssignSidearm(pawn, best, bestScore);
                    candidateWeapons[bestIdx] = null;
                }
            }
        }

        /// <summary>
        /// 检查 Pawn 是否穿戴护盾腰带。
        /// 护盾腰带会阻挡远程武器射击，带护盾的 Pawn 不应配远程副武器。
        /// </summary>
        private static bool IsWearingShieldBelt(Pawn pawn)
        {
            if (pawn.apparel?.WornApparel == null) return false;
            List<Apparel> worn = pawn.apparel.WornApparel;
            for (int i = 0; i < worn.Count; i++)
            {
                if (worn[i].def.apparel?.layers != null
                    && worn[i].def.apparel.layers.Contains(ApparelLayerDefOf.Belt)
                    && worn[i].def.defName.IndexOf("SHIELD", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static void CollectCandidatePawns(Map map)
        {
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                if (DLCCompat.IsGhoul(pawn)) continue;
                if (!PawnSuitabilityChecker.CanManageGear(pawn)) continue;
                if (pawn.WorkTagIsDisabled(WorkTags.Violent)) continue;
                if (DLCCompat.IsSlave(pawn) || DLCCompat.IsChild(pawn)) continue;

                CompGearManager comp = pawn.GetComp<CompGearManager>();
                if (comp == null || comp.locked) continue;

                // 必须有主武器才需要副武器
                Thing primary = pawn.equipment?.Primary;
                if (primary == null) continue;

                // 库存中已有副武器则跳过
                if (HasSidearm(pawn, primary)) continue;

                candidatePawns.Add(pawn);
            }
        }

        private static void CollectCandidateWeapons(Map map)
        {
            foreach (Thing weapon in map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon))
            {
                if (!IsCandidateSidearm(weapon)) continue;
                candidateWeapons.Add(weapon);
            }
        }

        /// <summary>
        /// 检查 Pawn 库存中是否已持有与主武器类型相反的副武器。
        /// </summary>
        private static bool HasSidearm(Pawn pawn, Thing primary)
        {
            bool primaryIsRanged = primary.def.IsRangedWeapon;
            foreach (Thing item in pawn.inventory.innerContainer)
            {
                if (primaryIsRanged && item.def.IsMeleeWeapon) return true;
                if (!primaryIsRanged && item.def.IsRangedWeapon) return true;
            }
            return false;
        }

        /// <summary>
        /// 判断武器是否为可用副武器候选。
        /// </summary>
        private static bool IsCandidateSidearm(Thing weapon)
        {
            if (weapon?.def == null) return false;
            if (!weapon.def.IsWeapon) return false;
            // 候选筛选留给分配阶段（needMelee/needRanged），此处仅排除非武器
            return true;
        }

        private static void AssignSidearm(Pawn pawn, Thing weapon, float score)
        {
            var job = JobMaker.MakeJob(JobDefOf.TakeCountToInventory, weapon);
            job.count = 1;
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);

            // 决策日志：玩家可见的换装反馈（低频，受全局周期控制）
            Log.Message($"[AutoEverything] 副武器分配: {AEDebug.Label(pawn)} (战斗价值={CombatEvaluator.ComputeCombatValue(pawn):F1}) ← {weapon.LabelShort} (score={score:F1})");
        }
    }
}
