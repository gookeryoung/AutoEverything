# 迭代 11：CombatEvaluator 内聚性整合

## 变动日期

2026-07-21

## 需求清单

- [x] 整理整合代码，提高内聚性（用户反馈）

## 迭代目标

对 `CombatEvaluator.cs` 进行内部代码整合，消除重复、统一相似方法签名、修复注释 bug，提高模块内聚性。不改变对外公共 API 与行为。

## 改动文件清单

- `Source/AutoEverything/RoleEvaluation/CombatEvaluator.cs` — 3 处整合 + 1 处 bug 修复

## 关键决策与依据

### 决策 1：提取 `TryGetOverrideScore` 消除重复

**问题**：`ComputeCombatValue` 与 `ComputePawnValueScore` 开头各有完全重复的「自定义评级 + 配偶豁免」检查（共 4 行 × 2 处 = 8 行重复）：

```csharp
if (AESettings.TryGetCustomTier(GetPawnLookupName(pawn), out CombatTier customTier))
    return tierRepresentativeScore[(int)customTier] + 0.5f;
if (HasSpouseTierAtLeast(pawn, CombatTier.S))
    return tierRepresentativeScore[(int)CombatTier.S] + 0.5f;
```

**方案**：提取为 `TryGetOverrideScore(pawn, out float score)`，两个调用点改为：

```csharp
if (TryGetOverrideScore(pawn, out float overrideScore))
    return overrideScore;
```

**依据**：
- 完全重复代码违反 DRY，且两处都需同步修改时易遗漏
- 提取后语义更清晰：「尝试获取覆盖分」一目了然
- 该方法是 private，不扩大对外 API 面

### 决策 2：合并 `IsPassionAtLeast`/`IsPassionExactly` 为统一 `IsPassion`

**问题**：两个方法逻辑高度相似，仅比较运算符不同（`>=` vs `==`）：

```csharp
private static bool IsPassionAtLeast(Pawn pawn, SkillDef skillDef, PassionHelper.PassionTier minTier)
{
    SkillRecord s = pawn.skills?.GetSkill(skillDef);
    return s != null && (int)PassionHelper.GetPassionTier(s.passion) >= (int)minTier;
}

private static bool IsPassionExactly(Pawn pawn, SkillDef skillDef, PassionHelper.PassionTier tier)
{
    SkillRecord s = pawn.skills?.GetSkill(skillDef);
    return s != null && PassionHelper.GetPassionTier(s.passion) == tier;
}
```

**方案**：合并为 `IsPassion(pawn, skillDef, tier, atLeast)`：

```csharp
private static bool IsPassion(Pawn pawn, SkillDef skillDef, PassionHelper.PassionTier tier, bool atLeast)
{
    SkillRecord s = pawn.skills?.GetSkill(skillDef);
    if (s == null) return false;
    var actual = PassionHelper.GetPassionTier(s.passion);
    return atLeast ? (int)actual >= (int)tier : actual == tier;
}
```

**依据**：
- 与已有 `CountPassions(pawn, targetTier, atLeast, skills)` 的 `atLeast` 参数模式一致
- 5 处调用点（ShootingMajor/Minor、MeleeMajor/Minor、SocialMajor）改为显式 `atLeast: true/false`，可读性更佳
- 减少方法数量，符合 KISS 原则

### 决策 3：修复维度3 注释被拆断为两行的 bug

**问题**：[CombatEvaluator.cs:349-350](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/RoleEvaluation/CombatEvaluator.cs#L349-350) 注释「维度3」被意外拆为两行：

```csharp
// 维
// 度3（工作狂神经质系列）：industrious AND neurotic → ...
```

**方案**：合并为单行 `// 维度3（工作狂神经质系列）：...`

**依据**：
- 行间断开破坏注释可读性，IDE 显示为「度3」开头
- 可能是之前 Edit 工具操作时换行未对齐导致

### 决策 4：不拆分文件

**考虑**：CombatEvaluator.cs 原 575 行，已超 rule-spec-01「500 行考虑拆分」阈值。

**不拆分理由**：
- 4 个职责（战斗价值分、综合价值评分、评级档次、配偶豁免）通过 `CombatEvaluator` 静态类统一对外，拆分需保留门面或更新所有引用，风险较大
- 整合后降至 432 行，已低于阈值
- KISS 原则：能用内部整合解决就不拆分

## 代码实现情况

### 整合点 1：TryGetOverrideScore 提取

新增 private 方法 `TryGetOverrideScore`，统一处理「自定义评级 + 配偶豁免」早返回路径。`ComputeCombatValue` 与 `ComputePawnValueScore` 开头均改为调用该方法。

### 整合点 2：IsPassion 统一方法

合并 `IsPassionAtLeast` + `IsPassionExactly` 为 `IsPassion(pawn, skillDef, tier, atLeast)`。5 处 `CollectTierInput` 调用点同步更新，显式传递 `atLeast: true/false`。

### 整合点 3：维度3 注释修复

合并两行注释为单行 `// 维度3（工作狂神经质系列）：industrious AND neurotic → 组合即 S，workMajors 决定升档`。

## 测试验证结果

- 编译：`make check` 通过，零警告零错误
- 测试：`AutoEverything.Tests.exe` 全部通过，402/402
- 行数：575 → 432 行（-143 行），低于 500 行阈值

## 遗留事项

无。本次为内部整合，未改变对外公共 API 与行为，未影响评级契约。

## 下一轮计划

无。需求已交付完毕。
