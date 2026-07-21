# 需求 05：CombatEvaluator 内聚性整合

## 状态

[x] 已完成（2026-07-21，iter-11）

## 需求描述

用户反馈：请整理整合代码，提高内聚性。

## 解决方案

对 `CombatEvaluator.cs` 进行内部整合（不改对外 API 与行为）：

1. **提取 `TryGetOverrideScore`**：消除 `ComputeCombatValue` 与 `ComputePawnValueScore` 开头 4 行重复的「自定义评级 + 配偶豁免」检查
2. **合并 `IsPassionAtLeast`/`IsPassionExactly`**：统一为 `IsPassion(pawn, skillDef, tier, atLeast)`，与已有 `CountPassions` 的 `atLeast` 模式一致
3. **修复维度3 注释被拆断**：`// 维\n// 度3` → `// 维度3`

## 验收

- [x] 3 处整合 + 1 处 bug 修复
- [x] make check 编译零警告零错误
- [x] 全部测试通过（402/402，含 EvaluateAutoTierCoreTests 43/43）
- [x] 文件行数 575 → 432 行，低于 500 行阈值
- [x] 未改变对外公共 API 与行为
- [x] 同步 iter-11 迭代文档
