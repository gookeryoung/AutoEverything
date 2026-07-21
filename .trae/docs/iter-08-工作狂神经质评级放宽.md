# 迭代 08：工作狂+神经质评级放宽

## 变动日期

2026-07-21

## 需求清单

- [x] 神经质+工作狂的小人应给高评级（用户反馈，放宽 degree 要求）

## 迭代目标

将「工作狂+神经质」组合特质的评级规则从严格 `degree == 2 + degree == 2` 放宽到 `degree >= 1 + degree >= 1`，让更多命中该组合的 Pawn 触发高评级（S/SS/SSS）。

## 改动文件清单

- `Source/AutoEverything/RoleEvaluation/CombatEvaluator.cs` — 字段重命名 + degree 放宽 + 注释更新
- `Test/AutoEverything.Tests/EvaluateAutoTierCoreTests.cs` — 字段引用同步 + 注释更新

## 关键决策与依据

### 决策 1：degree 范围放宽到 >= 1

**用户决策**：
- 「工作狂」特质（Industriousness）：degree >= 1（含 degree=1 努力 +12%、degree=2 勤奋 +35%）
- 「神经质」特质（Neurotic）：degree >= 1（含 degree=1 轻度 +8%、degree=2 严重 +14%）

**依据**：
- 旧规则要求 `Industriousness degree == 2`（勤奋）AND `Neurotic degree == 2`（严重神经质），过于严格
- degree=1（努力/轻度神经质）的特质也有显著工作效率加成，组合后仍有高价值
- RimWorld 中文玩家社区常称 degree=2 Industriousness 为「工作狂」，但实际游戏中 degree=1 也常被视为工作狂的弱化版
- 放宽后更多 Pawn 命中组合触发评级，符合"高价值单位一眼可辨"的模块设计目的

### 决策 2：保留 workMajors 升降档结构

**用户决策**：组合即 S 档，workMajors >= 2 升 SS，>= 3 升 SSS

**依据**：
- 保留现有升降档结构，仅放宽 degree 要求
- workMajors 仍是评级档次的核心区分维度（更多专业工作双火 = 更高价值）
- 不破坏既有评级规则的其他维度（乱开枪系列、坚韧格斗系列等）

### 决策 3：字段重命名 `Industrious2` → `HasIndustrious`，`Neurotic2` → `HasNeurotic`

**选择**：重命名字段，语义清晰化

**依据**：
- 旧字段名 `Industrious2` 暗示 degree == 2，放宽后字段语义已变（degree >= 1）
- 保留旧名会让代码"名不副实"，违反「理解优于记忆」原则
- 新名 `HasIndustrious` / `HasNeurotic` 含义清晰："是否拥有任一 degree 的该特质"
- 同步更新测试代码，保持代码一致性

## 代码实现情况

### CombatEvaluator.cs 字段重命名

```csharp
internal struct TierEvaluationInput
{
    // ...
    // 工作狂/神经质：放宽到 degree >= 1（含 degree=1 努力/轻度神经质 + degree=2 勤奋/严重神经质）
    // 用户决策（2026-07-21）：「神经质+工作狂」组合应给高评级，放宽 degree 要求
    public bool HasIndustrious;    // 工作狂 Industriousness degree >= 1（努力/勤奋）
    public bool HasNeurotic;       // 神经质 Neurotic degree >= 1（轻度/严重）
    // ...
}
```

### CombatEvaluator.cs 维度3 判定逻辑

```csharp
// 维度3（工作狂神经质系列）：industrious AND neurotic + workMajors
//   用户决策（2026-07-21）：「神经质+工作狂」组合应给高评级
//   degree 要求放宽到 >= 1（含 degree=1 努力/轻度神经质 + degree=2 勤奋/严重神经质）
//   SSS: hasIndustrious && hasNeurotic && workMajors >= 3
//   SS:  hasIndustrious && hasNeurotic && workMajors >= 2
//   S:   hasIndustrious && hasNeurotic && workMajors >= 1
if (input.HasIndustrious && input.HasNeurotic)
{
    if (input.WorkMajors >= 3)
        tier = MaxTier(tier, CombatTier.SSS);
    else if (input.WorkMajors >= 2)
        tier = MaxTier(tier, CombatTier.SS);
    else if (input.WorkMajors >= 1)
        tier = MaxTier(tier, CombatTier.S);
}
```

### CombatEvaluator.cs CollectTierInput degree 判定放宽

```csharp
input.HasIndustrious = hasTraits && TraitDefCache.Industriousness != null
                      && pawn.story.traits.DegreeOfTrait(TraitDefCache.Industriousness) >= 1;
input.HasNeurotic = hasTraits && TraitDefCache.Neurotic != null
                    && pawn.story.traits.DegreeOfTrait(TraitDefCache.Neurotic) >= 1;
```

### 测试同步更新

- 字段引用 `Industrious2` → `HasIndustrious`
- 参数名 `industrious2` → `hasIndustrious`
- 注释新增"用户决策（2026-07-21）：degree 要求从 == 2 放宽到 >= 1"

## 测试验证结果

- 编译：`make check` 通过，零警告零错误
- 测试：`make test-check` 全部通过，391 个测试通过
- EvaluateAutoTierCoreTests：32/32 通过（维度3 测试用例字段引用已同步）

## 遗留事项

无。本次变更范围明确，测试覆盖完整。

## 下一轮计划

无。需求已交付完毕。
