# 需求 03：工作狂+神经质组合即 S 修复

## 状态

[x] 已完成（2026-07-21，iter-09）

## 需求描述

用户反馈：带有工作狂和神经质的 Pawn 依然评级 B，期望应该是 S 起步。

## 根因

iter-08 实现时把 S 档也加上 `workMajors >= 1` 条件，导致无专业工作双火的组合不触发维度3，落入 A/B 判定路径。

## 解决方案

修正维度3 逻辑为「组合即 S」：`HasIndustrious && HasNeurotic` 直接给 S 档，`workMajors >= 2` 升 SS，`>= 3` 升 SSS，与 iter-08 用户决策一致。

## 验收

- [x] 代码修正：CombatEvaluator.cs 维度3 判定逻辑
- [x] 测试覆盖：维度3 测试用例修正（workMajors=0 → S）
- [x] 文档同步：README.md 评级规则表 + iter-09 迭代文档
- [x] 编译验证：make check 通过
- [x] 测试验证：391/391 全部通过
