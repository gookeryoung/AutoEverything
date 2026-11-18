# AI 开发入口（AutoEverything）

> 本文件是每轮开发的唯一入口，只写“做什么 / 何时停 / 去哪里看”。
> 阶段细则见 [AI_DRIVEN_DEV_FLOW](docs/AI_DRIVEN_DEV_FLOW.md)，硬约束索引见第 7 节，Trae 工具执行卡见 [.trae/rules/agent.md](.trae/rules/agent.md)。
> 同一规则只在一个文件维护；其他位置只写一句摘要 + 链接 / H#，不复制完整口径。

## 1. 唯一来源

| 内容 | 唯一来源 | 说明 |
|---|---|---|
| 执行契约 | `AGENTS.md` | 开工顺序、S/L 分级、实施与收工流程 |
| 阶段流水线 | `docs/AI_DRIVEN_DEV_FLOW.md` | 需求 → 方案 → 代码 → 测试 → 门禁 → 发布 |
| 编码硬约束 | `.trae/rules/rimworld.md` | 写码细则；编号索引见第 7 节 |
| 玩家可见口径 | `README.md` | 评级 / 工作 / 装备等规则与文档同步清单 |
| 决策与交接 | `docs/adr/NNNN-*.md` | 方案、不变量、落地状态、下一步、已知坑 |
| 需求原文与 C1..Cn | `.trae/specs/<topic>/spec.md` | 沿用仓库既有 spec / tasks 约定；ADR 只引用不复制 |
| 角色操作卡 | `.trae/skills/*/SKILL.md` | 4 个角色，见第 6 节 |

## 2. 开工顺序（换会话 / 换 Agent 必做）

1. 读本文件与 `.trae/rules/agent.md`。
2. 读 `README.md` 相关章节与当前需求的 `.trae/specs/<topic>/spec.md`（S2 再读 ADR 的「落地状态」「交接」）。
3. 对表第 7 节 H#：设计触碰任一条时先回到 `.trae/rules/rimworld.md` 核对口径。
4. 跑对应基线（命令以 `Makefile` 为准）：
   - 纯文档：无构建门禁，自查链接、编号与 CHANGELOG。
   - 配置 / 资源：`make check`（`-warnaserror` 零警告零错误）。
   - 代码：`make test-check`（编译 + 全部单测）；大改动再 `make rebuild-check`。
5. 先给任务定级（第 3 节），再决定是否写 spec / ADR。

不要假设上一轮门禁全绿；基线失败先修基线，不把失败带进新改动。

## 3. 分级：先定性，再选流程

### 3.1 需求级（S）

| 级别 | 判定 | 产物 | 人工卡点 |
|---|---|---|---|
| **S0 小改** | bug 修复、文案 / 样式微调、单点改动，无存档 / 设置键 / 判定口径变化 | 无 spec、无 ADR | 意图确认 |
| **S1 常规功能** | 跨 2+ 模块，多约束但无架构分叉 | `.trae/specs/<topic>/{spec.md,tasks.md}`（可轻量） | 需求确认 |
| **S2 重大项** | 存档字段 / 判定公式 / 对外契约变化，或方案有实质分叉 | spec + `docs/adr/NNNN-<topic>.md` | 需求确认 + ADR 评审 |

拿不准升一级；实现中发现设计分叉，S1 升 S2 补 ADR。

### 3.2 门禁级（L，按改动文件路径判定）

| 级别 | 范围 | 必跑 | 说明 |
|---|---|---|---|
| **L0 文档** | 仅 `*.md` | 无构建命令 | 自查链接可达、编号连续、CHANGELOG 已补 |
| **L1 配置 / 资源** | `Makefile`、`.github/`、`About/`、`Defs/`、`Patches/`、`Languages/`、`Textures/`、`.csproj` | `make check` | Def / 翻译 / 纹理改动须进游戏加载一遍，确认无红字 |
| **L2 代码** | `Source/`、`Test/` | `make test-check` | 阶段收尾或大改动再 `make rebuild-check` |

- 混合改动按最高级执行；拿不准按 L2（fail-safe）。
- 分级只约束本地内环与提交前自查；CI（[ci.yml](.github/workflows/ci.yml)）只构建两个项目，不运行单测，所以 `make test` 必须在本地跑过。
- L2 执行链：`make check` → `make test` → 更新文档 → 提交 / 推送；任一步失败修完重跑。

## 4. 实施循环

1. **需求 / 方案**：S1 先落 spec；S2 再写 ADR，`Proposed → Accepted` 前完成人工评审。
2. **小步实现**：S2 按 P0 → P1 → P2；每期一个内聚改动，禁止把多期混成一次提交。
3. **内环**：每次改完跑 `make check`；红了先修，不继续堆代码。
4. **测试同轮**：可测逻辑抽 `*Core` 纯函数并补 `Test/AutoEverything.Tests` 用例，不留“以后再补”（H3 / H4）。
5. **回填**：每期完成更新 ADR `落地状态` 与 `交接`；需求变更先改 spec / ADR 再改代码。
6. **移除**：需求作废或被替代时，直接删除 ADR 与其引用；不保留 Superseded / Deprecated，编号不复用（H16）。

## 5. 收工与提交

1. 按第 3.2 节跑到对应级别全绿；H# 中列出的命令必须绿。
2. 更新 `CHANGELOG.md`（写 `[Unreleased]` 对应分组；S2 条目带 `(ADR NNNN Pn)`），并按 README「文档同步检查清单」同步对应章节。
3. commit 用 conventional commits（H14）：L0 `docs:`，L1 `chore:` / `ci:` / `build:`，L2 `feat:` / `fix:` / `refactor:` / `test:` / `perf:`；描述用中文。
4. 禁止 `--no-verify`；不擅自改 git config；未授权不 force push、不直推 main/master、不打 tag（H15）。
5. 打 tag 与正式发布是人工卡点，未确认不执行。

## 6. AI 驱动结构（4 个角色）

| 阶段 | 角色 Skill | 产出 | 人工卡点 |
|---|---|---|---|
| A 需求 + B 方案 | `spec-writer` | spec / tasks；S2 的 ADR | 需求确认 / ADR 评审 |
| C 实现 + D 测试 | `implementer` | C# 代码、Def / 翻译、单测 | — |
| E 门禁 | `gate-runner` | 全绿门禁与自愈记录 | — |
| F 发布 | `release-manager` | 版本号、tag、release zip | 打 tag 授权 |

阶段输入 / 产出 / 门禁的完整表以 `docs/AI_DRIVEN_DEV_FLOW.md` 为准。

## 7. 硬约束索引（细则见 `.trae/rules/rimworld.md`）

| # | 约束 | 细则位置 |
|---|---|---|
| H1 | 先方案后代码：S1 spec / S2 ADR 未确认不得进入实现 | 本文件第 3、4 节 |
| H2 | 编译零警告：C# / csproj / Makefile 改动后 `make check` 必须绿 | rimworld.md「工作流与验证 · 编译验证」 |
| H3 | 测试同轮：行为改动同轮补用例并 `make test` 全绿，禁注释断言 / 跳过用例 | rimworld.md「工作流与验证」、README「测试」 |
| H4 | 纯逻辑可测：可测逻辑抽 `*Core`，不引用 `Pawn` / `Verse` / `RimWorld` 类型 | README「纯逻辑核心模式」 |
| H5 | 文档同步：玩家可见契约变化必须同步 README 与 CHANGELOG，不一致视为未完成 | README「文档同步检查清单」 |
| H6 | 存档安全：`PostExposeData` 默认值、`ae_` 键前缀、引用用 `Scribe_References`、改名双读 | rimworld.md「存档安全」 |
| H7 | XML 只补丁：不改游戏原生 XML，Patch 用 defName 精确匹配 + Conditional，Def / 翻译 Key 带 `AE_` 前缀 | rimworld.md「XML Def」、项目约定 |
| H8 | Harmony 边界：ID 固定 `gookeryoung.autoeverything`、优先 Postfix、Prefix 复现副作用、`StaticConstructorOnStartup` 初始化 | rimworld.md「Harmony 补丁」 |
| H9 | Tick 性能：禁 LINQ / 分配、取模分散、缓存 TTL ≥ 2500 tick、日志短路 | rimworld.md「性能」 |
| H10 | 异常隔离：Tick 入口 try-catch，`Log.ErrorOnce` + 独立 salt，禁静默吞异常 | rimworld.md「错误处理与日志」 |
| H11 | DLC 与 Def 兼容：`ModsConfig.XActive` 检查、`GetNamed(defName, false)`、null 防御 | rimworld.md「兼容性」 |
| H12 | UI 约束：`ScrollView` 包裹、文字防换行、宽度按 `Text.CalcSize` 动态计算 | rimworld.md「UI 与窗口」、README「面板文字防换行」 |
| H13 | 本地化双语：`ChineseSimplified` 与 `English` 同步，禁硬编码显示文本 | rimworld.md「本地化」 |
| H14 | 提交规范：conventional commits、禁 `--no-verify`、不擅改 git config | 本文件第 5 节 |
| H15 | 发布授权：tag / 正式发布须人工授权；不 force push、不直推 main | 本文件第 5 节 |
| H16 | ADR 生命周期：失效即删并清理引用，编号不复用，不保留 Superseded | `docs/adr/TEMPLATE.md` |

- 两条规则冲突时从严执行；与 ADR 冲突时先改 ADR 与索引，再写码。
- H 条目新增 / 修改 / 删除须人工确认，LLM 不得自行放宽。
