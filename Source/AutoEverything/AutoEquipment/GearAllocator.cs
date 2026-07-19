using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using AutoEverything.Core;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.AutoEquipment
{
    /// <summary>
    /// 装备分配主入口：事件驱动 + ITab 勾选触发的全局装备重分配。
    ///
    /// 设计模式：静态类，状态包括 IsDirty 脏标与 lastTick 冷却。
    /// 由 AutoExecutor 评估周期开头检查 IsDirty 并调用 AllocateAll。
    ///
    /// 分配算法（贪心 + 层级独占）：
    /// 1. 收集候选装备（地图上未穿戴 + 玩家阵营 Pawn 已穿戴）
    /// 2. 收集参与 Pawn（殖民者 + 奴隶，排除食尸鬼），按 CombatTier 降序排
    /// 3. 对每个 Pawn，按 ApparelLayer 分组选最高分 apparel：
    ///    - X 档（禁止暴力）跳过
    ///    - 同层已穿戴更优 apparel 则跳过该层
    ///    - 否则卸下旧 apparel + 装备新 apparel（含从他人身上扒装）
    /// 4. 高评级 Pawn 先分配，确保优质装备优先给关键角色
    ///
    /// 触发条件（不引入 Tick 检查策略）：
    /// - ITab 勾选切换：立即执行一次（TriggerGearNow）
    /// - 事件驱动：Harmony Postfix 设置 IsDirty=true，AutoExecutor 周期去抖执行
    ///   事件：Thing.SpawnSetup / Thing.Destroy / Pawn.SetFaction / Pawn.Kill
    /// </summary>
    internal static class GearAllocator
    {
        /// <summary>
        /// 脏标：事件触发时置 true，AutoExecutor 周期检查并执行分配后清零。
        /// volatile：事件 Postfix 与 AutoExecutor.Tick 可能跨线程（实际同主线程，但语义明确）
        /// </summary>
        public static bool IsDirty;

        // 上次执行 tick：冷却控制，避免短时间内连续重分配造成卡顿
        private static int lastAllocateTick = -9999;

        // 冷却：2500 tick ≈ 42 秒（与工作重配一致，避免抖动）
        private const int AllocateCooldown = 2500;

        // 错误去重 salt：0xA800 段为 GearAllocator 保留
        private const int AllocateErrorSalt = 0xA800;

        // 复用缓冲区：避免 Tick 路径 new List<>()
        private static readonly List<Apparel> layerBestBuffer = new List<Apparel>();
        private static readonly List<Apparel> wornCopyBuffer = new List<Apparel>();

        /// <summary>
        /// 由 AutoExecutor 调用：检查脏标 + 冷却，若到期则执行一次全局分配。
        /// 不弹消息（避免周期路径刷屏），仅 AEDebug.Log。
        /// </summary>
        /// <returns>是否实际执行了分配</returns>
        public static bool TryAllocateFromTick()
        {
            if (!IsDirty) return false;
            int tick = Find.TickManager.TicksGame;
            if (tick - lastAllocateTick < AllocateCooldown) return false;
            // 战斗中不执行：扒装会取消 Job，可能打断医疗/手术
            if (AnyCombatActive()) return false;

            IsDirty = false;
            lastAllocateTick = tick;
            ExecuteAllocation(tick, showMessage: false);
            return true;
        }

        /// <summary>
        /// ITab 勾选切换时调用：立即执行全局装备分配并弹消息框。
        /// 不受冷却限制（玩家主动触发）。
        /// </summary>
        public static void TriggerGearNow()
        {
            int tick = Find.TickManager.TicksGame;
            lastAllocateTick = tick;
            IsDirty = false;
            ExecuteAllocation(tick, showMessage: true);
        }

        /// <summary>
        /// 标记脏状态：由 Harmony 事件补丁调用（SpawnSetup/Destroy/SetFaction/Kill）。
        /// 仅设置标志，不立即执行——由 AutoExecutor 周期去抖执行。
        /// </summary>
        public static void MarkDirty()
        {
            if (!AESettings.autoEquipmentEnabled) return;
            IsDirty = true;
        }

        /// <summary>
        /// 执行全局装备分配的核心逻辑。
        /// </summary>
        private static void ExecuteAllocation(int tick, bool showMessage)
        {
            if (!AESettings.enabled) return;
            if (!AESettings.autoEquipmentEnabled) return;

            try
            {
                GearInventoryService.ResetAllocation();
                List<Apparel> candidateApparel = GearInventoryService.CollectCandidateApparel();
                List<Pawn> candidatePawns = GearInventoryService.CollectCandidatePawns();

                // 候选 Pawn 在 GearInventoryService.CollectCandidateApparel 内已收集（复用同一缓冲）
                // 但 CollectCandidateApparel 返回前已填好 candidatePawnBuffer，此处再次拿引用
                // 注意：candidatePawnBuffer 在 CollectCandidateApparel 内被填充，此处无需重复调用

                // 按 CombatTier 降序排序（高评级先选优质装备）
                // 用 Comparer 排序，避免 LINQ
                candidatePawns.Sort(PawnTierComparer.Instance);

                int allocatedCount = 0;
                for (int i = 0; i < candidatePawns.Count; i++)
                {
                    Pawn pawn = candidatePawns[i];
                    if (pawn == null || pawn.Dead || !pawn.Spawned) continue;
                    if (DLCCompat.IsGhoul(pawn)) continue;

                    CombatTier tier = CombatEvaluator.GetCombatTier(pawn);
                    if (tier == CombatTier.X) continue; // 禁止暴力者跳过战斗装备分配

                    Role role = RoleDetector.DetectRole(pawn);
                    ArmorPreference armorPref = RoleDetector.GetArmorPreference(role);

                    if (AllocateForPawn(pawn, role, armorPref, candidateApparel))
                    {
                        allocatedCount++;
                    }
                }

                AEDebug.Log(() => $"[AutoExecutor] 自动装备分配: {allocatedCount}/{candidatePawns.Count} 个殖民者 (tick={tick})");
                if (showMessage)
                {
                    Messages.Message("AE_AutoGear_AllocateResult".Translate(allocatedCount, candidatePawns.Count),
                        MessageTypeDefOf.TaskCompletion);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] 自动装备分配失败: " + ex.Message, AllocateErrorSalt);
            }
        }

        /// <summary>
        /// 为单个 Pawn 分配装备：按层选最高分 apparel，必要时卸旧换新。
        /// </summary>
        /// <returns>是否实际装备了至少一件新 apparel</returns>
        private static bool AllocateForPawn(Pawn pawn, Role role, ArmorPreference armorPref, List<Apparel> candidateApparel)
        {
            bool anyAllocated = false;

            // 收集该 Pawn 已穿戴的 apparel（按层索引便于查找）
            // 同层可能有多件 apparel（不同 bodyPartGroup），但简化为"层"维度
            // 注：WornApparel 在迭代中可能被 TryRemove 修改，先复制到缓冲
            wornCopyBuffer.Clear();
            if (pawn.apparel != null)
            {
                List<Apparel> worn = pawn.apparel.WornApparel;
                for (int i = 0; i < worn.Count; i++)
                {
                    wornCopyBuffer.Add(worn[i]);
                }
            }

            // 按层分组处理：同一 ApparelLayer 只选一件最佳
            // 注意：apparel 可能跨多层（如防弹背心覆盖 Shell+OnFace），按第一层归类即可
            HashSet<Def> processedLayers = new HashSet<Def>();
            for (int i = 0; i < candidateApparel.Count; i++)
            {
                Apparel candidate = candidateApparel[i];
                if (candidate == null) continue;
                if (GearInventoryService.IsAllocated(candidate)) continue;

                ApparelProperties props = candidate.def?.apparel;
                if (props == null || props.layers == null || props.layers.Count == 0) continue;

                // 取第一层作为归类键（多数 apparel 只有单层）
                ApparelLayerDef layerKey = props.layers[0];
                if (processedLayers.Contains(layerKey)) continue;
                processedLayers.Add(layerKey);

                // 在该层找最高分 apparel
                Apparel best = FindBestForLayer(pawn, role, armorPref, candidateApparel, layerKey, wornCopyBuffer);
                if (best == null) continue;

                // 与已穿戴的同层 apparel 比较
                Apparel currentWorn = FindWornByLayer(wornCopyBuffer, layerKey);
                float bestScore = GearScorer.ComputeScore(pawn, best, role, armorPref);
                float currentScore = currentWorn != null
                    ? GearScorer.ComputeScore(pawn, currentWorn, role, armorPref)
                    : float.MinValue;

                // 仅当新 apparel 明显更优（差值 > 阈值）才替换，避免抖动
                if (bestScore - currentScore <= AESettings.geReplaceThreshold) continue;

                // 执行替换：先卸下旧 apparel，再装备新 apparel
                if (currentWorn != null)
                {
                    if (!TrySafeRemove(pawn, currentWorn)) continue;
                }

                // 若新 apparel 在他人身上，先扒下来
                // RimWorld 1.6 中 Apparel.Wearer 属性返回穿戴者（无穿戴者时为 null）
                Pawn wearer = best.Wearer;
                if (wearer != null && wearer != pawn)
                {
                    if (!TrySafeRemove(wearer, best)) continue;
                }

                // 装备新 apparel
                if (!TrySafeEquip(pawn, best))
                {
                    // 装备失败：把刚卸下的旧 apparel 装回（best effort，失败也不阻塞）
                    if (currentWorn != null) TrySafeEquip(pawn, currentWorn);
                    continue;
                }

                GearInventoryService.MarkAllocated(best);
                anyAllocated = true;
            }

            return anyAllocated;
        }

        /// <summary>
        /// 在指定层中找最高分的未占用 apparel。
        /// </summary>
        private static Apparel FindBestForLayer(Pawn pawn, Role role, ArmorPreference armorPref,
            List<Apparel> candidates, ApparelLayerDef layer, List<Apparel> wornCopy)
        {
            Apparel best = null;
            float bestScore = float.MinValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                Apparel candidate = candidates[i];
                if (candidate == null) continue;
                if (GearInventoryService.IsAllocated(candidate)) continue;
                ApparelProperties props = candidate.def?.apparel;
                if (props == null || props.layers == null || props.layers.Count == 0) continue;
                if (props.layers[0] != layer) continue;

                // 跳过该 Pawn 已穿戴的同一件 apparel
                if (wornCopy != null && wornCopy.Contains(candidate)) continue;

                // 检查 apparel 是否能被 pawn 穿戴：Pawn 是否有对应的身体部位
                // RimWorld 1.6 中 PawnApparelGenerator.CanWearApparelDef 不存在，改用 ApparelUtility.HasPartsToWear
                if (!ApparelUtility.HasPartsToWear(pawn, candidate.def)) continue;

                float score = GearScorer.ComputeScore(pawn, candidate, role, armorPref);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return best;
        }

        /// <summary>
        /// 在 Pawn 已穿戴列表中找指定层的 apparel。
        /// </summary>
        private static Apparel FindWornByLayer(List<Apparel> worn, ApparelLayerDef layer)
        {
            if (worn == null) return null;
            for (int i = 0; i < worn.Count; i++)
            {
                ApparelProperties props = worn[i]?.def?.apparel;
                if (props == null || props.layers == null || props.layers.Count == 0) continue;
                if (props.layers[0] == layer) return worn[i];
            }
            return null;
        }

        /// <summary>
        /// 安全卸装：包裹 try-catch，防止单件 apparel 卸载失败阻塞整个分配。
        /// RimWorld 1.6 API：
        /// - Apparel.Wearer 返回穿戴者（取代旧版 WornByPawn）
        /// - Pawn_ApparelTracker.Remove(Apparel) 直接卸下（不返回掉落物，自动 spawn 到 pawn 位置）
        /// </summary>
        private static bool TrySafeRemove(Pawn pawn, Apparel apparel)
        {
            if (pawn?.apparel == null || apparel == null) return false;
            try
            {
                // 若 apparel 当前不由 pawn 穿戴，视为已成功卸下
                if (apparel.Wearer != pawn) return true;
                pawn.apparel.Remove(apparel);
                return true;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[AutoEverything] 卸装失败 pawn={pawn.LabelShort} apparel={apparel.def?.defName}: {ex.Message}",
                    pawn.thingIDNumber ^ apparel.thingIDNumber ^ 0xA810);
                return false;
            }
        }

        /// <summary>
        /// 安全装备：包裹 try-catch，防止单件 apparel 装备失败阻塞整个分配。
        /// RimWorld 1.6 API：
        /// - Pawn_ApparelTracker.Wear(Apparel, true) 装备 apparel，自动 DeSpawn
        ///   返回值 void；若同层已有 apparel 会自动替换掉落到 pawn 位置
        /// </summary>
        private static bool TrySafeEquip(Pawn pawn, Apparel apparel)
        {
            if (pawn?.apparel == null || apparel == null) return false;
            try
            {
                // apparel 必须处于 Spawned 状态才能 Wear
                // 若刚从他人身上扒下，Remove 会 Spawn 到 wearer 位置
                if (!apparel.Spawned)
                {
                    // 极少情况：apparel 在容器/库存中（如装备堆叠在仓鼠笼内）
                    // 简化：跳过非 Spawned apparel 的分配
                    return false;
                }
                // Wear 第二参数 lockApparel=true 表示装备后锁定（与玩家手动装备行为一致）
                // 同层已有 apparel 时会自动替换并掉落
                pawn.apparel.Wear(apparel, true);
                return apparel.Wearer == pawn;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[AutoEverything] 装备失败 pawn={pawn.LabelShort} apparel={apparel.def?.defName}: {ex.Message}",
                    pawn.thingIDNumber ^ apparel.thingIDNumber ^ 0xA820);
                return false;
            }
        }

        /// <summary>
        /// 战斗检测：与 AutoExecutor.AnyCombatActive 一致的实现（避免跨类可见性问题）。
        /// </summary>
        private static bool AnyCombatActive()
        {
            foreach (Map map in Find.Maps)
            {
                if (map.mapPawns == null) continue;
                IReadOnlyList<Pawn> allPawns = map.mapPawns.AllPawnsSpawned;
                for (int i = 0; i < allPawns.Count; i++)
                {
                    Pawn p = allPawns[i];
                    if (p.Downed || p.Dead) continue;
                    if (p.HostileTo(Faction.OfPlayer)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Pawn 评级降序比较器：CombatTier 高的在前，同档按 CombatValue 降序。
        /// 奴隶与殖民者统一参与（不区分），由 CollectCandidatePawns 已收集。
        /// </summary>
        private sealed class PawnTierComparer : IComparer<Pawn>
        {
            public static readonly PawnTierComparer Instance = new PawnTierComparer();

            public int Compare(Pawn a, Pawn b)
            {
                if (a == b) return 0;
                if (a == null) return 1;
                if (b == null) return -1;

                CombatTier ta = CombatEvaluator.GetCombatTier(a);
                CombatTier tb = CombatEvaluator.GetCombatTier(b);
                // 降序：tier 值大的在前
                int cmp = ((int)tb).CompareTo((int)ta);
                if (cmp != 0) return cmp;

                // 同档按战斗价值降序
                float va = CombatEvaluator.ComputeCombatValue(a);
                float vb = CombatEvaluator.ComputeCombatValue(b);
                return vb.CompareTo(va);
            }
        }
    }
}
