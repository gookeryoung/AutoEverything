# AI 驱动开发流（AutoEverything）

> 目标：需求意图 → 方案 → 代码 → 测试 → 门禁 → 发布，全程由 AI 编排、由机器门禁裁定。
> 执行契约与 S/L 分级见 [AGENTS.md](../AGENTS.md)；硬约束索引见 AGENTS 第 7 节；角色操作卡见 [.trae/skills/](../.trae/skills/)。

## 1. 核心原则

1. **门禁即裁判**：AI 只认命令退出码与 CI 结果，不靠“我认为做完了”。
2. **单一来源**：规则只在 AGENTS / rimworld.md / 本文件 / spec / ADR 各写一次，其他位置只摘要 + 链接。
3. **阶段化 + 人工卡点**：只保留 3 个人工卡点——需求确认、ADR 评审、打 tag。
4. **失败自愈**：门禁报错 → 定位根因 → 修复 → 重跑，直到全绿；不绕过。

## 2. 流水线总览

| 阶段 | 输入 / 触发 | AI 动作 | 产出 | 客观门禁 | 人工卡点 |
|---|---|---|---|---|---|
| **A 需求** | 用户口语需求 | 逐句拆 C1..Cn；粗查 `Source/` 影响面 | `.trae/specs/<topic>/spec.md` | 每句有 C 编号、未决问题清零 | ✅ 需求确认 |
| **B 方案** | spec / tasks | 核对真实代码、定不变量、写决策与分期 | S2 的 ADR（`docs/adr/NNNN-*.md`） | 卡片约束逐条有落点、D 表无待定 | ✅ ADR 评审 |
| **C 实现** | 已确认方案 | 按 P0 → P1 → P2 小步实现 | C# 代码、Def / 翻译、UI | `make check` | — |
| **D 测试** | 与 C 同轮 | 抽 `*Core` 纯逻辑并补用例 | `Test/AutoEverything.Tests/*.cs` | `make test` | — |
| **E 集成** | C + D 全绿 | 回填 ADR、更新 README / CHANGELOG、跑完整门禁 | 文档 + 全绿记录 | `make test-check`；Defs / 翻译改动进游戏加载验证 | — |
| **F 发布** | E 全绿 + 人工授权 | `make bump`、`make release`、`make push` | 版本号 + tag + release zip | [ci.yml](../.github/workflows/ci.yml) / [release.yml](../.github/workflows/release.yml) | ✅ 打 tag 授权 |

S0 / S1 / S2 的裁剪规则以 AGENTS 第 3.1 节为准：S0 直接从 C 开始，S1 从 A 开始但不写 ADR。

## 3. 阶段执行要点

### A 需求

- 需求原句逐字保留在 spec，作为唯一原文副本。
- 逐句拆成可实施约束 C1..Cn，每条必须能落到模块 / 类 / 设置键 / 翻译 Key / 测试。
- 粗查受影响代码，标候选 `Source/AutoEverything/...:行号`；精查留给 B。
- 产出模板：[REQUIREMENT_CARD_TEMPLATE.md](REQUIREMENT_CARD_TEMPLATE.md)；执行拆步写入同目录 `tasks.md`。

### B 方案

- 只对 S2 产出 ADR；S1 把方案写入 spec 即可。
- 口径表沿用 C1..Cn，只补“设计落点”，不复制原文。
- 必须写清：设置项与存档字段、判定入口（`*Core` 纯函数）、UI 落点、Def / 翻译键、边界与例外、兼容性（DLC / 存档中途添加）、测试、分期。
- 决策结论只写 D1..Dn 一处；ADR 结构以 [adr/TEMPLATE.md](adr/TEMPLATE.md) 为准。

### C 实现

- 按模块分目录：`Source/AutoEverything/<模块>/`（AutoWork / AutoEquip / AutoCarry / AutoMarkPawn …），共享逻辑放 `Core/`，界面放 `UI/`。
- 纯逻辑抽 `*Core` 静态方法，输入 struct / 基础类型，生产方法负责从 `Pawn` 收集字段后转调。
- Def 用 `Patches/` + Patch XML 扩展，翻译同时写 `Languages/ChineseSimplified` 与 `Languages/English`。
- 每改完跑 `make check`；每期完成回填 ADR 的“落地状态 / 交接”。

### D 测试

- 与实现同轮，按 `*Core` 分层补 `Test/AutoEverything.Tests/*.cs`；新增测试类要登记到 `Program.cs` 的 `RunAll` 列表。
- 断言行为而非实现细节；覆盖 ADR 的边界与例外。
- 门槛：`make test` 全绿，既有用例不得减少；不能靠删断言 / 跳用例变绿。

### E 集成

- 跑 `make test-check`；更新 README 对应章节（按「文档同步检查清单」）、CHANGELOG `[Unreleased]`、ADR 落地表与交接。
- Defs / Languages / Textures 改动额外做一次进游戏加载验证：无红字、旧存档可继续、新建殖民地正常。
- GitHub Actions [ci.yml](../.github/workflows/ci.yml) 只做构建（零警告零错误），不替代本地 `make test`。

### F 发布

- 打 tag 前必须人工确认；`make bump patch|minor|major` 会更新 `About/About.xml` 与 `.csproj` 版本并创建 tag。
- `make release` 在本地打包 `dist/AutoEverything-v<版本>.zip` 预演；`make push` 推送代码与所有 tag。
- tag 触发 [release.yml](../.github/workflows/release.yml)：构建 + 打包 + GitHub Release。

## 4. 角色与驱动机制

| 角色 | 负责阶段 | 输入 | 输出 / 停止条件 |
|---|---|---|---|
| `spec-writer` | A + B | 用户需求 | spec / tasks；S2 的 ADR（Proposed → 人工评审后 Accepted） |
| `implementer` | C + D | 已确认方案 / ADR | 代码 + 测试；`make check` 与 `make test` 全绿 |
| `gate-runner` | E | 改动完成 | `make test-check` / CI 全绿；失败只修根因并重跑 |
| `release-manager` | F | 全绿 + 人工授权 | 版本号 / tag / release zip；打 tag 由人确认 |

Skill 定义在 `.trae/skills/`；工具不自动加载时，按 AGENTS 第 2 节手动读取。

## 5. 失败闭环

```
门禁失败输出 → 定位根因 → 修改代码 / 测试 / 文档 → 重跑失败项 → 跑完整链 → 全绿
```

- 禁止去掉 `-warnaserror`、注释断言、跳用例、`--no-verify` 或用参数绕过。
- 编译失败先确认 RimWorld / Harmony 引用路径（`Source/AutoEverything/AutoEverything.csproj` 的 `RimWorldPath` / `HarmonyPath`），再查代码。
- 测试失败先跑 `make test` 拿完整失败清单，逐条修根因，不放宽断言。
- 修复后至少重跑失败项 + 一次完整级别门禁。

## 6. 上下文与交接

- 每轮开工按 AGENTS 第 2 节恢复上下文，门禁基线以本轮实跑为准。
- ADR 是方案的唯一载体；需求原文在 `.trae/specs/<topic>/spec.md`；玩家可见口径在 README。
- 收工前回填 ADR 交接（做到哪期 / 下一步 / 已知坑），再更新 CHANGELOG 与门禁。
- 需求移除时先删 ADR 与引用，再改代码（H16）。
