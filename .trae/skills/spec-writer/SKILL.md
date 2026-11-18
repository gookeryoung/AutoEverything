---
name: spec-writer
description: Use when a raw AutoEverything requirement needs an implementable spec or an ADR. Produce the spec card for S0/S1/S2, then the ADR for S2; research and design only, never implement code.
---

# 需求与方案（spec-writer）

## 输入

- 用户需求原句
- `AGENTS.md`、`docs/AI_DRIVEN_DEV_FLOW.md`、`README.md`
- 现有 `.trae/specs/` 与 `docs/adr/`、相关代码

## 步骤

1. **定级**：按 AGENTS 3.1 判定 S0 / S1 / S2；S0 直接交给 implementer。
2. **需求卡片（S1 / S2）**：逐字保留需求原句，逐句拆 C1..Cn；粗查受影响代码，标候选 `Source/AutoEverything/...:行号`；未决问题清零。模板 `docs/REQUIREMENT_CARD_TEMPLATE.md`，落到 `.trae/specs/<topic>/spec.md`，执行拆步写 `tasks.md`。
3. **人工确认**：S1 卡片确认后才进入实现；S2 继续写 ADR。
4. **ADR（S2）**：口径表沿用 C1..Cn，只补设计落点；核对真实代码，写不变量 I#、决策 D#、边界、存档 / 兼容、测试、分期。模板 `docs/adr/TEMPLATE.md`，文件 `docs/adr/NNNN-<topic>.md`。
5. **登记**：玩家可见口径变化登记到 README 对应章节；新术语直接写进 README，不另建术语表。
6. **评审**：人工评审后将 Status 改为 Accepted，再交给 implementer。

## 停止条件

- S0 / S1：需求卡片经人工确认。
- S2：ADR 经人工评审并 Accepted，决策记录无「待定」。
- 不写实现代码，不改业务逻辑。
