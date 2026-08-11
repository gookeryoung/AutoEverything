# req-12 移除负面特质降档判定

## 需求清单

- [x] 移除 `CombatEvaluator.EvaluateAutoTierCore` 末尾的降档判断（`if (tier > D && HasNegativeTrait)` 降一档）
- [x] 只要评级达到 S/SS/SSS，一律按该档次标记，不考虑负面特质影响
- [x] 删除 `TierEvaluationInput.HasNegativeTrait` 字段、`CollectTierInput` 中的赋值与 `HasNegativeTrait(Pawn)` 私有方法
- [x] 删除 `TraitDefCache` 中仅供降档使用的 `Pyromaniac`/`SlowLearner`/`Wimp` 字段
- [x] 同步更新 `CombatEvaluator` / `CombatTier` 注释、README 评级章节、单元测试
- [x] `make check` 编译与测试全部通过

## 背景

用户反馈：评级达到 S/SS/SSS 的殖民者不应因负面特质被降档，应直接按命中档次标记。原降档规则会让"工作狂+神经质+3双火(SSS)+脆弱"降为 SS，违背"顶级组合一律按顶级标记"的预期。
