# 需求 02：工作狂+神经质评级放宽

## 状态

[x] 已完成（2026-07-21，iter-08）

## 需求描述

神经质+工作狂的小人应给高评级。

## 用户决策（2026-07-21）

- 「工作狂」特质（Industriousness）：degree >= 1（含 degree=1 努力 +12%、degree=2 勤奋 +35%）
- 「神经质」特质（Neurotic）：degree >= 1（含 degree=1 轻度 +8%、degree=2 严重 +14%）
- 评级档次：组合即 S 档，workMajors >= 2 升 SS，>= 3 升 SSS（保留升降档结构，仅放宽 degree 要求）

## 实施记录

见 `.trae/docs/iter-08-工作狂神经质评级放宽.md`。
