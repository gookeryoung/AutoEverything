# 迭代记录 iter-04：优化修复

## 需求清单

来源：用户反馈"继续优化修复"。

- [x] 修复 `TrySafeEquip` 注释错误（Remove→TryDrop、lockApparel→dropReplacedApparel）
- [x] 修复 `TriggerGearNow` 注释错误（声称"取消勾选也会调用"但实际不调用）
- [x] 消除 `ExecuteAllocation` 中冗余的 `CollectCandidatePawns` 调用（根本解决候选 Pawn 翻倍问题）
- [x] make test-check 全部门禁通过

## 迭代目标

1. 审查 AutoEquipment 模块代码，识别潜在 bug 与可优化点
2. 修正注释与代码不一致问题（曾因注释错误导致装备消失 bug，注释准确性至关重要）
3. 消除冗余调用，简化 API 设计，避免"幂等设计"这类反直觉的临时方案

## 改动文件清单

### 修改源文件

- `Source/AutoEverything/AutoEquipment/GearAllocator.cs`
  - `TrySafeEquip` 注释修正：`Remove` → `TryDrop`、`lockApparel` → `dropReplacedApparel`
  - `ExecuteAllocation` 简化：移除冗余的 `CollectCandidatePawns` 调用，改为先调用一次 `CollectCandidatePawns` 再传给 `CollectCandidateApparel`
- `Source/AutoEverything/AutoEquipment/GearInventoryService.cs`
  - `CollectCandidateApparel` 签名变更：接受 `List<Pawn> candidatePawns` 参数，不再内部调用 `CollectCandidatePawns`
  - `CollectCandidatePawns` 注释更新：从"幂等设计"改为"防御性设计"（正常路径只调用一次，Clear 仅作防御）
- `Source/AutoEverything/Core/AutoExecutor.cs`
  - `TriggerGearNow` 注释修正：移除错误的"取消勾选也会调用本方法"说明

### 新增文档

- `.trae/docs/iter-04-优化修复.md` — 本次迭代记录

## 关键决策与依据

### 1. `TrySafeEquip` 注释修正

`TrySafeEquip` 方法中有两处注释错误：

- 错误 1：注释说"若刚从他人身上扒下，Remove 会 Spawn 到 wearer 位置"，实际 `Remove` 不 spawn（这正是 rule-08 装备消失 bug 的根因）
- 错误 2：注释说"Wear 第二参数 lockApparel=true 表示装备后锁定"，实际第二参数是 `dropReplacedApparel`

修正为：
- "若刚从他人身上扒下，TryDrop 会 Spawn 到 wearer 位置（不能误用 Remove）"
- "Wear 第二参数 dropReplacedApparel=true：同层冲突时自动 drop 旧装备"

依据：RimWorld 1.6 `Pawn_ApparelTracker` 实际 API 签名（rule-08 已通过 PowerShell 反射验证）。

### 2. `TriggerGearNow` 注释修正

`AutoExecutor.TriggerGearNow` 的注释声称"取消勾选也会调用本方法——GearAllocator 内部检测开关后静默返回"，但 ITab 中实际逻辑是：

```csharp
if (AESettings.autoEquipmentEnabled && AESettings.autoEquipmentEnabled != prevGear)
{
    AutoExecutor.TriggerGearNow();
}
```

仅在勾选（false → true）时调用，取消勾选不调用。与 `TriggerMarkNow` 的双向触发不同（Mark 在 `autoMarkPawn != prevMark` 时调用，任一方向都触发）。

修正为："仅在勾选（false → true）时触发；取消勾选不调用（仅停止自动，保留当前装备，无法撤销已分配装备）。"

依据：ITab_GearManager.cs L376-379 实际调用逻辑。

### 3. `ExecuteAllocation` 冗余调用简化

**原设计问题**：
- `CollectCandidateApparel()` 内部调用 `CollectCandidatePawns()` 填充 `candidatePawnBuffer`
- `ExecuteAllocation` 又外部调用 `CollectCandidatePawns()` 拿同一引用
- 两次填充导致缓冲区翻倍（每个 Pawn 出现两次）
- 临时方案：在 `CollectCandidatePawns` 开头加 Clear 使其幂等（rule-07 前的状态）

**根本解决方案**：
- `CollectCandidateApparel` 签名变更：接受 `List<Pawn> candidatePawns` 参数
- `ExecuteAllocation` 显式调用一次 `CollectCandidatePawns`，传给 `CollectCandidateApparel` 复用
- `CollectCandidatePawns` 的 Clear 保留为"防御性设计"（正常路径只调用一次）

依据：Karpathy 四原则之"删除优于扩展"——消除冗余调用比"幂等设计"更简单直观。

### 4. Karpathy 四原则审视

- 简单优于复杂：消除冗余调用比"幂等设计"更简单
- 删除优于扩展：删除 `CollectCandidateApparel` 内部的 `CollectCandidatePawns` 调用，而非加 Clear 兜底
- 理解优于记忆：注释准确描述 API 行为，避免误导后续开发
- 原型优于规划：先审查代码识别问题，再逐一修复验证

## 代码实现情况

### `TrySafeEquip` 注释修正

```csharp
// 修正前
// apparel 必须处于 Spawned 状态才能 Wear
// 若刚从他人身上扒下，Remove 会 Spawn 到 wearer 位置
if (!apparel.Spawned) { return false; }
// Wear 第二参数 lockApparel=true 表示装备后锁定
pawn.apparel.Wear(apparel, true);

// 修正后
// apparel 必须处于 Spawned 状态才能 Wear
// 若刚从他人身上扒下，TryDrop 会 Spawn 到 wearer 位置（不能误用 Remove）
if (!apparel.Spawned) { return false; }
// Wear 第二参数 dropReplacedApparel=true：同层冲突时自动 drop 旧装备
// （与玩家手动装备行为一致，旧装备掉落到 pawn 位置而非消失）
pawn.apparel.Wear(apparel, true);
```

### `ExecuteAllocation` 简化

```csharp
// 修正前
GearInventoryService.ResetAllocation();
List<Apparel> candidateApparel = GearInventoryService.CollectCandidateApparel();
// CollectCandidatePawns 幂等（开头 Clear），CollectCandidateApparel 内部已调过一次，
// 这里再调一次拿同一引用，缓冲区不会翻倍
List<Pawn> candidatePawns = GearInventoryService.CollectCandidatePawns();

// 修正后
GearInventoryService.ResetAllocation();
// 显式调用一次 CollectCandidatePawns，传给 CollectCandidateApparel 复用，
// 避免内部重复调用导致缓冲区翻倍或浪费 CPU
List<Pawn> candidatePawns = GearInventoryService.CollectCandidatePawns();
List<Apparel> candidateApparel = GearInventoryService.CollectCandidateApparel(candidatePawns);
```

### `CollectCandidateApparel` 签名变更

```csharp
// 修正前
public static List<Apparel> CollectCandidateApparel()
{
    // ...
    List<Pawn> pawns = CollectCandidatePawns();  // 内部调用，可能导致翻倍
    for (int i = 0; i < pawns.Count; i++) { /* ... */ }
    return candidateApparelBuffer;
}

// 修正后
public static List<Apparel> CollectCandidateApparel(List<Pawn> candidatePawns)
{
    // ...
    for (int i = 0; i < candidatePawns.Count; i++) { /* 复用传入的列表 */ }
    return candidateApparelBuffer;
}
```

## 整合优化情况

- 本次修复消除了"幂等设计"这一反直觉的临时方案，回归"显式调用一次"的简单设计
- 注释准确性提升：`TrySafeEquip` 与 `TriggerGearNow` 的注释现在与实际行为一致
- API 设计更清晰：`CollectCandidateApparel` 接受 `List<Pawn>` 参数，调用方显式控制填充时机

## 测试验证结果

### 编译验证

- `make test-check` 完整门禁通过（编译零警告零错误 + 所有测试通过）

### 测试统计

```
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

- 总计：351 个测试全部通过

## 遗留事项

- 游戏内验证仍待用户实际运行（开发环境无法启动 RimWorld）
- 本次仅审查 AutoEquipment 模块，其他模块（AutoWork/AutoMarkPawn/Core）未深入审查

## 下一轮计划

本轮交付完成后无下一轮计划。如游戏内验证发现问题，开新一轮迭代修复。
