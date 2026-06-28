using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace AutoEquipment
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
        /// 计算 Pawn 的战斗价值分（用于副武器分配优先级）。
        /// 双火（Passion.Major）高技能角色得分最高。
        /// </summary>
        public static float ComputeCombatValue(Pawn pawn)
        {
            if (pawn?.skills == null) return 0f;

            float total = 0f;
            SkillRecord shooting = pawn.skills.GetSkill(SkillDefOf.Shooting);
            SkillRecord melee = pawn.skills.GetSkill(SkillDefOf.Melee);

            if (shooting != null)
                total += shooting.Level * GetPassionMult(shooting.passion);

            if (melee != null)
                total += melee.Level * GetPassionMult(melee.passion);

            return total;
        }

        private static float GetPassionMult(Passion passion)
        {
            switch (passion)
            {
                case Passion.Minor: return 1.5f;
                case Passion.Major: return 2.0f;
                default: return 1.0f;
            }
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
            candidatePawns.Sort(ComparePawnByCombatValueDesc);

            // 依次为高价值 Pawn 分配，分配后从候选池移除（设为 null）
            for (int i = 0; i < candidatePawns.Count; i++)
            {
                Pawn pawn = candidatePawns[i];
                Thing primary = pawn.equipment?.Primary;
                if (primary == null) continue;

                bool needMelee = primary.def.IsRangedWeapon;
                bool needRanged = primary.def.IsMeleeWeapon;

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

        private static int ComparePawnByCombatValueDesc(Pawn a, Pawn b)
        {
            return ComputeCombatValue(b).CompareTo(ComputeCombatValue(a));
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
            Log.Message($"[AutoEquipment] 副武器分配: {pawn.LabelShort} (战斗价值={ComputeCombatValue(pawn):F1}) ← {weapon.LabelShort} (score={score:F1})");
        }
    }
}
