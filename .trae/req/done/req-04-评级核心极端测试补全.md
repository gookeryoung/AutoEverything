# 需求 04：评级核心极端情况测试补全

## 状态

[x] 已完成（2026-07-21，iter-10）

## 需求描述

用户反馈：请继续完善测试用例，提高极端情况验证。

## 解决方案

针对 `CombatEvaluator.EvaluateAutoTierCore` 评级纯逻辑核心，新增「极端情况验证」段 11 个测试用例，按 4 类场景：

1. **多维度叠加 + 降档交互**（3 个）：三维度全 SSS、SSS+负面降档、两维度 SS+负面降档
2. **A/B 判定边界**（3 个）：2 Major+0 Minor、0 Major+5 Minor、1 Major+3 Minor
3. **维度3 阈值与降档**（2 个）：workMajors=10 远超阈值、SS+负面降档
4. **沉鱼落雁边界**（1 个）：Beauty2 + 非社交双火 → C
5. **全字段最大组合**（2 个）：所有维度全开 → SSS、+ 负面降档 → SS

## 验收

- [x] 测试用例新增 11 个（EvaluateAutoTierCoreTests 32 → 43）
- [x] make check 编译零警告零错误
- [x] 全部测试通过（402/402）
- [x] 同步 iter-10 迭代文档
