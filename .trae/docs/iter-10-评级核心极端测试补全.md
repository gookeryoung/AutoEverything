# 迭代 10：评级核心极端情况测试补全

## 变动日期

2026-07-21

## 需求清单

- [x] 继续完善测试用例，提高极端情况验证（用户反馈）

## 迭代目标

针对 `CombatEvaluator.EvaluateAutoTierCore` 评级纯逻辑核心，补充极端情况测试用例，覆盖多维度叠加 + 降档交互、A/B 边界、阈值越界、全字段最大组合等场景，强化评级契约的回归保护。

## 改动文件清单

- `Test/AutoEverything.Tests/EvaluateAutoTierCoreTests.cs` — 新增「极端情况验证」段，11 个用例

## 关键决策与依据

### 决策 1：极端情况分类与用例设计

按 4 类极端场景补充 11 个用例：

| 类别 | 用例数 | 覆盖点 |
|------|--------|--------|
| 多维度叠加 + 降档交互 | 3 | 三维度全 SSS、三维度 SSS + 负面降档、两维度 SS + 负面降档 |
| A/B 判定边界 | 3 | 2 Major + 0 Minor、0 Major + 5 Minor、1 Major + 3 Minor |
| 维度3 阈值与降档 | 2 | workMajors=10 远超阈值、SS + 负面降档 |
| 沉鱼落雁边界 | 1 | Beauty2 + 非社交双火（不触发） |
| 全字段最大组合 | 2 | 所有维度全开 → SSS、+ 负面降一档 → SS |

**依据**：
- 原有 32 个用例覆盖基础路径与单维度场景，但缺乏多维度同时触发的极端叠加
- 降档逻辑与高维度（SSS）交互未覆盖，存在降档边界回归风险
- A/B 判定的合计阈值边界（=3 / <3 / 远超）未覆盖
- 维度3 阈值越界（workMajors=10）未验证不会异常升档
- 全字段最大组合验证 MaxTier 在所有维度都触发时不会出现意外行为

### 决策 2：「三维度 SS + 负面」用例调整为「两维度 SS + 负面」

**问题**：原设计「三维度 SS」同时设 `triggerHappy + tough + shootingMajor`，但维度1 SSS 条件正是 `triggerHappy + tough + shootingMajor`，会导致维度1 实际触发 SSS 而非 SS，与预期不符。

**修正**：移除 `tough` 参数，让维度1 只触发 SS（triggerHappy + shootingMajor），同时维度3 提供 SS（组合 + workMajors=2），两维度叠加 SS + 负面 → S。

**教训**：设计测试用例时需对照评级规则表逐条核对每个维度触发的具体档次，避免「想当然」的组合输入触发更高档次条件。

## 代码实现情况

### 新增「极端情况验证」段（11 个用例）

```csharp
// ── 极端情况验证 ──────────────────────────────────────
// 三维度同时触发 SSS：乱开枪SSS + 坚韧格斗SSS + 工作狂SSS → SSS（MaxTier 不互斥）
// 三维度 SSS + 负面特质 → SS（最高档降一档，验证多维度与降档交互）
// 两维度 SS + 负面特质 → S（降一档：维度1 SS 乱开枪+射击双火 + 维度3 SS 工作狂组合+2双火）
//   注：tough=true 会让维度1 升 SSS（triggerHappy+tough+shootingMajor），故此处不用 tough
// A/B 边界：2 Major + 0 Minor → C（合计<3 不满足A/B）
// A/B 边界：0 Major + 5 Minor → C（无双火不满足A/B，单火再多也不触发）
// A/B 边界：1 Major + 3 Minor → B（合计=4 满足B）
// 维度3 阈值越界：workMajors=10（远超 SSS 阈值 3）→ SSS（>=3 即升 SSS，更大值不越界）
// 维度3 SS + 负面特质 → S（降一档，验证维度3 与降档交互）
// 沉鱼落雁边界：Beauty2 + 非社交双火（如 ShootingMajor）→ C（沉鱼落雁必须配 SocialMajor）
// 全字段最大组合：所有维度触发 SSS + 特殊天赋 S + 沉鱼落雁 S + A 档条件 → SSS（取最高）
// 全字段最大 + 负面特质 → SS（SSS 降一档，验证最高档降档边界）
```

## 测试验证结果

- 编译：`make check` 通过，零警告零错误
- 测试：`AutoEverything.Tests.exe` 全部通过，402/402
- EvaluateAutoTierCoreTests：43/43 通过（原 32 + 新增 11）

## 遗留事项

无。本次为纯测试用例补充，未修改生产代码，未影响评级契约。

## 下一轮计划

无。需求已交付完毕。
