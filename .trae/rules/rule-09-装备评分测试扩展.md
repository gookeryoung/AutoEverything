# 装备评分测试扩展

## 变动日期

2026-07-20

## 变动背景

用户要求"继续分析和完善测试"。前一轮（rule-07）已为 `GearAllocator.ComputeHeavyUpgradeFlags` 添加 21 个测试用例，但 `GearScorer` 的 4 个评分方法（Armor/LayerMatch/Insulation/MovementPenalty）仍无测试覆盖。这些方法是评分公式的核心，是玩家可见契约，最易因 bug 导致装备分配错误。

按项目既有纯逻辑抽取模式（`EvaluateAutoTierCore` / `ComputeHeavyUpgradeFlags`），把 `GearScorer` 的 4 个方法抽出 `Compute*ScoreCore` 纯逻辑版本，便于单元测试。

## 变动内容

### `Source/AutoEverything/AutoEquipment/GearScorer.cs`

- 4 个 `Compute*Score` 方法各自抽出 `Compute*ScoreCore` 纯逻辑版本：
  - `ComputeArmorScoreCore(sharp, blunt, heat, weight)` — 三项平均 × 权重
  - `ComputeLayerMatchScoreCore(sharp, blunt, armorPref, heavyMatchW, lightMatchW, lightAvoidW, flexibleMatchW)` — 三种偏好的加减分
  - `ComputeInsulationScoreCore(ambient, comfortMin, comfortMax, insulationCold, insulationHeat, weight, tempMargin)` — 寒冷/炎热/舒适三档
  - `ComputeMovementPenaltyCore(mass, role, workerW, backRowW, frontRowW)` — 角色 → 权重映射
- 原 `Compute*Score` 方法保留 RimWorld 外壳，负责调用 `apparel.GetStatValue` 收集数值后委托给 Core
- 原因：Core 方法不依赖 Apparel/Pawn 实例，可构造数值输入直接测试。权重参数从 `AESettings.ge*` 静态字段传入，避免测试依赖全局状态。

### `Test/AutoEverything.Tests/GearScorerTests.cs`（新增）

- 4 个 Core 方法的单元测试，共 109 个用例：
  - ArmorScore (7)：全 0/单项极值/权重 0/权重 2.0/真实场景/负值防御
  - LayerMatch (20)：Heavy 线性/Light 双向（含 armorSum 截断到 1.0）/Flexible 线性/权重 0
  - Insulation (34)：寒冷/炎热/舒适三档/strict 边界/tempMargin=0/权重 0/真实场景
  - MovementPenalty (48)：Worker/Doctor/Pacifist 共享 workerW/Shooter/Hunter 共享 backRowW/其他用 frontRowW/mass=0/权重 0/真实轻甲重甲
- 关键测试点：
  - Light 偏好的 `armorSum` 截断到 1.0（避免高护甲反而加分，关键契约）
  - 寒冷/炎热边界用 strict `<` / `>`（边界值不算极端）
  - Worker/Doctor/Pacifist 共享 workerW（角色映射契约）

### `Test/AutoEverything.Tests/Program.cs`

- 注册 `GearScorerTests.RunAll()` 到测试入口

## 影响范围

- 代码：`GearScorer.cs` 重构（4 个方法各加 Core 纯逻辑层，原方法保留外壳）+ 新增测试文件
- 文档：新建 iter-03 迭代记录 + 本规则文件
- 测试：109 个新测试用例全部通过，总计 351 个测试全部通过
- `make test-check` 完整门禁通过（编译零警告零错误 + 所有测试通过）

## 验证方法

- 编译验证：`make test-check` 通过
- 测试统计：旧 242 + 新 109 = 351 个测试全部通过
- 游戏内行为不变（Core 方法仅是原方法逻辑的提取，原方法签名与行为保持一致）

## 同步更新

- `project_memory.md` 追加本次规则文件变动记录（Rule File Update 章节）+ Engineering Conventions 章节（Core 方法抽取模式扩展）
