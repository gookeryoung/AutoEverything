# Agent 执行卡（AutoEverything）

> 本卡只做每轮 checklist；完整口径见 [AGENTS.md](../../AGENTS.md)、[AI_DRIVEN_DEV_FLOW.md](../../docs/AI_DRIVEN_DEV_FLOW.md)、[rimworld.md](rimworld.md)。

1. **先读**：AGENTS → 本卡 → 当前 `.trae/specs/<topic>/spec.md`（S2 再读 ADR 的「落地状态 / 交接」）→ AGENTS 第 7 节相应 H#。
2. **先定级**：需求按 S0 / S1 / S2，改动按 L0 / L1 / L2；拿不准升级。
3. **先方案**：S1 写 spec / tasks；S2 写 spec + ADR；需求确认 / ADR 评审由人拍板，未确认不动代码。
4. **小步做**：S2 按 P0 → P1 → P2；每期一个内聚改动，改完即跑 `make check`。
5. **同轮测试**：可测逻辑抽 `*Core` 并补 `Test/AutoEverything.Tests` 用例，登记到 `Program.cs`；不得注水或跳过用例。
6. **只认门禁**：L0 自查链接 / 编号；L1 `make check`；L2 `make test-check`（大改动 `make rebuild-check`）；失败只修根因并重跑。
7. **回填文档**：ADR 落地状态与交接、CHANGELOG、README 同步清单；ADR 失效即删除，不留 Superseded。
8. **提交规范**：conventional commits（`docs:` / `chore:` / `feat:` / `fix:` / `test:` / `perf:` …）；禁 `--no-verify`。
9. **授权边界**：不改 git config；未授权不 force push、不直推 main/master、不打 tag。
10. **冲突从严**：规则冲突取最严；H 条目不得自行放宽，必须先改台账并人工确认。
