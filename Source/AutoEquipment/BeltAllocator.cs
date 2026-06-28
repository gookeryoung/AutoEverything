using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace AutoEquipment
{
    /// <summary>
    /// 腰带附件全局分配器：为纯近战角色（射击无火）分配护盾腰带或消防背包。
    ///
    /// 设计目的：
    /// - 纯近战角色需要贴身作战，护盾腰带提供远程防护，消防背包应对火灾/机械族
    /// - 全局协调：至少确保 1 名殖民者装备消防背包，避免全员护盾导致火灾无人应对
    /// - 复用 SidearmAllocator.ComputeCombatValue 按战斗价值降序分配
    ///
    /// 分配规则：
    /// 1. 收集所有纯近战角色（射击无火）且 belt 层空缺的殖民者
    /// 2. 收集地图上所有 belt 类附件候选（ShieldBelt / FirefoamPack）
    /// 3. 若全局无任何殖民者穿戴消防背包，强制最高战斗价值纯近战角色拿消防背包
    /// 4. 其余纯近战角色在护盾与消防背包中按评分选择
    /// </summary>
    public static class BeltAllocator
    {
        // 全局分配间隔：≥ 2500 tick（规则要求），3000 tick ≈ 50 秒
        private const int AllocationInterval = 3000;
        private static int lastAllocationTick = -9999;

        // 候选缓存（Tick 路径禁止 new List，复用静态字段）
        private static readonly List<Pawn> candidatePawns = new List<Pawn>();
        private static readonly List<Thing> candidateBelts = new List<Thing>();

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
        /// 全局分配：收集候选并按战斗价值降序分配 belt。
        /// </summary>
        private static void AllocateAllColonists()
        {
            candidatePawns.Clear();
            candidateBelts.Clear();

            bool anyFirefoamWorn = false;

            foreach (Map map in Find.Maps)
            {
                CollectCandidatePawns(map, ref anyFirefoamWorn);
                CollectCandidateBelts(map);
            }

            if (candidatePawns.Count == 0 || candidateBelts.Count == 0) return;

            // 按战斗价值降序排序（高价值优先）——List.Sort 非 LINQ，Tick 路径允许
            candidatePawns.Sort(ComparePawnByCombatValueDesc);

            // 全局保底：若无任何殖民者穿戴消防背包，强制最高价值纯近战角色拿消防背包
            bool forceFirefoam = !anyFirefoamWorn;
            bool firefoamAssigned = false;

            for (int i = 0; i < candidatePawns.Count; i++)
            {
                Pawn pawn = candidatePawns[i];
                if (HasBelt(pawn)) continue;

                // 首个高价值 Pawn 强制拿消防背包（若全局无人穿戴）
                if (forceFirefoam && !firefoamAssigned)
                {
                    Thing firefoam = FindFirstFirefoamPack();
                    if (firefoam != null)
                    {
                        AssignBelt(pawn, firefoam, "全局保底消防背包");
                        candidateBelts.Remove(firefoam);
                        firefoamAssigned = true;
                        continue;
                    }
                }

                // 其余 Pawn 在护盾与消防背包中按评分选择
                Thing best = null;
                float bestScore = 0f;

                for (int j = 0; j < candidateBelts.Count; j++)
                {
                    Thing b = candidateBelts[j];
                    if (b == null) continue;

                    float score = ScoreBelt(pawn, b);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = b;
                    }
                }

                if (best != null)
                {
                    AssignBelt(pawn, best, bestScore.ToString("F1"));
                    candidateBelts.Remove(best);
                }
            }
        }

        private static void CollectCandidatePawns(Map map, ref bool anyFirefoamWorn)
        {
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                if (DLCCompat.IsGhoul(pawn)) continue;
                if (!PawnSuitabilityChecker.CanManageGear(pawn)) continue;
                if (pawn.WorkTagIsDisabled(WorkTags.Violent)) continue;
                if (DLCCompat.IsSlave(pawn) || DLCCompat.IsChild(pawn)) continue;

                CompGearManager comp = pawn.GetComp<CompGearManager>();
                if (comp == null || comp.locked) continue;

                // 检查当前穿戴的消防背包（用于全局保底判定）
                if (IsWearingFirefoamPack(pawn)) anyFirefoamWorn = true;

                // 仅纯近战角色（射击无火）参与 belt 分配
                if (!IsPureMeleeShooter(pawn)) continue;

                // 必须有 belt 空位
                if (HasBelt(pawn)) continue;

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
        /// belt 评分：护盾腰带优先（近战防护），消防背包次之。
        /// 纯近战角色贴身作战，护盾提供远程免疫，消防背包应对火灾。
        /// </summary>
        private static float ScoreBelt(Pawn pawn, Thing belt)
        {
            float score = 0f;

            // 护盾腰带：提供远程免疫，近战角色最需
            if (IsShieldBelt(belt))
            {
                score += 100f;
            }

            // 消防背包：应对火灾/机械族，次选
            if (IsFirefoamPack(belt))
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

            Log.Message($"[AutoEquipment] 腰带分配: {pawn.LabelShort} (战斗价值={SidearmAllocator.ComputeCombatValue(pawn):F1}) ← {belt.LabelShort} (reason={reason})");
        }

        // ===================== 判定辅助 =====================

        /// <summary>
        /// 纯近战角色判定：射击技能无火（passion=None）。
        /// </summary>
        private static bool IsPureMeleeShooter(Pawn pawn)
        {
            if (pawn?.skills == null) return false;
            SkillRecord shooting = pawn.skills.GetSkill(SkillDefOf.Shooting);
            if (shooting == null) return true;
            return shooting.passion == Passion.None;
        }

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
        /// 护盾腰带判定：通过 defName 启发式识别。
        /// </summary>
        private static bool IsShieldBelt(Thing thing)
        {
            return thing?.def != null
                && thing.def.defName.ToUpperInvariant().Contains("SHIELD");
        }

        /// <summary>
        /// 消防背包判定：通过 defName 启发式识别。
        /// </summary>
        private static bool IsFirefoamPack(Thing thing)
        {
            return thing?.def != null
                && thing.def.defName.ToUpperInvariant().Contains("FIREFOAM");
        }

        /// <summary>
        /// 检查 Pawn 是否已穿戴 belt 层附件。
        /// </summary>
        private static bool HasBelt(Pawn pawn)
        {
            if (pawn.apparel?.WornApparel == null) return false;
            List<Apparel> worn = pawn.apparel.WornApparel;
            for (int i = 0; i < worn.Count; i++)
            {
                if (IsBelt(worn[i])) return true;
            }
            return false;
        }

        /// <summary>
        /// 检查 Pawn 是否已穿戴消防背包。
        /// </summary>
        private static bool IsWearingFirefoamPack(Pawn pawn)
        {
            if (pawn.apparel?.WornApparel == null) return false;
            List<Apparel> worn = pawn.apparel.WornApparel;
            for (int i = 0; i < worn.Count; i++)
            {
                if (IsFirefoamPack(worn[i])) return true;
            }
            return false;
        }

        private static Thing FindFirstFirefoamPack()
        {
            for (int i = 0; i < candidateBelts.Count; i++)
            {
                if (IsFirefoamPack(candidateBelts[i])) return candidateBelts[i];
            }
            return null;
        }

        private static int ComparePawnByCombatValueDesc(Pawn a, Pawn b)
        {
            return SidearmAllocator.ComputeCombatValue(b).CompareTo(SidearmAllocator.ComputeCombatValue(a));
        }
    }
}
