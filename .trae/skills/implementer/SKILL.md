---
name: implementer
description: Use when implementing a confirmed S0/S1 requirement or an Accepted S2 ADR in AutoEverything. Implement in phases, write tests in the same round, and keep make check / make test green.
---

# 实现与测试（implementer）

## 输入

- S0：已确认的意图；S1：spec / tasks；S2：Accepted ADR
- `AGENTS.md`、`docs/AI_DRIVEN_DEV_FLOW.md`、`.trae/rules/rimworld.md` 相关条款

## 步骤

1. **拆步**：S2 按 P0 → P1 → P2；S0 / S1 按最小内聚改动拆步，逐项勾掉 `tasks.md`。
2. **代码分层**：新逻辑放对应模块目录（`AutoWork/`、`AutoEquip/`、`AutoCarry/`、`AutoMarkPawn/`、`RoleEvaluation/`、`UI/`），共享能力放 `Core/`。
3. **纯逻辑可测**：判定逻辑抽 `*Core` 静态方法（输入 struct / 基础类型，不引用 `Pawn` / `Verse` / `RimWorld`），生产方法收集字段后转调（H4）。
4. **约束落地**：存档字段 / Def / Harmony / Tick 性能 / 异常隔离 / DLC 兼容 / UI 防换行 / 双语翻译按 H6–H13 执行。
5. **测试同轮**：在 `Test/AutoEverything.Tests/` 补用例并登记到 `Program.cs` 的 `RunAll`；不留“以后再补”（H3）。
6. **内环**：每改完跑 `make check`；失败修根因后重跑；行为改动再跑 `make test`。
7. **回填**：每期更新 ADR 落地状态与交接；最后按 README「文档同步检查清单」更新文档与 CHANGELOG。

## 停止条件

- 目标 L 级门禁全绿（L2 为 `make test-check`）；
- S2 的落地表与实际代码 / 测试一致；
- 不做未授权的提交、推送、打 tag。
