# 迭代记录 iter-02：性能优化与测试

## 需求清单

来源：用户第四条消息"1.增加测试内容，确保逻辑合理。2.完善文档。3.进一步查找其余性能问题。"

- [x] 抽出 `ComputeHeavyUpgradeFlags` 纯逻辑方法（便于单元测试）
- [x] 修复 `AllocateForPawn` 里 `new HashSet<Def>` 性能问题（改为静态复用）
- [x] 编写 `GearAllocatorTests` 测试顺延名额逻辑
- [x] 在 `Program.cs` 注册新测试
- [x] 完善 README 与迭代记录
- [x] 同步规则文件（按 rule-02 约束记录为 rule-07）
- [x] `make test-check` 完整门禁通过

## 迭代目标

1. 为 AutoEquipment 顺延名额逻辑补充单元测试，确保规则契约稳定
2. 修复 Tick 路径的 GC 源（`new HashSet<Def>` per Pawn）
3. 同步文档（README + 规则文件 + 项目记忆）与代码实现一致

## 改动文件清单

### 修改源文件

- `Source/AutoEverything/AutoEquipment/GearAllocator.cs`：
  - 新增 `internal static bool[] ComputeHeavyUpgradeFlags(int, int, List<ArmorPreference>)` 纯逻辑方法
  - 重构 `ExecuteAllocation`：排序后预扫描统一收集 `ArmorPreference` + 统计 `heavyPawnCount`，调用 `ComputeHeavyUpgradeFlags` 获取升级标志
  - `AllocateForPawn` 内 `new HashSet<Def>()` → 改用静态 `processedLayersBuffer.Clear()` 复用
  - 删除未使用的 `layerBestBuffer` 静态字段
  - 删除 `CountHeavyPreferencePawns` 方法（逻辑合并到预扫描）

### 新增测试文件

- `Test/AutoEverything.Tests/GearAllocatorTests.cs` — 21 个测试用例，覆盖 6 类场景

### 修改测试文件

- `Test/AutoEverything.Tests/Program.cs` — 注册 `GearAllocatorTests.RunAll()`

### 修改文档

- `README.md` — 同步"优先级顺延"章节
- `.trae/rules/autoeverything-project.md` L93 — 移除 `CountHeavyPreferencePawns` 引用，新增预扫描与 `ComputeHeavyUpgradeFlags` 描述
- `.trae/rules/rule-07-性能优化与测试.md` — 规则变动记录（新增）
- `c:\Users\zhou\.trae-cn\memory\projects\-e-SteamLibrary-steamapps-common-RimWorld-Mods-AutoEverything\project_memory.md` — 追加 Rule File Update 章节

## 关键决策与依据

### 1. 抽出纯逻辑方法 `ComputeHeavyUpgradeFlags`

**问题**：原 `ExecuteAllocation` 内的顺延名额计算与 RimWorld 状态耦合（嵌在主循环中），无法单元测试。

**方案**：把核心算法抽成 `internal static bool[] ComputeHeavyUpgradeFlags(int heavyArmorCount, int heavyPawnCount, List<ArmorPreference> sortedPrefs)`，无 RimWorld 依赖，输入是 `int + int + List<enum>`，输出是 `bool[]`。

**依据**：
- 项目惯例（`PawnMarker.GetMarkerCategoryCore` / `WorkAllocator.ApplySkillFloorCore` / `CombatEvaluator.EvaluateAutoTierCore`）—— 纯逻辑核心模式
- `InternalsVisibleTo("AutoEverything.Tests")` 已在 `AssemblyInfo.cs` 配置
- 测试不依赖 RimWorld 运行时（无 Pawn/Map/Apparel 实例）

### 2. 修复 `new HashSet<Def>` GC 源

**问题**：`AllocateForPawn` 在 `ExecuteAllocation` 主循环里逐 Pawn 调用，每次 `new HashSet<Def>()` 在 Tick 路径产生 GC。

**方案**：声明静态 `processedLayersBuffer`，每次调用 `Clear()` 复用。语义等价（每次进入 `AllocateForPawn` 时缓冲区应为空），零 GC。

**依据**：项目规则 `rimworld-mod-dev.md` L86「Tick 路径：禁止 LINQ、`new List<>()`、`OrderBy`」+ L88「集合用静态缓存或实例字段复用」。

### 3. 删除未使用的 `layerBestBuffer` 与 `CountHeavyPreferencePawns`

**问题**：
- `layerBestBuffer` 声明后从未引用（可能是早期设计的残留）
- `CountHeavyPreferencePawns` 在 `ExecuteAllocation` 重构后不再被调用，逻辑已合并到预扫描

**方案**：直接删除。

**依据**：Karpathy 原则「删除优于扩展」+ 项目规则 `rimworld-mod-dev.md` L21「单一职责：一个类/文件只做一件事」。

### 4. DetectRole 重复调用（保留现状）

**问题**：`RoleDetector.DetectRole` 在预扫描与主循环各调用 1 次/Pawn（共 2 次）。

**分析**：
- `DetectRole` 返回 `enum Role`，不分配内存（非 GC 源）
- 主循环需要 `Role` 用于评分（`ComputeLayerMatchScore` 用 role，`ComputeMovementPenalty` 用 role），无法仅用 `ArmorPreference` 替代
- 若缓存 `Role` 到额外缓冲区，需新增 `sortedRolesBuffer` + 索引对齐维护，复杂度上升

**决策**：保留现状。

**依据**：Karpathy 原则「简单优于复杂」+ 项目规则 `rimworld-mod-dev.md` L20「KISS 原则优先，能用 if-else 不用策略模式」。enum 返回不分配内存，不违反 Tick 路径 GC 约束。

### 5. 预扫描统一收集 `ArmorPreference` 与 `heavyPawnCount`

**问题**：原 `CountHeavyPreferencePawns` 独立遍历候选 Pawn 调用 `DetectRole`，与主循环重复。

**方案**：在 `ExecuteAllocation` 排序后、主循环前，一次遍历同时：
- 收集每个候选 Pawn 的 `ArmorPreference` 到 `sortedPrefsBuffer`
- 统计 `heavyPawnCount`（`pref == Heavy` 的数量）
- 剔除 Ghoul/X 档（与主循环跳过条件一致，确保索引对齐）

主循环通过 `specIndex` 取预扫描结果，避免第二次 `GetArmorPreference` 调用。

## 代码实现情况

### `ComputeHeavyUpgradeFlags` 算法

```csharp
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
```

### `ExecuteAllocation` 预扫描

```csharp
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

bool[] upgradeFlags = ComputeHeavyUpgradeFlags(heavyArmorCount, heavyPawnCount, sortedPrefsBuffer);
```

### `AllocateForPawn` 缓冲区复用

```csharp
// 旧：HashSet<Def> processedLayers = new HashSet<Def>();
// 新：
processedLayersBuffer.Clear();
// ... 后续 processedLayers.Contains/Add 改为 processedLayersBuffer.Contains/Add
```

## 测试验证结果

### `GearAllocatorTests.cs` 21 个测试用例

6 类场景全部通过：

| 场景 | 用例数 | 验证内容 |
|------|--------|----------|
| EmptyBoundary | 4 | 空列表、heavyPawnCount > heavyArmorCount 防御 |
| AllFlexible | 9 | 全 Flexible + 0~5 重甲的升级名额分配 |
| Mixed | 12 | Heavy/Flexible/Light 混合 |
| LightNeverUpgrade | 15 | Light 始终不升级（保工作效率核心契约） |
| HeavyAlreadyOccupied | 18 | Heavy 已占名额不参与顺延 |
| PriorityOrder | 21 | 按 sortedPrefs 顺序消耗名额（应已按 CombatTier 降序排好） |

### 完整测试套件

```
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
All tests passed.
```

### 编译验证

- `make check` 通过（零错误零警告，`-warnaserror`）
- `make test-check` 完整门禁通过

## 整合优化情况

- 复用项目既有「纯逻辑核心 + internal static」测试模式（与 `PawnMarkerTests` / `ApplySkillFloorCoreTests` 一致）
- 复用 `InternalsVisibleTo("AutoEverything.Tests")` 配置，无需新增程序集暴露
- 复用 `make test-check` 门禁，无需修改 Makefile

## Karpathy 四原则审视

- **简单优于复杂**：`ComputeHeavyUpgradeFlags` 仅 8 行核心逻辑，无早期返回/嵌套分支；保留 DetectRole 双调用以避免引入额外缓冲区
- **删除优于扩展**：删除未使用的 `layerBestBuffer` 与 `CountHeavyPreferencePawns`；不新增 `sortedRolesBuffer`
- **理解优于记忆**：`ComputeHeavyUpgradeFlags` 方法名 + 中文注释自解释；预扫描与主循环用相同跳过条件，注释说明索引对齐
- **原型优于规划**：先编译通过再补测试，测试驱动发现 `ComputeHeavyUpgradeFlags` 缺失后立即实现

## 遗留事项

无。

## 下一轮计划

本轮交付完成后无下一轮计划。如游戏内验证发现问题，开新一轮迭代修复。
