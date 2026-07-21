# 迭代 09：工作狂+神经质组合即 S 修复

## 变动日期

2026-07-21

## 需求清单

- [x] 带有工作狂和神经质依然评级 B，请分析原因并给出解决方案（用户反馈）

## 迭代目标

修复 iter-08 实现中的逻辑缺陷：原实现把 S 档也要求 `workMajors >= 1`，导致无任何专业工作双火的工作狂+神经质 Pawn 落入 A/B 判定路径，被错评为 B 档。本次修正为「组合即 S」，与用户决策一致。

## 改动文件清单

- `Source/AutoEverything/RoleEvaluation/CombatEvaluator.cs` — 维度3 判定逻辑修正 + XML 注释更新
- `Test/AutoEverything.Tests/EvaluateAutoTierCoreTests.cs` — 维度3 测试用例修正
- `README.md` — 评级规则表 S 档条件 3 更新

## 关键决策与依据

### 决策 1：组合即 S（无需 workMajors）

**用户反馈**：带有工作狂和神经质依然评级 B

**根因分析**：

iter-08 实现的维度3 判定逻辑：

```csharp
if (input.HasIndustrious && input.HasNeurotic)
{
    if (input.WorkMajors >= 3)       tier = MaxTier(tier, CombatTier.SSS);
    else if (input.WorkMajors >= 2)  tier = MaxTier(tier, CombatTier.SS);
    else if (input.WorkMajors >= 1)  tier = MaxTier(tier, CombatTier.S);
    // ⚠️ WorkMajors == 0 时不进入任何分支 → 维度3 完全不触发
}
```

当 Pawn 有工作狂+神经质但**没有任何专业工作技能双火**（workMajors=0）时：
- 维度3 跳过，tier 仍为 C
- 进入 A/B 判定路径
- 若有 1 个双火 + 2 个单火 → 评级 B（用户反馈的现象）

**违背用户决策**：iter-08 用户明确决策"组合即 S 档，workMajors >= 2 升 SS，>= 3 升 SSS"，但实现时错误地把 S 档也加上 workMajors>=1 条件。

**修复方案**：

```csharp
if (input.HasIndustrious && input.HasNeurotic)
{
    tier = MaxTier(tier, CombatTier.S);            // 组合即 S
    if (input.WorkMajors >= 3)       tier = MaxTier(tier, CombatTier.SSS);
    else if (input.WorkMajors >= 2)  tier = MaxTier(tier, CombatTier.SS);
}
```

**依据**：
- 与用户决策完全一致：组合即 S，workMajors 决定升档
- 工作狂+神经质组合的工作效率加成（最高 +49% = 勤奋 +35% + 严重神经质 +14%）本身即具高价值
- 即使无工作双火，特质组合也显著提升 Pawn 长期产出，应归 S 档

## 代码实现情况

### CombatEvaluator.cs 维度3 判定逻辑修正

```csharp
// 维度3（工作狂神经质系列）：industrious AND neurotic → 组合即 S，workMajors 决定升档
//   用户决策（2026-07-21）：「神经质+工作狂」组合应给高评级（至少 S）
//   degree 要求放宽到 >= 1（含 degree=1 努力/轻度神经质 + degree=2 勤奋/严重神经质）
//   S:   hasIndustrious && hasNeurotic（组合即 S，无需 workMajors）
//   SS:  hasIndustrious && hasNeurotic && workMajors >= 2
//   SSS: hasIndustrious && hasNeurotic && workMajors >= 3
//   修复（2026-07-21）：原实现把 S 档也要求 workMajors>=1，导致无工作双火的组合
//     落入 A/B 判定路径（如 1 Major+2 Minor → B），违背"组合即 S"决策
if (input.HasIndustrious && input.HasNeurotic)
{
    tier = MaxTier(tier, CombatTier.S);
    if (input.WorkMajors >= 3)
        tier = MaxTier(tier, CombatTier.SSS);
    else if (input.WorkMajors >= 2)
        tier = MaxTier(tier, CombatTier.SS);
}
```

### CombatEvaluator.cs GetAutoCombatTier XML 注释更新

S 档条件 3 从「工作狂 + 神经质 + 1 个专业工作双火」改为「工作狂 + 神经质（组合即 S，无需工作双火）」。

### 测试用例修正

| 用例 | 旧期望 | 新期望 | 说明 |
|------|--------|--------|------|
| 工作狂+神经质+0双火 | C | S | 组合即 S |
| 工作狂+神经质+1双火 | S | S | workMajors<2 不升档（与原期望一致） |

## 测试验证结果

- 编译：`make check` 通过，零警告零错误
- 测试：`AutoEverything.Tests.exe` 全部通过，391/391
- EvaluateAutoTierCoreTests：32/32 通过（含修正后的维度3 测试用例）

## 遗留事项

无。本次为 bug 修复，范围明确，测试覆盖完整。

## 下一轮计划

无。需求已交付完毕。
