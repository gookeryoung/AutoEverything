# iter-24 移除负面特质降档判定

## 需求清单

- [x] 移除 `CombatEvaluator.EvaluateAutoTierCore` 末尾的降档判断
- [x] S/SS/SSS 一律按命中档次标记，不考虑负面特质
- [x] 删除 `TierEvaluationInput.HasNegativeTrait` 字段、`HasNegativeTrait(Pawn)` 方法及 `CollectTierInput` 中的赋值
- [x] 删除 `TraitDefCache` 中仅供降档使用的 `Pyromaniac`/`SlowLearner`/`Wimp` 字段
- [x] 同步注释、README 与单元测试
- [x] `make test-check` 完整门禁通过

## 迭代目标

用户反馈：评级达到 S/SS/SSS 的殖民者不应因负面特质被降档，应直接按命中档次标记。原降档规则会让"工作狂+神经质+3双火(SSS)+脆弱"降为 SS，违背"顶级组合一律按顶级标记"的预期。本次迭代彻底移除降档逻辑。

## 改动文件清单

| 文件 | 改动 |
|------|------|
| `Source/AutoEverything/RoleEvaluation/CombatEvaluator.cs` | 删除 `EvaluateAutoTierCore` 末尾降档段；删除 `TierEvaluationInput.HasNegativeTrait` 字段；删除 `CollectTierInput` 中的赋值；删除 `HasNegativeTrait(Pawn)` 私有方法；同步类注释与 `GetAutoCombatTier` 文档说明 |
| `Source/AutoEverything/Core/CombatTier.cs` | 移除"有负面特质降一档"枚举注释，改为说明已移除降档 |
| `Source/AutoEverything/Core/TraitDefCache.cs` | 删除 `Pyromaniac`/`SlowLearner`/`Wimp` 三个仅供降档使用的字段 |
| `Test/AutoEverything.Tests/EvaluateAutoTierCoreTests.cs` | 删除 5 个降档测试用例；将 2 个"SS+负面→S"用例改为"SS→SS"；移除 `Empty()` 的 `negativeTrait` 参数与 `HasNegativeTrait` 字段赋值 |
| `README.md` | C/D 档描述更新；删除"降档规则"段落；新增"负面特质不降档"说明 |
| `.trae/req/req-12-移除负面特质降档判定.md` | 新建需求记录 |

## 关键决策与依据

### 决策1：完全移除降档逻辑（而非仅对 S/SS/SSS 豁免）

- 用户表述「移除对于降档的判断」+「只要包含S、SS、SSS特质一律按此标记」语义一致：彻底删除降档。
- 遵循 Karpathy 四原则之「删除优于扩展」：完全删除降档代码比"对高档豁免、低档保留降档"更简单，且符合用户意图。
- 副作用：A/B/C 档也不再因负面特质降档，D 档在自动评级中不再产生（仅供玩家自定义评级使用）。这与用户"不考虑负面因素"的表述一致。

### 决策2：保留 D 档枚举值

- D 档在自动评级中不再产生，但自定义评级（`AESettings.TryGetCustomTier`）仍允许玩家手动设置 D 档，且 `tierRepresentativeScore` 数组仍按枚举索引访问。
- 删除 D 档会破坏枚举数值连续性（X=0,D=1,C=2...），影响 `MaxTier` 比较与 `(CombatTier)(tier-1)` 等历史逻辑（虽已无调用方，但保留枚举值零成本）。

### 决策3：删除 TraitDefCache 中的 Pyromaniac/SlowLearner/Wimp 字段

- 这三个字段仅用于 `HasNegativeTrait(Pawn)` 判定，删除降档逻辑后无任何引用。
- 通过 Grep 确认全仓库无其他引用，删除安全。

## 代码实现情况

### `CombatEvaluator.EvaluateAutoTierCore` 末尾

```csharp
// 注：原负面特质降档逻辑已移除（用户决策 2026-07-26）。
//   S/SS/SSS 一律按命中档次返回，纵火狂/脑子慢/脆弱/工作懒惰怠惰不再降档。
return tier;
```

### `TierEvaluationInput` 结构

移除 `public bool HasNegativeTrait;` 字段，其余字段保持不变。

### `TraitDefCache`

移除三个负面特质字段定义，保留战斗特质、工作狂神经质系列、沉鱼落雁、特殊天赋特质。

## 整合优化情况

- 删除降档逻辑后，`HasNegativeTrait(Pawn)` 私有方法成为死代码，一并删除。
- `TraitDefCache.Pyromaniac`/`SlowLearner`/`Wimp` 字段无外部引用，一并删除。
- 测试文件中 5 个降档用例删除、2 个改为等价的"SS→SS"用例，避免测试覆盖空洞。

## 测试验证结果

`make test-check` 完整门禁：

```
[check] PASS: No errors
=== AutoEverything.Tests ===
[ApplySkillFloorCoreTests] 30/30 passed
[EvaluateAutoTierCoreTests] 38/38 passed
[PawnMarkerTests/Category] 14/14 passed
[PawnMarkerTests/FormatMessage] 23/23 passed
[PawnMarkerTests/ComputeNewlyMarked] 32/32 passed
All tests passed.
```

- 编译零警告零错误（`-warnaserror`）
- 全部 137 个单元测试通过
- `EvaluateAutoTierCoreTests` 由原 42 项调整为 38 项（删除 5 个降档用例 + 新增 1 个"工作狂SS→SS"用例）

## 遗留事项

无。

## 下一轮计划

无。本次需求已闭环交付。
