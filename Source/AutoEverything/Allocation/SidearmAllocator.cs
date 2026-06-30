using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using AutoEverything.Core;
using AutoEverything.RoleEvaluation;
using AutoEverything.AutoEquipment;
using AutoEverything.AutoEquipment.Scoring;

namespace AutoEverything.Allocation
{
    /// <summary>
    /// EMP 手雷全局分配器：为 Flexible 后排（Shooter/Hunter/Leader）评级较低者分配 EMP 手雷。
    ///
    /// 设计目的：
    /// - 取消携带多个装备：每人只选最适合自己的主武器，不再自动分配反向类型副武器
    /// - EMP 手雷特例：评级较低的 Flexible 后排至少 2 人持有 EMP 手雷，
    ///   应对机械族/护盾等需要 EMP 的战术场景
    /// - 评级低者优先：重甲前排承担近战，评级低的后排承担 EMP 战术支援
    ///
    /// 分配规则：
    /// 1. 收集所有 Flexible 后排（IsBackRow）且无 EMP 副武器的殖民者
    /// 2. 按 CombatTier 升序排序（评级低者优先）
    /// 3. 收集地图上所有 EMP 武器候选
    /// 4. 前 2 人分配 EMP 武器（库存携带，副武器特例）
    /// </summary>
    public static class SidearmAllocator
    {
        // 全局分配间隔：比单 Pawn 评估间隔长，避免每个 Pawn 触发全局扫描
        // 2000 tick ≈ 33 秒，EMP 分配非紧急操作，延迟可接受
        private const int AllocationInterval = 2000;
        private static int lastAllocationTick = -9999;

        // EMP 手雷最低配备人数：Flexible 后排至少 2 人持有 EMP 手雷
        private const int MinEmpPawns = 2;

        // 候选缓存（Tick 路径禁止 new List，复用静态字段）
        private static readonly List<Pawn> candidatePawns = new List<Pawn>();
        private static readonly List<Thing> candidateWeapons = new List<Thing>();

        // 评级缓存：排序前预计算，避免 Sort 比较器内 O(n log n) 次重复调用 GetAutoCombatTier
        private static readonly Dictionary<Pawn, CombatTier> tierCache = new Dictionary<Pawn, CombatTier>();

        /// <summary>
        /// 为单个 Pawn 触发 EMP 手雷分配。
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
        /// 全局分配：收集所有需要 EMP 手雷的 Flexible 后排殖民者与可用 EMP 武器，
        /// 按 CombatTier 升序排序后依次分配前 2 人。
        /// try-catch 隔离：失败时 Log.ErrorOnce 记录，不影响其他 Pawn 评估。
        /// </summary>
        private static void AllocateAllColonists()
        {
            try
            {
                candidatePawns.Clear();
                candidateWeapons.Clear();
                tierCache.Clear();

                foreach (Map map in Find.Maps)
                {
                    CollectCandidatePawns(map);
                    CollectCandidateWeapons(map);
                }

                if (candidatePawns.Count == 0 || candidateWeapons.Count == 0) return;

                // 预计算评级缓存：避免 Sort 比较器内重复调用 GetAutoCombatTier（O(n log n) 次技能查询）
                for (int i = 0; i < candidatePawns.Count; i++)
                {
                    Pawn p = candidatePawns[i];
                    tierCache[p] = CombatEvaluator.GetAutoCombatTier(p);
                }

                // 按 CombatTier 升序排序（评级低者优先）——List.Sort 非 LINQ，Tick 路径允许
                // 设计意图：评级低的后排承担伤害能力较弱，优先持有 EMP 手雷提供战术价值
                candidatePawns.Sort((a, b) => tierCache[a].CompareTo(tierCache[b]));

                // 前 2 人分配 EMP 武器（库存携带，副武器特例）
                int empCount = 0;

                for (int i = 0; i < candidatePawns.Count; i++)
                {
                    Pawn pawn = candidatePawns[i];

                    // 库存中已有 EMP 武器则跳过（避免重复分配）
                    if (HasEmpSidearm(pawn)) continue;

                    if (empCount < MinEmpPawns)
                    {
                        int empIdx = FindFirstEmpWeaponIndex();
                        if (empIdx >= 0)
                        {
                            AssignSidearm(pawn, candidateWeapons[empIdx], "EMP手雷(评级优先)");
                            // null 占位避免 Remove 的 O(n) 列表重排
                            candidateWeapons[empIdx] = null;
                            empCount++;
                            continue;
                        }
                    }

                    // 已达 2 人配额或无 EMP 武器可用，不再分配其他副武器
                    // 设计意图：每人只选最适合自己的主武器，不携带多个装备
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] EMP手雷分配失败: " + ex.Message, SidearmErrorSalt);
            }
        }

        // EMP 手雷分配错误去重 salt，与 BeltAllocator.BeltErrorSalt 区分
        private const int SidearmErrorSalt = 0xB1A1;

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

                // 仅 Flexible 后排（Shooter/Hunter/Leader）参与 EMP 手雷分配
                // 设计意图：后排远程角色评级较低者需要 EMP 手雷应对机械族/护盾
                Role role = RoleDetector.DetectRole(pawn);
                if (!RoleDetector.IsBackRow(role)) continue;

                // 必须有主武器（用于排除无武器的新殖民者）
                Thing primary = pawn.equipment?.Primary;
                if (primary == null) continue;

                // 库存中已有 EMP 武器则跳过（避免重复分配）
                if (HasEmpSidearm(pawn)) continue;

                candidatePawns.Add(pawn);
            }
        }

        private static void CollectCandidateWeapons(Map map)
        {
            foreach (Thing weapon in map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon))
            {
                // 仅收集 EMP 武器（取消其他副武器分配）
                if (!GearDefClassifier.IsEmpWeapon(weapon)) continue;
                if (weapon.IsForbidden(Faction.OfPlayer)) continue;
                candidateWeapons.Add(weapon);
            }
        }

        /// <summary>
        /// 检查 Pawn 库存中是否已持有 EMP 武器。
        /// </summary>
        private static bool HasEmpSidearm(Pawn pawn)
        {
            foreach (Thing item in pawn.inventory.innerContainer)
            {
                if (GearDefClassifier.IsEmpWeapon(item)) return true;
            }
            return false;
        }

        /// <summary>
        /// 在候选武器池中查找首个 EMP 武器索引。
        /// </summary>
        private static int FindFirstEmpWeaponIndex()
        {
            for (int i = 0; i < candidateWeapons.Count; i++)
            {
                Thing w = candidateWeapons[i];
                if (w != null && GearDefClassifier.IsEmpWeapon(w)) return i;
            }
            return -1;
        }

        private static void AssignSidearm(Pawn pawn, Thing weapon, string reason)
        {
            var job = JobMaker.MakeJob(JobDefOf.TakeCountToInventory, weapon);
            job.count = 1;
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);

            // 决策日志：玩家可见的换装反馈（低频，受全局周期控制）
            Log.Message($"[AutoEverything] EMP 手雷分配: {AEDebug.Label(pawn)} (评级={CombatEvaluator.GetAutoCombatTier(pawn)}) ← {weapon.LabelShort} (reason={reason})");
        }
    }
}
