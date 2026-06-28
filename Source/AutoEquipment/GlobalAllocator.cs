using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace AutoEquipment
{
    /// <summary>
    /// 全局重配：真正的"全局"分配语义。
    ///
    /// 设计目的：
    /// - 高战斗价值殖民者优先获取高价值武器
    /// - 无火小人手里的好武器会被释放给双火小人
    ///
    /// 流程：
    /// 1. 收集所有非征召、非锁定殖民者，按战斗价值降序排序
    /// 2. 第一遍：所有殖民者放下当前武器到地上（进入地图候选池）
    ///    跳过征召中、生物编码武器（个人绑定不可释放）
    /// 3. 第二遍：按战斗价值降序，为每个殖民者从地图候选池评分选最佳武器
    ///    已分配的武器从候选池移除，避免重复抢占
    /// 4. 服装/副武器/库存仍用 ForceEvaluate（按单 Pawn 评估即可）
    ///
    /// 战斗价值复用 SidearmAllocator.ComputeCombatValue：
    /// 射击等级 × 兴趣乘数 + 近战等级 × 兴趣乘数
    /// 兴趣乘数：无火 1.0，单火 1.5，双火 2.0
    /// </summary>
    public static class GlobalAllocator
    {
        // 候选缓存（手动触发，非 Tick 路径，但仍复用静态字段避免 GC）
        private static readonly List<Pawn> sortedPawns = new List<Pawn>();
        private static readonly List<Thing> candidateWeapons = new List<Thing>();
        private static readonly HashSet<int> assignedWeaponIds = new HashSet<int>();

        /// <summary>
        /// 全局重配：放下所有殖民者武器，按战斗价值降序重新分配。
        /// 返回被触发的殖民者数量。
        /// </summary>
        public static int ReallocateAll()
        {
            sortedPawns.Clear();
            candidateWeapons.Clear();
            assignedWeaponIds.Clear();

            // ========== 收集候选殖民者 ==========
            foreach (Map map in Find.Maps)
            {
                foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
                {
                    if (DLCCompat.IsGhoul(pawn)) continue;
                    if (!PawnSuitabilityChecker.CanManageGear(pawn)) continue;
                    if (pawn.Dead || pawn.Downed) continue;
                    // 征召中的殖民者正在战斗，不打断
                    if (pawn.Drafted) continue;

                    CompGearManager comp = pawn.GetComp<CompGearManager>();
                    if (comp == null || comp.locked) continue;

                    sortedPawns.Add(pawn);
                }
            }

            if (sortedPawns.Count == 0) return 0;

            // 按战斗价值降序排序：高价值殖民者优先分配
            sortedPawns.Sort(ComparePawnByCombatValueDesc);

            // ========== 第一遍：放下所有殖民者的当前武器 ==========
            // 设计意图：让无火小人手里的好武器进入地图候选池，供双火小人拾取
            // 保护：跳过生物编码武器（个人绑定）、跳过无武器者
            int droppedCount = 0;
            for (int i = 0; i < sortedPawns.Count; i++)
            {
                Pawn pawn = sortedPawns[i];
                ThingWithComps primary = pawn.equipment?.Primary;
                if (primary == null) continue;

                // 生物编码武器：个人绑定，放下后无法被他人拾取，跳过
                var bioApp = primary.TryGetComp<CompBiocodable>();
                if (bioApp != null && bioApp.Biocoded) continue;

                // 放下武器到 Pawn 位置，进入地图候选池
                ThingWithComps dropped;
                pawn.equipment.TryDropEquipment(primary, out dropped, pawn.Position, false);
                if (dropped != null)
                {
                    droppedCount++;
                    Log.Message($"[AutoEquipment] 全局重配: {pawn.LabelShort} 放下武器 {dropped.LabelShort}");
                }
            }

            Log.Message($"[AutoEquipment] 全局重配: 共 {droppedCount} 把武器已释放到地图候选池");

            // ========== 收集地图候选武器 ==========
            foreach (Map map in Find.Maps)
            {
                foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon))
                {
                    if (!thing.def.IsWeapon) continue;
                    if (!thing.def.IsRangedWeapon && !thing.def.IsMeleeWeapon) continue;
                    if (thing.def.IsStuff) continue;
                    candidateWeapons.Add(thing);
                }
            }

            // ========== 第二遍：按战斗价值降序分配最佳武器 ==========
            int triggered = 0;
            for (int i = 0; i < sortedPawns.Count; i++)
            {
                Pawn pawn = sortedPawns[i];
                if (pawn.Map == null) continue;

                CompGearManager comp = pawn.GetComp<CompGearManager>();
                if (comp == null) continue;

                Role role = comp.CurrentRole;
                GearContext context = ContextDetector.GetContext(pawn);

                // 评分所有候选武器，选最佳
                Thing bestWeapon = null;
                float bestScore = float.MinValue;
                int bestIdx = -1;

                for (int j = 0; j < candidateWeapons.Count; j++)
                {
                    Thing w = candidateWeapons[j];
                    if (w == null) continue;
                    if (assignedWeaponIds.Contains(w.thingIDNumber)) continue;
                    if (w.IsForbidden(pawn)) continue;
                    if (!pawn.CanReserve(w) || !pawn.CanReach(w, PathEndMode.ClosestTouch, Danger.Some)) continue;
                    if (w.def.IsRangedWeapon && pawn.WorkTagIsDisabled(WorkTags.Violent)) continue;
                    if (w.def.IsMeleeWeapon && pawn.WorkTagIsDisabled(WorkTags.Violent)) continue;

                    // 生物编码检查：非编码者不能拾取
                    var bioApp = w.TryGetComp<CompBiocodable>();
                    if (bioApp != null && bioApp.Biocoded && bioApp.CodedPawn != pawn) continue;

                    float score = GearScorer.ScoreWeapon(pawn, w, role, context);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestWeapon = w;
                        bestIdx = j;
                    }
                }

                if (bestWeapon != null)
                {
                    // 标记为已分配，避免后续殖民者抢占
                    assignedWeaponIds.Add(bestWeapon.thingIDNumber);
                    // 从候选池移除（设为 null 而非 Remove，避免列表重排开销）
                    candidateWeapons[bestIdx] = null;

                    // 创建 Equip job
                    var job = JobMaker.MakeJob(JobDefOf.Equip, bestWeapon);
                    pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);

                    Log.Message($"[AutoEquipment] 全局重配 #{i + 1}: {pawn.LabelShort} (战斗价值={SidearmAllocator.ComputeCombatValue(pawn):F1}) ← {bestWeapon.LabelShort} (score={bestScore:F1})");
                }
                else
                {
                    Log.Message($"[AutoEquipment] 全局重配 #{i + 1}: {pawn.LabelShort} 无可用武器");
                }

                // 服装/副武器/库存仍用 ForceEvaluate 评估
                comp.ForceEvaluate(CompGearManager.ReloadTarget.Apparel);
                comp.ForceEvaluate(CompGearManager.ReloadTarget.Sidearm);
                comp.ForceEvaluate(CompGearManager.ReloadTarget.Inventory);

                triggered++;
            }

            return triggered;
        }

        private static int ComparePawnByCombatValueDesc(Pawn a, Pawn b)
        {
            return SidearmAllocator.ComputeCombatValue(b).CompareTo(SidearmAllocator.ComputeCombatValue(a));
        }
    }
}
