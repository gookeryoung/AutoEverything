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
    /// 全局装备重配：真正的"全局"分配语义。
    ///
    /// 设计目的：
    /// - 高战斗价值殖民者优先获取高价值武器
    /// - 无火小人手里的好武器会被释放给双火小人
    /// - 高评级殖民者优先获得高价值护甲，且匹配角色偏好（重甲/轻甲）
    ///
    /// 流程：
    /// 1. 收集所有非征召、非锁定殖民者，按战斗价值降序排序
    /// 2. 武器重配：放下所有武器到地上，按战斗价值降序贪婪分配最佳武器
    /// 3. 护甲重配：放下所有护甲到地上，按护甲内在价值降序逐件分配
    ///    每件护甲分配给"评分最高"的殖民者（评分含角色偏好匹配奖励 + 评级权重）
    ///    避免高评级 Flexible 殖民者抢占 Heavy 殖民者急需的重甲
    /// 4. 副武器与库存仍用 ForceEvaluate（按单 Pawn 评估即可）
    ///
    /// 战斗价值复用 CombatEvaluator.ComputeCombatValue：
    /// 射击等级 × 兴趣乘数 + 近战等级 × 兴趣乘数
    /// 兴趣乘数：无火 1.0，单火 1.5，双火 2.0
    /// </summary>
    public static class GlobalAllocator
    {
        // 候选缓存（手动触发，非 Tick 路径，但仍复用静态字段避免 GC）
        private static readonly List<Pawn> sortedPawns = new List<Pawn>();
        private static readonly List<Thing> candidateWeapons = new List<Thing>();
        private static readonly HashSet<int> assignedWeaponIds = new HashSet<int>();

        // 护甲重配候选缓存
        private static readonly List<Apparel> candidateApparels = new List<Apparel>();
        private static readonly HashSet<int> assignedApparelIds = new HashSet<int>();

        /// <summary>
        /// 全局重配：放下所有殖民者武器与护甲，按战斗价值降序重新分配。
        /// 返回被触发的殖民者数量。
        /// 注意：本方法仅影响装备分配，不修改 pawn.playerSettings.displayOrder，
        ///       即不改变殖民者栏顺序——玩家可放心使用，不会打乱已排好的头像顺序。
        /// </summary>
        public static int ReallocateAll()
        {
            sortedPawns.Clear();
            candidateWeapons.Clear();
            assignedWeaponIds.Clear();
            candidateApparels.Clear();
            assignedApparelIds.Clear();

            // ========== 收集候选殖民者 ==========
            foreach (Map map in Find.Maps)
            {
                foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
                {
                    if (DLCCompat.IsGhoul(pawn)) continue;
                    if (!PawnSuitabilityChecker.CanManageGear(pawn)) continue;
                    if (pawn.Dead || pawn.Downed) continue;
                    // 奴隶不参与全局装备重配（玩家手动装备由玩家负责）
                    if (DLCCompat.IsSlave(pawn)) continue;
                    // 征召中的殖民者正在战斗，不打断（玩家可在规则面板关闭此保护）
                    if (AESettings.reallocateRespectDrafted && pawn.Drafted) continue;

                    CompGearManager comp = pawn.GetComp<CompGearManager>();
                    // 已锁定的殖民者尊重玩家意愿（玩家可在规则面板关闭此保护）
                    if (AESettings.reallocateRespectLocked && (comp == null || comp.locked)) continue;
                    if (comp == null) continue;

                    sortedPawns.Add(pawn);
                }
            }

            if (sortedPawns.Count == 0) return 0;

            // 按战斗价值降序排序：高价值殖民者优先分配
            // 预计算缓存：List.Sort 是 O(n log n) 次比较，避免每次比较重复调用
            // ComputeCombatValue（涉及技能查询与特质查询），50 人约省 300 次重复计算
            var combatValueCache = new Dictionary<Pawn, float>();
            for (int i = 0; i < sortedPawns.Count; i++)
            {
                combatValueCache[sortedPawns[i]] = CombatEvaluator.ComputeCombatValue(sortedPawns[i]);
            }
            sortedPawns.Sort((a, b) => combatValueCache[b].CompareTo(combatValueCache[a]));

            // ========== 武器重配 ==========
            ReallocateWeapons();

            // ========== 护甲重配（按角色偏好分配重甲/轻甲） ==========
            if (AESettings.reallocateApparel)
            {
                ReallocateApparel();
            }

            // ========== 副武器与库存：仍用 ForceEvaluate ==========
            for (int i = 0; i < sortedPawns.Count; i++)
            {
                Pawn pawn = sortedPawns[i];
                CompGearManager comp = pawn.GetComp<CompGearManager>();
                if (comp == null) continue;

                comp.ForceEvaluate(CompGearManager.ReloadTarget.Sidearm);
                comp.ForceEvaluate(CompGearManager.ReloadTarget.Inventory);
            }

            return sortedPawns.Count;
        }

        /// <summary>
        /// 武器重配：放下所有殖民者武器，按战斗价值降序从地图候选池评分分配。
        /// </summary>
        private static void ReallocateWeapons()
        {
            // ========== 第一遍：放下所有殖民者的当前武器 ==========
            // 设计意图：让无火小人手里的好武器进入地图候选池，供双火小人拾取
            // 玩家可在规则面板关闭此步骤，仅评估地图上已有的武器
            int droppedCount = 0;
            if (AESettings.reallocateDropWeapons)
            {
                for (int i = 0; i < sortedPawns.Count; i++)
                {
                    Pawn pawn = sortedPawns[i];
                    ThingWithComps primary = pawn.equipment?.Primary;
                    if (primary == null) continue;

                    // 生物编码武器：个人绑定，放下后无法被他人拾取，跳过
                    // 玩家可在规则面板关闭此保护（关闭后仍会放下，但他人无法拾取，纯属浪费）
                    if (AESettings.reallocateRespectBiocoded)
                    {
                        var bioApp = primary.TryGetComp<CompBiocodable>();
                        if (bioApp != null && bioApp.Biocoded) continue;
                    }

                    // 放下武器到 Pawn 位置，进入地图候选池
                    ThingWithComps dropped;
                    pawn.equipment.TryDropEquipment(primary, out dropped, pawn.Position, false);
                    if (dropped != null)
                    {
                        droppedCount++;
                        Log.Message($"[AutoEverything] 全局重配: {AEDebug.Label(pawn)} 放下武器 {dropped.LabelShort}");
                    }
                }
                Log.Message($"[AutoEverything] 全局重配: 共 {droppedCount} 把武器已释放到地图候选池");
            }
            else
            {
                Log.Message("[AutoEverything] 全局重配: 已禁用'放下当前武器'，仅评估地图候选池");
            }

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

                    // 生物编码检查：非编码者不能拾取（玩家可关闭此保护，但关闭后仍会被游戏原生拒绝）
                    if (AESettings.reallocateRespectBiocoded)
                    {
                        var bioApp = w.TryGetComp<CompBiocodable>();
                        if (bioApp != null && bioApp.Biocoded && bioApp.CodedPawn != pawn) continue;
                    }

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

                    Log.Message($"[AutoEverything] 全局重配 #{i + 1}: {AEDebug.Label(pawn)} (战斗价值={CombatEvaluator.ComputeCombatValue(pawn):F1}) ← {bestWeapon.LabelShort} (score={bestScore:F1})");
                }
                else
                {
                    Log.Message($"[AutoEverything] 全局重配 #{i + 1}: {AEDebug.Label(pawn)} 无可用武器");
                }

                // 服装由后续 ReallocateApparel 处理
            }
        }

        /// <summary>
        /// 护甲重配：放下所有殖民者护甲，按护甲内在价值降序逐件分配给最需要的殖民者。
        ///
        /// 算法改进（修复"高评级没拿到好护甲"Bug）：
        /// 旧算法：按 Pawn 战斗价值降序，每个 Pawn 贪婪填满所有护甲槽位。
        ///   问题：S 级（Flexible 偏好）会先拿走重甲，导致 A 级（Heavy 偏好）无重甲可用。
        /// 新算法：按护甲内在价值降序，每件护甲分配给"评分最高"的殖民者。
        ///   评分 = GearScorer.ScoreApparel + 角色偏好匹配奖励 + 评级权重
        ///   - 角色偏好匹配奖励：Heavy 偏好+重甲 = +matchBonus，Light 偏好+轻甲 = +matchBonus
        ///     让匹配偏好的殖民者天然胜过 Flexible 偏好
        ///   - 评级权重：同分时高评级优先（权重小，仅打破平局）
        /// </summary>
        private static void ReallocateApparel()
        {
            // 护甲分配按"全局价值评级"（CombatTier）降序，与武器分配解耦
            // 预计算缓存：避免 O(n log n) 次比较中重复调用 GetCombatTier 与 ComputePawnValueScore
            // （两者均涉及技能/特质查询），50 人约省 300+ 次重复计算
            var tierCache = new Dictionary<Pawn, int>();
            var valueScoreCache = new Dictionary<Pawn, float>();
            for (int i = 0; i < sortedPawns.Count; i++)
            {
                Pawn p = sortedPawns[i];
                tierCache[p] = (int)CombatEvaluator.GetCombatTier(p);
                valueScoreCache[p] = CombatEvaluator.ComputePawnValueScore(p);
            }
            sortedPawns.Sort((a, b) =>
            {
                int ta = tierCache[a];
                int tb = tierCache[b];
                if (ta != tb) return tb.CompareTo(ta);
                return valueScoreCache[b].CompareTo(valueScoreCache[a]);
            });

            // ========== 第一遍：放下所有殖民者的当前护甲 ==========
            // 复用"放下当前武器"开关语义：放下所有护甲进入地图候选池
            int droppedApparelCount = 0;
            if (AESettings.reallocateDropWeapons)
            {
                for (int i = 0; i < sortedPawns.Count; i++)
                {
                    Pawn pawn = sortedPawns[i];
                    if (pawn.apparel?.WornApparel == null) continue;

                    // 复制一份避免在遍历中修改原列表
                    List<Apparel> wornCopy = new List<Apparel>(pawn.apparel.WornApparel);
                    for (int j = 0; j < wornCopy.Count; j++)
                    {
                        Apparel ap = wornCopy[j];

                        // 生物编码护甲：个人绑定，跳过
                        if (AESettings.reallocateRespectBiocoded)
                        {
                            var bioApp = ap.TryGetComp<CompBiocodable>();
                            if (bioApp != null && bioApp.Biocoded) continue;
                        }

                        pawn.apparel.Remove(ap);
                        Thing dropped;
                        if (GenDrop.TryDropSpawn(ap, pawn.Position, pawn.Map, ThingPlaceMode.Near, out dropped))
                        {
                            droppedApparelCount++;
                        }
                    }
                }
                Log.Message($"[AutoEverything] 全局重配护甲: 共 {droppedApparelCount} 件护甲已释放到地图候选池");
            }

            // ========== 收集地图候选护甲 ==========
            foreach (Map map in Find.Maps)
            {
                foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Apparel))
                {
                    Apparel ap = thing as Apparel;
                    if (ap == null) continue;
                    if (ap.WornByCorpse) continue;  // 跳过尸体上的衣物（卫生问题）
                    candidateApparels.Add(ap);
                }
            }

            // ========== 按护甲内在价值降序排序 ==========
            // 让高价值护甲优先分配，确保好护甲落到最需要的殖民者手里
            candidateApparels.Sort(CompareApparelByIntrinsicValueDesc);

            // ========== 逐件分配：每件护甲分配给评分最高的殖民者 ==========
            // 关键改进：避免 S 级 Flexible 殖民者贪婪抢占重甲，
            // 让 Heavy 偏好殖民者通过匹配奖励自然获得重甲
            int totalAssigned = 0;
            for (int j = 0; j < candidateApparels.Count; j++)
            {
                Apparel ap = candidateApparels[j];
                if (ap == null) continue;
                if (assignedApparelIds.Contains(ap.thingIDNumber)) continue;

                Pawn bestPawn = null;
                float bestScore = float.MinValue;
                int bestPawnTier = -1;

                for (int i = 0; i < sortedPawns.Count; i++)
                {
                    Pawn pawn = sortedPawns[i];
                    if (pawn.Map == null) continue;
                    if (pawn.Dead || pawn.Downed) continue;

                    CompGearManager comp = pawn.GetComp<CompGearManager>();
                    if (comp == null) continue;

                    // 检查可达性、保留、禁止
                    if (ap.IsForbidden(pawn)) continue;
                    if (!pawn.CanReserve(ap) || !pawn.CanReach(ap, PathEndMode.ClosestTouch, Danger.Some)) continue;

                    // 检查可穿戴：身体部位 + 不与已穿戴护甲冲突
                    if (!ApparelUtility.HasPartsToWear(pawn, ap.def)) continue;

                    bool conflict = false;
                    List<Apparel> worn = pawn.apparel.WornApparel;
                    for (int k = 0; k < worn.Count; k++)
                    {
                        if (!ApparelUtility.CanWearTogether(worn[k].def, ap.def, pawn.RaceProps.body))
                        {
                            conflict = true;
                            break;
                        }
                    }
                    if (conflict) continue;

                    Role role = comp.CurrentRole;
                    GearContext context = ContextDetector.GetContext(pawn);
                    ArmorPreference pref = RoleDetector.GetArmorPreference(role);

                    // 基础评分
                    float score = GearScorer.ScoreApparel(pawn, ap, role, context);

                    // 重甲判定：ArmorRating_Sharp ≥ 阈值
                    float armorSharp = ap.GetStatValue(StatDefOf.ArmorRating_Sharp);
                    bool isHeavy = armorSharp >= AESettings.heavyArmorSharpThreshold;

                    // 角色偏好调整：
                    // - Heavy 偏好 + 轻甲：大惩罚（-1000，硬否决）
                    // - Light 偏好 + 重甲：大惩罚（-1000，硬否决）
                    // - Heavy 偏好 + 重甲：匹配奖励（+AESettings.heavyArmorMatchBonus，默认 500，让 Heavy 显著胜过 Flexible）
                    // - Light 偏好 + 轻甲：匹配奖励（+AESettings.heavyArmorMatchBonus，默认 500，让 Light 显著胜过 Flexible）
                    // - Flexible：无调整（既不奖励也不惩罚）
                    // 设计意图：匹配偏好的殖民者优先获得对应类型护甲，
                    //   避免 Flexible 殖民者抢走 Heavy 殖民者急需的重甲
                    if (pref == ArmorPreference.Heavy && !isHeavy)
                        score += AESettings.heavyArmorPenaltyForLight;
                    else if (pref == ArmorPreference.Light && isHeavy)
                        score += AESettings.lightArmorPenaltyForHeavy;
                    else if ((pref == ArmorPreference.Heavy && isHeavy)
                             || (pref == ArmorPreference.Light && !isHeavy))
                        score += AESettings.heavyArmorMatchBonus;  // 匹配奖励（默认 500，让匹配偏好显著胜过 Flexible）

                    // 评级权重：同分时高评级优先
                    // 0.5 分/档 × 7 档 = 3.5（最大值），远小于匹配奖励(500)与惩罚(1000)
                    CombatTier pawnTier = CombatEvaluator.GetCombatTier(pawn);
                    score += (float)pawnTier * 0.5f;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestPawn = pawn;
                        bestPawnTier = (int)pawnTier;
                    }
                }

                // 评分过低（≤ 0）表示无殖民者适合此护甲，跳过
                if (bestPawn != null && bestScore > 0f)
                {
                    assignedApparelIds.Add(ap.thingIDNumber);
                    candidateApparels[j] = null;

                    var job = JobMaker.MakeJob(JobDefOf.Wear, ap);
                    bestPawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);

                    totalAssigned++;
                    Log.Message($"[AutoEverything] 全局重配护甲 #{totalAssigned}: {AEDebug.Label(bestPawn)} ← {ap.LabelShort} (score={bestScore:F1})");
                }
            }

            Log.Message($"[AutoEverything] 全局重配护甲完成: 共分配 {totalAssigned} 件护甲");
        }

        /// <summary>
        /// 按护甲内在价值（锐器护甲 + 钝器护甲×0.5）降序比较。
        /// 用于让高价值护甲优先进入分配流程。
        /// </summary>
        private static int CompareApparelByIntrinsicValueDesc(Apparel a, Apparel b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return 1;
            if (b == null) return -1;

            float va = a.GetStatValue(StatDefOf.ArmorRating_Sharp)
                     + a.GetStatValue(StatDefOf.ArmorRating_Blunt) * 0.5f;
            float vb = b.GetStatValue(StatDefOf.ArmorRating_Sharp)
                     + b.GetStatValue(StatDefOf.ArmorRating_Blunt) * 0.5f;
            return vb.CompareTo(va);
        }
    }
}