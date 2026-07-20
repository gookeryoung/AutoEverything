# 迭代记录 iter-03：装备评分测试扩展

## 需求清单

来源：用户反馈"继续分析和完善测试"。

- [x] 从 GearScorer 4 个评分方法抽出纯逻辑 Core（便于单元测试）
- [x] 新建 GearScorerTests.cs 覆盖 4 个 Core 方法
- [x] 在 Program.cs 注册新测试入口
- [x] make test-check 全部门禁通过

## 迭代目标

1. 把 GearScorer 的 4 个评分方法（Armor/LayerMatch/Insulation/MovementPenalty）的数学核心抽出为 `internal static` 纯逻辑方法，参照 `EvaluateAutoTierCore` / `ComputeHeavyUpgradeFlags` 既有模式
2. 新建 `GearScorerTests.cs` 覆盖 4 个 Core，含零值/负值/角色映射/温度三档边界
3. 测试通过后同步迭代记录与规则变动记录

## 改动文件清单

### 修改源文件

- `Source/AutoEverything/AutoEquipment/GearScorer.cs` — 4 个 `Compute*Score` 方法各自抽出 `Compute*ScoreCore` 纯逻辑版本，原方法保留 RimWorld 依赖外壳调用 Core
- `Test/AutoEverything.Tests/Program.cs` — 注册 `GearScorerTests.RunAll()`

### 新增测试文件

- `Test/AutoEverything.Tests/GearScorerTests.cs` — 4 个 Core 方法的单元测试，109 个用例

### 新增文档

- `.trae/docs/iter-03-装备评分测试.md` — 本次迭代记录
- `.trae/rules/rule-09-装备评分测试扩展.md` — 规则文件变动记录

## 关键决策与依据

### 1. 纯逻辑抽取模式（参照 EvaluateAutoTierCore / ComputeHeavyUpgradeFlags）

`GearScorer` 的 4 个 `Compute*Score` 方法都依赖 `apparel.GetStatValue(StatDefOf.X)`，无法直接单元测试。抽出 `Compute*ScoreCore` 方法，输入为纯数值（sharp/blunt/heat/mass/ambient 等）+ 权重参数，不依赖 RimWorld 运行时。原方法保留 RimWorld 外壳，负责调用 `GetStatValue` 收集数值后委托给 Core。

### 2. Core 方法签名设计

- `ComputeArmorScoreCore(sharp, blunt, heat, weight)` — 简单 4 入参
- `ComputeLayerMatchScoreCore(sharp, blunt, armorPref, heavyMatchW, lightMatchW, lightAvoidW, flexibleMatchW)` — 7 入参，覆盖三种偏好的所有权重
- `ComputeInsulationScoreCore(ambient, comfortMin, comfortMax, insulationCold, insulationHeat, weight, tempMargin)` — 7 入参，含温度边界 margin
- `ComputeMovementPenaltyCore(mass, role, workerW, backRowW, frontRowW)` — 5 入参，含 Role 枚举

权重参数从 `AESettings.ge*` 静态字段传入，避免测试依赖全局状态。

### 3. 测试覆盖重点

按"理解优于记忆"原则，重点测试易因 bug 导致分配错误的契约：

- **ComputeLayerMatchScoreCore**：Light 偏好的 `armorSum` 截断到 1.0（避免高护甲反而加分）+ 双向加减分
- **ComputeInsulationScoreCore**：寒冷/炎热边界用 strict `<` / `>`（不是 `<=` / `>=`），边界值不算极端
- **ComputeMovementPenaltyCore**：Worker/Doctor/Pacifist 共享 workerW，Shooter/Hunter 共享 backRowW，其他用 frontRowW
- **ComputeArmorScoreCore**：单项极值平均化（避免纯防弹衣 Sharp 高 Blunt 低压倒其他维度）

### 4. Karpathy 四原则审视

- 简单优于复杂：Core 方法直接 switch/if-else 分支，不用策略模式
- 删除优于扩展：原方法保留外壳不删除（仍是 RimWorld 入口），Core 是新增的纯逻辑层
- 理解优于记忆：每个 Core 方法注释明确写出算法公式与业务契约
- 原型优于规划：先抽取 Armor 一个 Core 跑通测试，再扩展到其余 3 个

## 代码实现情况

### GearScorer.cs 重构

4 个 `Compute*Score` 方法重构为「RimWorld 外壳 + 纯逻辑 Core」双层结构：

```csharp
private static float ComputeArmorScore(Apparel apparel)
{
    float sharp = apparel.GetStatValue(StatDefOf.ArmorRating_Sharp);
    float blunt = apparel.GetStatValue(StatDefOf.ArmorRating_Blunt);
    float heat = apparel.GetStatValue(StatDefOf.ArmorRating_Heat);
    return ComputeArmorScoreCore(sharp, blunt, heat, AESettings.geArmorWeight);
}

internal static float ComputeArmorScoreCore(float sharp, float blunt, float heat, float weight)
{
    float avg = (sharp + blunt + heat) * 0.3333f;
    return avg * weight;
}
```

其余 3 个方法同模式。

### GearScorerTests.cs

按 4 个 Core 分组，共 109 个测试用例：

| 分组 | 用例数 | 覆盖范围 |
|------|--------|---------|
| ArmorScore | 7 | 全 0/单项极值/权重 0/权重 2.0/真实场景/负值防御 |
| LayerMatch | 20 | Heavy 线性/Light 双向（含 armorSum 截断）/Flexible 线性/权重 0 |
| Insulation | 34 | 寒冷/炎热/舒适三档/strict 边界/tempMargin=0/权重 0/真实场景 |
| MovementPenalty | 48 | Worker/Doctor/Pacifist 共享 workerW/Shooter/Hunter 共享 backRowW/其他用 frontRowW/mass=0/权重 0/真实轻甲重甲 |

## 整合优化情况

- 复用既有纯逻辑抽取模式（`EvaluateAutoTierCore` / `ComputeHeavyUpgradeFlags`），不引入新测试框架
- 复用 `InternalsVisibleTo("AutoEverything.Tests")` 程序集可见性，无需暴露 public API
- 复用项目既有 `Check*` 辅助方法风格，统一测试输出格式

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
[GearScorerTests/ArmorScore] 7/7 passed          ← 新增
[GearScorerTests/LayerMatch] 20/20 passed        ← 新增
[GearScorerTests/Insulation] 34/34 passed        ← 新增
[GearScorerTests/MovementPenalty] 48/48 passed   ← 新增
All tests passed.
```

- 旧测试：242 个全部通过
- 新增测试：109 个全部通过
- 总计：351 个测试

## 遗留事项

- 游戏内验证仍待用户实际运行（开发环境无法启动 RimWorld）
- `GearScorer.ComputeScore` 整合方法未单独测试（依赖 Pawn/Apparel 实例，纯逻辑已由 4 个 Core 测试覆盖）

## 下一轮计划

本轮交付完成后无下一轮计划。如游戏内验证发现问题，开新一轮迭代修复。
