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
        private static readonly List<Apparel> wornCopyBuffer = new List<Apparel>();
        // 已处理层缓冲区：AllocateForPawn 每次调用复用，避免每个 Pawn new HashSet<Def>
        private static readonly HashSet<Def> processedLayersBuffer = new HashSet<Def>();
        // 顺延名额计算输入缓冲区：按 CombatTier 排序后收集每个候选 Pawn 的 ArmorPreference
        // 用途：传给纯逻辑方法 ComputeHeavyUpgradeFlags 计算升级标志，避免在主循环里重复调用 DetectRole
        private static readonly List<ArmorPreference> sortedPrefsBuffer = new List<ArmorPreference>();
        // 本轮被升级为 Heavy 的 Pawn 集合：扒装守卫用此判断 wearer 的有效偏好
        // 每轮分配开始时 Clear()，主循环中遇到 upgrade=true 时 Add
        // 用途：ShouldStealFromWearer 需用 wearer 的"有效偏好"（含升级）计算得分，否则升级 Flexible 的得分被低估导致误扒
        private static readonly HashSet<Pawn> upgradedPawns = new HashSet<Pawn>();

        // 装备分配过程统计：每轮 ExecuteAllocation 开头清零，结束时输出分类跳过数便于调试
        // statsSkipOscillation=防振荡跳过, statsSkipStealGuard=扒装守卫拒绝, statsSkipThreshold=阈值不足
        private static int statsSkipOscillation;
        private static int statsSkipStealGuard;
        private static int statsSkipThreshold;

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
                // 清空升级集合：扒装守卫依赖此集合判断 wearer 有效偏好
                upgradedPawns.Clear();
                // 清零过程统计：本轮结束时输出分类跳过数
                statsSkipOscillation = 0;
                statsSkipStealGuard = 0;
                statsSkipThreshold = 0;
                // 显式调用一次 CollectCandidatePawns，传给 CollectCandidateApparel 复用，
                // 避免内部重复调用导致缓冲区翻倍或浪费 CPU
                List<Pawn> candidatePawns = GearInventoryService.CollectCandidatePawns();
                List<Apparel> candidateApparel = GearInventoryService.CollectCandidateApparel(candidatePawns);

                // 优先级顺延检测：基于"重甲数量 vs Heavy Pawn 数量"计算剩余重甲名额
                // 设计意图：
                //   1. 用户期望"重甲够不够殖民者数量"决定顺延——而非简单"有无 Heavy Pawn"
                //   2. 斥候装甲等低 mass 中等护甲装备对后排也适用，不应粗暴全部升级为 Heavy 丢失自由选择能力
                //   3. 重甲名额按 CombatTier 降序分配给 Flexible Pawn；分到名额的升级为 Heavy，没分到的保持 Flexible 自由选择
                // Light（Worker/Doctor/Pacifist）始终保持 Light，不参与顺延（保工作效率）
                int heavyArmorCount = CountHeavyArmor(candidateApparel);

                // 按 CombatTier 降序排序（高评级先选优质装备）
                // 用 Comparer 排序，避免 LINQ
                candidatePawns.Sort(PawnTierComparer.Instance);

                // 排序后预扫描：收集每个候选 Pawn 的 ArmorPreference 并统计 Heavy 数
                // 与主循环使用相同的跳过条件（Ghoul/X 档），确保索引对齐
                // 一次遍历同时完成 pref 收集 + heavyPawnCount 统计，避免重复调用 DetectRole
                sortedPrefsBuffer.Clear();
                int heavyPawnCount = 0;
                for (int i = 0; i < candidatePawns.Count; i++)
                {
                    Pawn p = candidatePawns[i];
                    if (p == null || p.Dead || !p.Spawned) continue;
                    if (DLCCompat.IsGhoul(p)) continue;
                    CombatTier t = CombatEvaluator.GetCombatTier(p);
                    if (t == CombatTier.X) continue;
                    Role r = RoleDetector.DetectRole(p);
                    ArmorPreference pref = RoleDetector.GetArmorPreference(r);
                    sortedPrefsBuffer.Add(pref);
                    if (pref == ArmorPreference.Heavy) heavyPawnCount++;
                }

                // 纯逻辑方法计算每个候选 Pawn 的 Heavy 升级标志（便于单元测试）
                bool[] upgradeFlags = ComputeHeavyUpgradeFlags(heavyArmorCount, heavyPawnCount, sortedPrefsBuffer);

                // 统计本轮升级的 Flexible Pawn 数（upgradeFlags 中 true 的数量）
                int upgradeCount = 0;
                for (int i = 0; i < upgradeFlags.Length; i++)
                    if (upgradeFlags[i]) upgradeCount++;

                // 开始日志：输出本轮分配的完整输入参数，便于玩家追踪整体流程
                AEDebug.Log(() =>
                    $"[GearAllocator] 开始装备分配: {candidatePawns.Count} Pawn, {candidateApparel.Count} 件装备, 重甲 {heavyArmorCount}, Heavy Pawn {heavyPawnCount}, 升级 {upgradeCount} (tick={tick})");

                // 候选 Pawn 列表日志：输出每个有效候选 Pawn 的评级/名字/偏好/升级标志
                // 用途：玩家发现"某 Pawn 没分到装备"时，可从此日志判断该 Pawn 是否在候选中
                // 若不在列表中 → 被 CollectCandidatePawns 排除（Ghoul/X 档/Dead/医疗中/非殖民者非奴隶）
                // 若在列表中但没换装 → 看后续决策点日志（阈值不足/防振荡/扒装拒绝）
                AEDebug.Log(() =>
                {
                    var sb = new System.Text.StringBuilder();
                    sb.Append("[GearAllocator] 候选 Pawn: ");
                    int prefIdx = 0;
                    for (int i = 0; i < candidatePawns.Count; i++)
                    {
                        Pawn p = candidatePawns[i];
                        if (p == null || p.Dead || !p.Spawned) continue;
                        if (DLCCompat.IsGhoul(p)) continue;
                        CombatTier t = CombatEvaluator.GetCombatTier(p);
                        if (t == CombatTier.X) continue;
                        ArmorPreference pref = sortedPrefsBuffer[prefIdx];
                        bool upgrade = upgradeFlags[prefIdx];
                        prefIdx++;
                        sb.Append(t).Append('#').Append(p.LabelShort).Append(':').Append(pref);
                        if (upgrade) sb.Append("↑");
                        sb.Append(' ');
                    }
                    return sb.ToString();
                });

                // 预填充 upgradedPawns：在主循环前根据 upgradeFlags 一次性收集本轮被升级的 Pawn
                // 用途：扒装守卫 ShouldStealFromWearer 需用 wearer 本轮的有效偏好（含升级）计算得分
                // 若在主循环中按处理顺序填充，高评级 stealer 先处理时 wearer 还未加入集合，
                // wearer 得分被低估（用基础 Flexible 偏好计算），导致误扒装 → 振荡
                // 预填充后所有 stealer 都能看到完整升级集合，扒装判定对称一致
                {
                    int prefIdx = 0;
                    for (int i = 0; i < candidatePawns.Count; i++)
                    {
                        Pawn p = candidatePawns[i];
                        if (p == null || p.Dead || !p.Spawned) continue;
                        if (DLCCompat.IsGhoul(p)) continue;
                        CombatTier t = CombatEvaluator.GetCombatTier(p);
                        if (t == CombatTier.X) continue;
                        if (upgradeFlags[prefIdx]) upgradedPawns.Add(p);
                        prefIdx++;
                    }
                }

                int allocatedCount = 0;
                int specIndex = 0;
                for (int i = 0; i < candidatePawns.Count; i++)
                {
                    Pawn pawn = candidatePawns[i];
                    if (pawn == null || pawn.Dead || !pawn.Spawned) continue;
                    if (DLCCompat.IsGhoul(pawn)) continue;

                    CombatTier tier = CombatEvaluator.GetCombatTier(pawn);
                    if (tier == CombatTier.X) continue; // 禁止暴力者跳过战斗装备分配

                    // 取预扫描的 ArmorPreference + 升级标志（索引与预扫描对齐）
                    ArmorPreference armorPref = sortedPrefsBuffer[specIndex];
                    bool upgrade = upgradeFlags[specIndex];
                    specIndex++;

                    // 顺延：升级标志为 true 的 Flexible Pawn 临时升级为 Heavy
                    // 仅影响传给 GearScorer 的评分参数（layerMatch 用 Heavy 公式 + movementPenalty 用前排容忍度）
                    // 不修改 RoleDetector 全局判定，不影响 ITab 徽章显示与其他模块
                    // 没分到名额的 Flexible 保持 Flexible，按原评分公式自由选择（斥候装甲等轻量护甲会被选中）
                    Role role = RoleDetector.DetectRole(pawn);
                    Role effectiveRole = role;
                    ArmorPreference effectivePref = armorPref;
                    if (upgrade)
                    {
                        effectivePref = ArmorPreference.Heavy;
                        effectiveRole = Role.Brawler;
                        // upgradedPawns 已在主循环前预填充（避免扒装守卫不对称）
                    }

                    if (AllocateForPawn(pawn, effectiveRole, effectivePref, candidateApparel, showMessage))
                    {
                        allocatedCount++;
                    }
                }

                // 结束日志：输出本轮分配的分类统计（换装成功数 + 三类跳过数），便于玩家排查
                AEDebug.Log(() =>
                    $"[GearAllocator] 装备分配完成: 换装 {allocatedCount}, 防振荡跳过 {statsSkipOscillation}, 扒装拒绝 {statsSkipStealGuard}, 阈值不足 {statsSkipThreshold} (tick={tick})");
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
        /// <param name="showMessage">是否在换装成功时弹消息栏提示（仅 ITab 手动触发时为 true，周期触发为 false 避免刷屏）</param>
        /// <returns>是否实际装备了至少一件新 apparel</returns>
        private static bool AllocateForPawn(Pawn pawn, Role role, ArmorPreference armorPref, List<Apparel> candidateApparel, bool showMessage)
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
            // 复用静态缓冲区：AllocateForPawn 在 ExecuteAllocation 主循环里逐 Pawn 调用，
            // 若每次 new HashSet<Def> 会在 Tick 路径产生 GC，故改为静态复用 + Clear()
            processedLayersBuffer.Clear();
            for (int i = 0; i < candidateApparel.Count; i++)
            {
                Apparel candidate = candidateApparel[i];
                if (candidate == null) continue;
                if (GearInventoryService.IsAllocated(candidate)) continue;

                ApparelProperties props = candidate.def?.apparel;
                if (props == null || props.layers == null || props.layers.Count == 0) continue;

                // 取第一层作为归类键（多数 apparel 只有单层）
                ApparelLayerDef layerKey = props.layers[0];
                if (processedLayersBuffer.Contains(layerKey)) continue;
                processedLayersBuffer.Add(layerKey);

                // 在该层找最高分 apparel
                Apparel best = FindBestForLayer(pawn, role, armorPref, candidateApparel, layerKey, wornCopyBuffer);
                if (best == null) continue;

                // 与已穿戴的同层 apparel 比较
                Apparel currentWorn = FindWornByLayer(wornCopyBuffer, layerKey);

                // 防止 Flexible Pawn 振荡：未升级的 Flexible Pawn 若当前已穿重甲，跳过该层换装
                // 根因：Flexible 升级为 Heavy 时穿重甲（评分高），下一轮没被升级，Flexible 偏好下重甲
                // 评分低（movementPenalty 用 backRowW=2.0，penalty 高），bestScore(轻甲) - currentScore(重甲)
                // 可能 > 阈值 → 换回轻甲；再下一轮又被升级 → 又换回重甲 → 反复换装振荡
                // 修复：未升级 Flexible 保留重甲不脱，消除振荡（重甲对 Flexible 也提供保护，并非无用）
                // 注：armorPref 是 effectivePref（升级后），== Flexible 即"basePref=Flexible 且未升级"
                // 重甲判定标准与 CountHeavyArmor 一致：(Sharp+Blunt) ≥ geHeavyArmorThreshold
                if (armorPref == ArmorPreference.Flexible && currentWorn != null)
                {
                    float curSharp = currentWorn.GetStatValue(StatDefOf.ArmorRating_Sharp);
                    float curBlunt = currentWorn.GetStatValue(StatDefOf.ArmorRating_Blunt);
                    if (curSharp + curBlunt >= AESettings.geHeavyArmorThreshold)
                    {
                        statsSkipOscillation++;
                        AEDebug.Log(() =>
                            $"[GearAllocator] {AEDebug.Label(pawn)} 保留重甲不换[{layerKey.defName}]: {currentWorn.def?.defName} (防振荡, 偏好={armorPref})");
                        continue;
                    }
                }

                float bestScore = GearScorer.ComputeScore(pawn, best, role, armorPref);
                float currentScore = currentWorn != null
                    ? GearScorer.ComputeScore(pawn, currentWorn, role, armorPref)
                    : float.MinValue;

                // 仅当新 apparel 明显更优（差值 > 阈值）才替换，避免抖动
                if (bestScore - currentScore <= AESettings.geReplaceThreshold)
                {
                    statsSkipThreshold++;
                    AEDebug.Log(() =>
                    {
                        string cur = currentWorn?.def?.defName ?? "无";
                        return $"[GearAllocator] {AEDebug.Label(pawn)} 跳过换装[{layerKey.defName}]: {cur} 保留 (差值 {bestScore - currentScore:F1} ≤ 阈值 {AESettings.geReplaceThreshold}, 偏好={armorPref})";
                    });
                    continue;
                }

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
                    // 扒装守卫：仅当 stealer 对该 apparel 的得分高于 wearer 有效得分时才扒装
                    // 防止两个 Pawn 之间反复抢装导致振荡（A 抢 B 的 Y → B 下轮抢回 → 循环）
                    // 根因：原逻辑仅比较 stealer 的"新旧得分差"，未考虑 wearer 的损失
                    if (!ShouldStealFromWearer(wearer, best, bestScore))
                    {
                        // wearer 得分更高或相当：不扒装，把刚卸下的旧 apparel 装回，跳过此层
                        statsSkipStealGuard++;
                        AEDebug.Log(() =>
                            $"[GearAllocator] {AEDebug.Label(pawn)} 放弃扒装[{layerKey.defName}]: {best.def?.defName} 在 {AEDebug.Label(wearer)} 身上 (wearer 得分更高, 偏好={armorPref})");
                        if (currentWorn != null) TrySafeEquip(pawn, currentWorn);
                        continue;
                    }

                    if (!TrySafeRemove(wearer, best))
                    {
                        // 扒装失败：把刚卸下的旧 apparel 装回（best effort），避免 pawn 失去装备
                        if (currentWorn != null) TrySafeEquip(pawn, currentWorn);
                        continue;
                    }
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

                // 调试日志：换装成功时输出 Pawn 名、层、旧装备→新装备、得分变化
                // 用 Func<string> 延迟构造，关闭调试日志时零字符串分配
                AEDebug.Log(() =>
                {
                    string oldName = currentWorn?.def?.defName ?? "无";
                    string newName = best.def?.defName ?? "?";
                    return $"[GearAllocator] {AEDebug.Label(pawn)} 换装[{layerKey.defName}]: {oldName} → {newName} (得分 {currentScore:F1} → {bestScore:F1}, 偏好={armorPref})";
                });

                // 消息栏逐件换装提示（req-08 需求4）：仅 ITab 手动触发时弹出，附带判断依据便于调试
                // 周期触发（showMessage=false）不弹避免刷屏，仅写入 AEDebug.Log
                // 注：用 string.Format 替代多参 Translate，规避 RimWorld 1.6 Translator.Translate(params object[]) 过时警告
                if (showMessage)
                {
                    string oldName = currentWorn?.def?.defName ?? "无";
                    string newName = best.def?.defName ?? "?";
                    string message = string.Format("AE_AutoGear_SwapDetail".Translate(),
                        pawn.LabelShort, layerKey.defName, oldName, newName,
                        currentScore.ToString("F1"), bestScore.ToString("F1"), armorPref);
                    Messages.Message(message, MessageTypeDefOf.TaskCompletion);
                }
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
        /// - Pawn_ApparelTracker.TryDrop(Apparel) 卸下并 spawn apparel 到 pawn 位置（返回是否成功）
        /// - ⚠️ 不能用 Remove(Apparel)：Remove 仅从 WornApparel 列表移除，不 spawn，
        ///   apparel 会变成 unspawned 状态（消失）。曾因误用 Remove 导致"勾选自动装备时身上装备消失"的 bug
        /// </summary>
        private static bool TrySafeRemove(Pawn pawn, Apparel apparel)
        {
            if (pawn?.apparel == null || apparel == null) return false;
            try
            {
                // 若 apparel 当前不由 pawn 穿戴，视为已成功卸下
                if (apparel.Wearer != pawn) return true;
                // TryDrop 会 spawn apparel 到 pawn 位置，确保后续 TrySafeEquip 能 Wear 它
                // （Wear 要求 apparel 处于 Spawned 状态）
                return pawn.apparel.TryDrop(apparel);
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
        /// - Pawn_ApparelTracker.Wear(Apparel, dropReplacedApparel=true) 装备 apparel，自动 DeSpawn
        ///   返回值 void；dropReplacedApparel=true 时同层已有 apparel 会自动 drop 旧装备到 pawn 位置
        /// </summary>
        private static bool TrySafeEquip(Pawn pawn, Apparel apparel)
        {
            if (pawn?.apparel == null || apparel == null) return false;
            try
            {
                // apparel 必须处于 Spawned 状态才能 Wear
                // 若刚从他人身上扒下，TryDrop 会 Spawn 到 wearer 位置（不能误用 Remove）
                if (!apparel.Spawned)
                {
                    // 极少情况：apparel 在容器/库存中（如装备堆叠在仓鼠笼内）
                    // 简化：跳过非 Spawned apparel 的分配
                    return false;
                }
                // Wear 第二参数 dropReplacedApparel=true：同层冲突时自动 drop 旧装备
                // （与玩家手动装备行为一致，旧装备掉落到 pawn 位置而非消失）
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

        /// <summary>
        /// 统计候选 Apparel 中的"重甲"数量。
        /// 判定标准：(Sharp + Blunt) ≥ <see cref="AESettings.geHeavyArmorThreshold"/>
        /// 用途：计算重甲名额，决定多少个 Flexible Pawn 可以顺延升级为 Heavy
        /// 注：不区分 ApparelLayer（简化），按装备总数算；实际分配时按层独立选最高分
        /// </summary>
        private static int CountHeavyArmor(List<Apparel> candidateApparel)
        {
            int count = 0;
            float threshold = AESettings.geHeavyArmorThreshold;
            for (int i = 0; i < candidateApparel.Count; i++)
            {
                Apparel a = candidateApparel[i];
                if (a == null) continue;
                float sharp = a.GetStatValue(StatDefOf.ArmorRating_Sharp);
                float blunt = a.GetStatValue(StatDefOf.ArmorRating_Blunt);
                if (sharp + blunt >= threshold) count++;
            }
            return count;
        }

        /// <summary>
        /// 计算每个候选 Pawn 是否应升级为 Heavy（顺延逻辑核心）。
        ///
        /// 纯逻辑方法，无 RimWorld 依赖，便于单元测试。
        /// 调用方需先把候选 Pawn 按 CombatTier 降序排好，再收集每个 Pawn 的 ArmorPreference
        /// 传入此方法。返回与 sortedPrefs 等长的 bool 数组，true 表示该 Pawn 应升级为 Heavy。
        ///
        /// 算法：
        /// 1. remaining = max(0, heavyArmorCount - heavyPawnCount)
        ///    —— 重甲总数扣除已被 Heavy Pawn 占用的名额，得到可顺延给 Flexible 的剩余名额
        /// 2. 遍历 sortedPrefs（按 CombatTier 降序），遇到 Flexible 且 remaining>0 时升级，名额 -1
        /// 3. Light Pawn 始终不升级（保工作效率）；Heavy Pawn 已占名额无需再升
        ///
        /// 设计意图：
        /// - 重甲够不够殖民者数量决定顺延（而非简单"有无 Heavy Pawn"）
        /// - 斥候装甲等低 mass 中等护甲对后排也适用，没分到名额的 Flexible 保持原评分公式自由选择
        /// - 名额按 CombatTier 降序分配，确保优质重甲优先给高评级角色
        /// </summary>
        /// <param name="heavyArmorCount">候选 Apparel 中的重甲数量（Sharp+Blunt ≥ 阈值）</param>
        /// <param name="heavyPawnCount">候选 Pawn 中 Heavy 偏好者数量（前排 Brawler）</param>
        /// <param name="sortedPrefs">按 CombatTier 降序排好的 ArmorPreference 列表（已剔除 Ghoul/X 档）</param>
        /// <returns>与 sortedPrefs 等长的 bool 数组，true 表示该 Pawn 应升级为 Heavy</returns>
        internal static bool[] ComputeHeavyUpgradeFlags(
            int heavyArmorCount,
            int heavyPawnCount,
            List<ArmorPreference> sortedPrefs)
        {
            int remaining = System.Math.Max(0, heavyArmorCount - heavyPawnCount);
            bool[] flags = new bool[sortedPrefs.Count];
            for (int i = 0; i < sortedPrefs.Count; i++)
            {
                if (sortedPrefs[i] == ArmorPreference.Flexible && remaining > 0)
                {
                    flags[i] = true;
                    remaining--;
                }
            }
            return flags;
        }

        /// <summary>
        /// 扒装守卫：判定是否应从 wearer 扒下 apparel 给当前 Pawn。
        ///
        /// 设计意图：防止两个 Pawn 之间反复抢装导致振荡。
        /// 振荡场景：A 抢 B 的 Y → 下轮 B 抢回 → A 再抢 → 无限循环。
        /// 根因：原逻辑仅比较 stealer 的"新旧得分差"（bestScore - currentScore > 阈值），
        ///       未考虑 wearer 失去该 apparel 的损失。当 stealer 与 wearer 得分接近时，
        ///       双方都会觉得自己"更应该拿"，造成抢装循环。
        ///
        /// 守卫规则：
        /// 1. wearer 不适合装备管理（食尸鬼/X 档/医疗中等）→ 允许扒装，不比较得分
        /// 2. wearer 在候选池中 → 仅当 stealerScore - wearerScore > 替换阈值 时允许扒装
        ///
        /// wearer 有效偏好：若 wearer 在本轮被升级为 Heavy（记于 <see cref="upgradedPawns"/>），
        /// 用 Heavy 偏好计算得分；否则用基础偏好。否则升级 Flexible 的得分被低估，
        /// 会导致误判"stealer 得分更高"而扒装，破坏顺延逻辑。
        /// </summary>
        /// <param name="wearer">当前穿戴 apparel 的 Pawn</param>
        /// <param name="apparel">被考虑扒装的 apparel</param>
        /// <param name="stealerScore">stealer 对该 apparel 的有效得分（已含升级偏好，由调用方传入）</param>
        /// <returns>true 表示允许扒装；false 表示 wearer 得分更高或相当，不应扒装</returns>
        private static bool ShouldStealFromWearer(Pawn wearer, Apparel apparel, float stealerScore)
        {
            if (wearer == null) return true;

            // wearer 不适合装备管理 → 允许扒装（食尸鬼/X 档/医疗中都不需要保装备）
            if (!PawnSuitabilityChecker.CanManageGear(wearer)) return true;
            if (DLCCompat.IsGhoul(wearer)) return true;
            if (CombatEvaluator.GetCombatTier(wearer) == CombatTier.X) return true;
            if (PawnJobGuard.ShouldSkipForMedical(wearer)) return true;

            // wearer 在候选池中：比较有效得分
            Role wearerRole = RoleDetector.DetectRole(wearer);
            ArmorPreference wearerPref = RoleDetector.GetArmorPreference(wearerRole);
            if (upgradedPawns.Contains(wearer))
            {
                // wearer 在本轮被升级为 Heavy：用 Heavy 偏好计算得分，与主循环评分保持一致
                wearerRole = Role.Brawler;
                wearerPref = ArmorPreference.Heavy;
            }
            float wearerScore = GearScorer.ComputeScore(wearer, apparel, wearerRole, wearerPref);
            return ShouldStealFromWearerCore(stealerScore, wearerScore, AESettings.geReplaceThreshold);
        }

        /// <summary>
        /// 扒装守卫纯逻辑核心：比较 stealer 与 wearer 得分。
        /// 抽出便于单元测试，不依赖 Pawn/Apparel 实例。
        ///
        /// 算法：stealerScore - wearerScore > threshold 时允许扒装。
        /// 严格大于（非 ≥）确保得分相当时不扒装，避免边际抢装振荡。
        /// </summary>
        /// <param name="stealerScore">stealer 对 apparel 的有效得分</param>
        /// <param name="wearerScore">wearer 对 apparel 的有效得分</param>
        /// <param name="threshold">替换阈值（与常规替换一致，由 AESettings.geReplaceThreshold 控制）</param>
        /// <returns>true 表示允许扒装</returns>
        internal static bool ShouldStealFromWearerCore(float stealerScore, float wearerScore, float threshold)
        {
            return stealerScore - wearerScore > threshold;
        }
    }
}
