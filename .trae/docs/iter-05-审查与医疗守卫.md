# 迭代 05：审查与医疗守卫

## 变动日期

2026-07-20

## 需求清单

- [x] 继续分析完善修复（用户指令）
- [x] 审查 HarmonyPatches.cs 完整内容
- [x] 审查 CombatEvaluator.cs（配偶豁免递归风险、缓存一致性）
- [x] 审查 AutoExecutor.cs 完整内容
- [x] 确认 ShouldDowngradeForResearch 不检查 BasicWorker 是否为 bug
- [x] 修复 AssignWork 末尾清理逻辑注释与代码不一致
- [x] 修复 GearInventoryService.CollectCandidatePawns 缺少医疗守卫
- [x] make test-check 验证全部测试通过

## 迭代目标

继续对 AutoEverything 模块进行系统审查，找出潜在 bug 与注释不一致问题，确保各模块在边界场景下的行为正确。

## 改动文件清单

### 1. `Source/AutoEverything/AutoWork/WorkAllocator.Assignment.cs`

**问题**：`AssignWork` 末尾清理逻辑（L289-299）注释说"无火者给0"，但 `ApplySkillFloor` 会把高技能无火者的 0 提升到 2（技能≥12）或 3（技能≥8），注释与代码行为不一致。

**修复**：
- 更新 L289 注释为"硬上限跳过的满载者：有火者给 Floor 保底，无火者给 0"
- 新增 L290-291 注释说明 `ApplySkillFloor` 的保底效果
- 更新 L300 注释为"技能等级保底：相关技能≥12 者 priority 不低于 2，≥8 者不低于 3"（原注释只提≥8，遗漏≥12）

### 2. `Source/AutoEverything/AutoEquipment/GearInventoryService.cs`

**问题**：`CollectCandidatePawns` 不跳过医疗中/休养中的 Pawn，而 `WorkAllocator.ReallocateAll` 跳过。扒装（`TryDrop`/`Wear`）会取消当前 Job，可能打断手术/治疗/休养，与 `WorkAllocator` 行为不一致。

**根因分析**：
- `PawnJobGuard.ShouldSkipForMedical` 检查：医生执行医疗 Job（TendPatient/TendEntity/Rescue/TakeToBedToOperate/DoBill(Bill_Medical)）或伤员床上休养
- `WorkAllocator.ReallocateAll` 在收集候选时调用此守卫跳过医疗中 Pawn（规则：SetPriority 取消 Job 打断医疗）
- `GearAllocator` 的 `TryDrop`/`Wear` 同样会取消 Job，但 `CollectCandidatePawns` 未调用守卫
- 虽有 `AnyCombatActive` 战斗过滤，非战斗时仍有医疗 Job（手术后休养、疾病治疗），需守卫保护

**修复**：
- 殖民者收集（L99-109）：添加 `if (PawnJobGuard.ShouldSkipForMedical(pawn)) continue;`
- 奴隶收集（L120-128）：同样添加医疗守卫（奴隶医疗中也不应被扒装打断）
- 更新方法注释（L88）：新增"排除：医疗中/休养中的 Pawn"说明

## 关键决策与依据

### 决策 1：`ShouldDowngradeForResearch` 不检查 `BasicWorker` 是设计合理

**初始假设**：`BasicWorker` 包含 `Hauling` workTag，priority=1 会打断研究，应纳入 `ShouldDowngradeForResearch` 检查。

**实际验证**：查阅 RimWorld 1.6 `Data/Core/Defs/WorkTypeDefs/WorkTypes.xml`：
- `BasicWorker` workTags：`Commoner` + `AllWork`，**不含 `Hauling`**
- `Hauling` workTags：`ManualDumb` + `Hauling` + `Commoner` + `AllWork`
- `Cleaning` workTags：`ManualDumb` + `Cleaning` + `Commoner` + `AllWork`

**结论**：`BasicWorker` 职责是"释放囚犯 + 开关机器"等基础任务，任务量小且不包含搬运，priority=1 不会频繁打断研究。`ShouldDowngradeForResearch` 只检查 `Hauling`/`Cleaning` 是合理设计，**不是 bug**。

### 决策 2：装备分配需医疗守卫

**依据**：
- `PawnJobGuard` 类注释明确说"所有会修改 Pawn 状态的全局入口复用"
- `TryDrop`/`Wear` 会触发 Job 重评估，取消当前 Job
- `WorkAllocator.ReallocateAll` 已用此守卫，`GearInventoryService.CollectCandidatePawns` 应保持一致
- 安全优先：打断医疗 Job 的代价（手术死循环、伤员失救）大于装备优化的收益

## 代码实现情况

### 审查范围（未发现问题）

1. **HarmonyPatches.cs**：事件 Postfix 简洁有效，`Thing_SpawnSetup_Patch`/`Thing_Destroy_Patch` 正确检查 `pawn.Faction == Faction.OfPlayer`（覆盖殖民者+奴隶），`Pawn_SetFaction_Patch`/`Pawn_Kill_Patch` 用 `CanManageGear` 宽松判断（依赖 AutoExecutor 周期去抖）

2. **CombatEvaluator.cs**：配偶豁免递归被正确切断（`HasSpouseTierAtLeast` 用 `GetAutoCombatTier(spouse)` 而非 `GetCombatTier`，因 `GetAutoCombatTier` 不调用 `HasSpouseTierAtLeast`）。三层方法分工明确：`GetCombatTier`（自定义>系统）/`GetSystemTier`（自动+配偶豁免）/`GetAutoCombatTier`（纯自动）

3. **AutoExecutor.cs**：`AnyCombatActive` 与 `GearAllocator.AnyCombatActive` 实现一致（避免跨类可见性问题重复实现）。`TryTick` 首次初始化守卫正确（`work.lastTick < 0` 时设为 tick 并 return）。`CountAllHumanlikeSpawned` 过滤条件与 `PawnMarker.IsMarkableTarget` 一致

4. **GearAllocator.cs**：`sortedPrefsBuffer` 预扫描与主循环跳过条件完全一致（`null/Dead/!Spawned/Ghoul/X 档`），`specIndex` 索引对齐正确。扒装失败恢复逻辑正确（扒装 best 失败时把 currentWorn 装回）。`TrySafeEquip` 装备失败也装回 currentWorn

5. **GearScorer.cs**：4 个 Core 方法纯逻辑正确，原方法外壳正确调用 `apparel.GetStatValue` 收集数值后委托 Core

6. **CultureChecker.cs**：`ViolatesIdeoRequirement` 与 `MatchesIdeoRequirement` 逻辑正确，`ModsConfig.IdeologyActive` 检查到位

7. **ITab_GearManager.cs**：Mark 勾选框双向触发（`autoMarkPawn != prevMark`），Gear 勾选框单向触发（`autoEquipmentEnabled && autoEquipmentEnabled != prevGear`），与 `TriggerMarkNow`/`TriggerGearNow` 注释一致

## 测试验证结果

```
make test-check
=== AutoEverything.Tests ===
[ApplySkillFloorCoreTests] 30/30 passed
[EvaluateAutoTierCoreTests] 32/32 passed
[PawnMarkerTests/Category] 14/14 passed
[PawnMarkerTests/Color] 20/20 passed
[PawnMarkerTests/FormatMessage] 29/29 passed
[PawnMarkerTests/ComputeNewlyMarked] 38/38 passed
[GearAllocatorTests/EmptyBoundary] 4/4 passed
[GearAllocatorTests/AllFlexible] 9/9 passed
[GearAllocatorTests/Mixed] 12/12 passed
[GearAllocatorTests/LightNeverUpgrade] 15/15 passed
[GearAllocatorTests/HeavyAlreadyOccupied] 18/18 passed
[GearAllocatorTests/PriorityOrder] 21/21 passed
[GearScorerTests/ArmorScore] 7/7 passed
[GearScorerTests/LayerMatch] 20/20 passed
[GearScorerTests/Insulation] 34/34 passed
[GearScorerTests/MovementPenalty] 48/48 passed
All tests passed.
```

- 编译：零警告零错误
- 测试：351 个测试全部通过

## 遗留事项

- 游戏内验证待用户执行（开发环境无法启动 RimWorld）
- 配偶评级未走 `TierCacheService` 缓存：`HasSpouseTierAtLeast` 调用 `GetAutoCombatTier(spouse)` 直接计算，每次 `GetCombatTier(pawn)` 都会触发配偶评级完整计算。性能影响可接受（配偶数量少），暂不优化
- `GearAllocator.PawnTierComparer` 不走 `TierCacheService`：排序时每个 Pawn 调用 `GetCombatTier` + `ComputeCombatValue`，N 个 Pawn 调用 2N 次。可优化为走 cache，但需确认 `TierCacheService.GetTier` 返回的是 `GetCombatTier` 还是 `GetSystemTier`

## 下一轮计划

- 等待用户游戏内验证反馈
- 如有新 bug 报告，继续修复
- 可考虑优化配偶评级缓存（低优先级）
