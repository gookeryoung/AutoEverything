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
    /// 腰带附件全局分配器：为重甲前排（Brawler）分配护盾腰带或消防背包。
    ///
    /// 设计目的：
    /// - 重甲前排承担近战伤害，护盾腰带提供远程免疫，消防背包应对火灾/机械族
    /// - 评级低者优先：评级低的重甲前排承担伤害能力较弱，优先配消防背包增强生存
    /// - 取消全局保底逻辑：改为"前 2 人强制消防背包"规则，更符合战术需求
    ///
    /// 分配规则：
    /// 1. 收集所有重甲前排（Heavy=Brawler）且 belt 层空缺的殖民者
    /// 2. 按 CombatTier 升序排序（评级低者优先）
    /// 3. 收集地图上所有 belt 类附件候选（ShieldBelt / FirefoamPack）
    /// 4. 前 2 人强制分配消防背包（若库存有），其余配护盾腰带
    /// </summary>
    public static class BeltAllocator
    {
        // 全局分配间隔：≥ 2500 tick（规则要求），3000 tick ≈ 50 秒
        private const int AllocationInterval = 3000;
        private static int lastAllocationTick = -9999;

        // 消防背包最低配备人数：重甲前排至少 2 人配消防背包
        private const int MinFirefoamPawns = 2;

        // 候选缓存（Tick 路径禁止 new List，复用静态字段）
        private static readonly List<Pawn> candidatePawns = new List<Pawn>();
        private static readonly List<Thing> candidateBelts = new List<Thing>();

        // 评级缓存：排序前预计算，避免 Sort 比较器内 O(n log n) 次重复调用 GetSystemTier
        private static readonly Dictionary<Pawn, CombatTier> tierCache = new Dictionary<Pawn, CombatTier>();

        /// <summary>
        /// 为单个 Pawn 触发 belt 分配。
        /// 受全局周期控制：仅当距离上次全局分配超过 AllocationInterval 时才重新分配。
        /// </summary>
        public static void AllocateForPawn(Pawn pawn)
        {
            int tick = Find.TickManager.TicksGame;
            if (tick - lastAllocationTick < AllocationInterval) return;
            lastAllocationTick = tick;

            AllocateAllColonists();
        }

        /// <summary>
        /// 全局分配：收集候选并按 CombatTier 升序分配 belt。
        /// 评级低者优先配消防背包，其余配护盾腰带。
        /// try-catch 隔离：失败时 Log.ErrorOnce 记录，不影响其他 Pawn 评估。
        /// </summary>
        private static void AllocateAllColonists()
        {
            try
            {
                candidatePawns.Clear();
                candidateBelts.Clear();
                tierCache.Clear();

                foreach (Map map in Find.Maps)
                {
                    CollectCandidatePawns(map);
                    CollectCandidateBelts(map);
                }

                if (candidatePawns.Count == 0 || candidateBelts.Count == 0) return;

                // 预计算评级缓存：避免 Sort 比较器内重复调用 GetSystemTier（O(n log n) 次技能+配偶查询）
                // 用 GetSystemTier（含配偶豁免）与评级标签/ITab 显示一致
                for (int i = 0; i < candidatePawns.Count; i++)
                {
                    Pawn p = candidatePawns[i];
                    tierCache[p] = CombatEvaluator.GetSystemTier(p);
                }

                // 按 CombatTier 升序排序（评级低者优先）——List.Sort 非 LINQ，Tick 路径允许
                // 设计意图：评级低的重甲前排承担伤害能力较弱，优先配消防背包增强生存
                candidatePawns.Sort((a, b) => tierCache[a].CompareTo(tierCache[b]));

                // 前 2 人强制分配消防背包（若库存有），其余配护盾腰带
                int firefoamCount = 0;

                for (int i = 0; i < candidatePawns.Count; i++)
                {
                    Pawn pawn = candidatePawns[i];
                    if (GearDefClassifier.HasBeltLayerApparel(pawn)) continue;

                    // 优先给前 2 人（评级最低者）配消防背包
                    if (firefoamCount < MinFirefoamPawns)
                    {
                        int firefoamIdx = FindFirstFirefoamPackIndex();
                        if (firefoamIdx >= 0)
                        {
                            AssignBelt(pawn, candidateBelts[firefoamIdx], "近战消防背包(评级优先)");
                            // null 占位避免 Remove 的 O(n) 列表重排
                            candidateBelts[firefoamIdx] = null;
                            firefoamCount++;
                            continue;
                        }
                    }

                    // 其余候选在护盾腰带与消防背包中按评分选择
                    // ScoreBelt：Brawler+护盾+100，Brawler+消防+60（默认护盾腰带胜出）
                    Thing best = null;
                    float bestScore = 0f;
                    int bestIdx = -1;

                    // 候选池已全部为 Brawler（CollectCandidatePawns 已 gate），护盾腰带默认 +100
                    for (int j = 0; j < candidateBelts.Count; j++)
                    {
                        Thing b = candidateBelts[j];
                        if (b == null) continue;

                        float score = ScoreBelt(pawn, b);
                        if (score > bestScore)
                        {
                            bestScore = score;
                            best = b;
                            bestIdx = j;
                        }
                    }

                    if (best != null)
                    {
                        AssignBelt(pawn, best, bestScore.ToString("F1"));
                        // null 占位避免 Remove 的 O(n) 列表重排
                        candidateBelts[bestIdx] = null;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] 腰带分配失败: " + ex.Message, BeltErrorSalt);
            }
        }

        // 腰带分配错误去重 salt，与 AutoExecutor.GearErrorSalt 区分
        private const int BeltErrorSalt = 0xB1A0;

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

                // 仅近战角色（Brawler）参与 belt 分配
                // 设计意图：护盾腰带阻挡远程射击，仅适合近战角色；
                // 消防背包也优先给前排承担伤害的近战单位
                Role role = RoleDetector.DetectRole(pawn);
                if (role != Role.Brawler) continue;

                // 必须有 belt 空位
                if (GearDefClassifier.HasBeltLayerApparel(pawn)) continue;

                candidatePawns.Add(pawn);
            }
        }

        private static void CollectCandidateBelts(Map map)
        {
            foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Apparel))
            {
                if (!IsBelt(thing)) continue;
                if (thing.IsForbidden(Faction.OfPlayer)) continue;
                candidateBelts.Add(thing);
            }
        }

        /// <summary>
        /// belt 评分：护盾腰带对近战角色 +100，消防背包对所有候选 +60。
        /// 设计意图：候选池已全部为 Brawler（CollectCandidatePawns 已 gate），护盾腰带默认胜出；
        /// 消防背包评分较低，仅在前 2 人强制分配逻辑中选用。
        /// </summary>
        private static float ScoreBelt(Pawn pawn, Thing belt)
        {
            float score = 0f;

            // 护盾腰带：仅近战角色（Brawler）加分
            // 候选池已全部为 Brawler，护盾腰带默认 +100
            if (GearDefClassifier.IsShieldBelt(belt))
            {
                score += 100f;
            }

            // 消防背包：应对火灾/机械族，所有候选可用
            if (GearDefClassifier.IsFirefoamPack(belt))
            {
                score += 60f;
            }

            // 品质加分
            QualityCategory qc;
            if (belt.TryGetQuality(out qc))
            {
                score += (int)qc * 5f;
            }

            return score;
        }

        private static void AssignBelt(Pawn pawn, Thing belt, string reason)
        {
            var job = JobMaker.MakeJob(JobDefOf.Wear, belt);
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);

            // 决策日志：受 debug 开关控制（与 SidearmAllocator 一致，避免周期触发刷屏）
            if (AEDebug.IsActive)
                AEDebug.Log(() => $"[AutoEverything] 腰带分配: {AEDebug.Label(pawn)} (评级={CombatEvaluator.GetSystemTier(pawn)}) ← {belt.LabelShort} (reason={reason})");
        }

        // ===================== 判定辅助 =====================

        /// <summary>
        /// 判断 apparel 是否为 belt 层附件。
        /// </summary>
        private static bool IsBelt(Thing thing)
        {
            if (thing?.def?.apparel == null) return false;
            return thing.def.apparel.layers != null
                && thing.def.apparel.layers.Contains(ApparelLayerDefOf.Belt);
        }

        /// <summary>
        /// 检查 Pawn 是否已穿戴 belt 层附件。
        /// 已迁移到 GearDefClassifier.HasBeltLayerApparel 统一实现。
        /// </summary>

        private static int FindFirstFirefoamPackIndex()
        {
            for (int i = 0; i < candidateBelts.Count; i++)
            {
                Thing b = candidateBelts[i];
                if (b != null && GearDefClassifier.IsFirefoamPack(b)) return i;
            }
            return -1;
        }
    }
}
