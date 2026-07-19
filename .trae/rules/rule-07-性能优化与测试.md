# 性能优化与测试

## 变动日期

2026-07-20

## 变动背景

用户要求三件事：
1. 增加测试内容，确保逻辑合理
2. 完善文档
3. 进一步查找其余性能问题

针对 AutoEquipment 模块的顺延名额逻辑进行：
- 抽出 `ComputeHeavyUpgradeFlags` 纯逻辑方法（无 RimWorld 依赖，便于单元测试）
- 修复 `AllocateForPawn` 内 `new HashSet<Def>()` 在 Tick 路径产生 GC 的问题
- 删除未使用的 `layerBestBuffer` 缓冲区与 `CountHeavyPreferencePawns` 方法
- 排序后预扫描统一收集 `ArmorPreference` 与统计 `heavyPawnCount`，避免主循环重复调用 `RoleDetector.DetectRole` 的命名意图混乱（注：DetectRole 在预扫描与主循环各调用 1 次，主循环需 Role 用于评分；enum 返回不分配内存，非 GC 源，按"简单优于复杂"原则保留）

规则文件中 L93 仍引用已删除的 `CountHeavyPreferencePawns` 方法，需同步更新。

## 变动内容

### `.trae/rules/autoeverything-project.md` L93（优先级顺延章节）

- 旧：`  - \`CountHeavyPreferencePawns\`：统计候选 Pawn 中 \`ArmorPreference.Heavy\`（前排 Brawler）数量`
- 新：
  ```
  - 排序后预扫描：候选 Pawn 按 `CombatTier` 降序排好后，一次遍历同时收集每个 Pawn 的 `ArmorPreference` 与统计 `heavyPawnCount`（`ArmorPreference.Heavy` 即前排 Brawler），避免重复调用 `RoleDetector.DetectRole`
  - `ComputeHeavyUpgradeFlags(heavyArmorCount, heavyPawnCount, sortedPrefs)`：纯逻辑方法计算每个候选 Pawn 的 Heavy 升级标志（无 RimWorld 依赖，便于单元测试）
  ```
- 原因：`CountHeavyPreferencePawns` 方法已删除（其逻辑合并到 `ExecuteAllocation` 主循环的排序后预扫描中），规则文件需同步描述当前实现。新增 `ComputeHeavyUpgradeFlags` 纯逻辑方法用于单元测试，需在规则文件中声明。

### `Source/AutoEverything/AutoEquipment/GearAllocator.cs`

- 新增 `internal static bool[] ComputeHeavyUpgradeFlags(int, int, List<ArmorPreference>)` 纯逻辑方法
- 重构 `ExecuteAllocation`：排序后预扫描收集 `ArmorPreference` 与统计 `heavyPawnCount`，调用 `ComputeHeavyUpgradeFlags` 获取升级标志，主循环按 `specIndex` 取标志
- 修复 `AllocateForPawn` 内 `new HashSet<Def>()` → 改用静态 `processedLayersBuffer.Clear()` 复用
- 删除未使用的 `layerBestBuffer` 静态字段
- 删除 `CountHeavyPreferencePawns` 方法（逻辑合并到主循环预扫描）

### `Test/AutoEverything.Tests/GearAllocatorTests.cs`（新增）

- 测试 `ComputeHeavyUpgradeFlags` 纯逻辑方法
- 6 类测试场景：空/边界、全 Flexible、混合、Light 永不升级、Heavy 已占名额、优先级顺序
- 共 21 个测试用例，覆盖所有边界组合

### `Test/AutoEverything.Tests/Program.cs`

- 注册 `GearAllocatorTests.RunAll()` 到测试入口

### `README.md`

- 同步"优先级顺延"章节，移除 `CountHeavyPreferencePawns` 引用，新增预扫描与 `ComputeHeavyUpgradeFlags` 描述

## 影响范围

- 代码：`GearAllocator.cs` 重构 + 新增测试文件
- 文档：README.md + autoeverything-project.md 同步
- 测试：21 个新测试用例全部通过
- `make test-check` 完整门禁通过（编译零警告零错误 + 所有测试通过）

## 同步更新

- `project_memory.md` 追加本次规则文件变动记录（Rule File Update 章节）
