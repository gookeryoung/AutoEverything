---
name: gate-runner
description: Use to run AutoEverything gates and drive failures to green before a change is considered done. Fix root causes, never drop -warnaserror, comment out assertions, or skip tests.
---

# 门禁自愈（gate-runner）

1. 按改动级别运行：L0 文档自查；L1 `make check`；L2 `make test-check`（大改动再 `make rebuild-check`）。
2. 编译失败先确认 `Source/AutoEverything/AutoEverything.csproj` 的 `RimWorldPath` / `HarmonyPath` 引用路径有效，再定位代码根因。
3. 只改代码 / 测试 / 文档修根因；禁止去掉 `-warnaserror`、注释断言、跳用例、`--no-verify`、改 Makefile 绕过。
4. 修完重跑失败项，再跑一次完整级别门禁；记录未解决风险而不是隐瞒。
5. L2 收尾：`make check` → `make test` → 更新文档 → 提交 / 推送（推送授权见 H15）。
6. CI（`ci.yml`）只构建不跑单测，不能拿 CI 绿替代本地 `make test`。

## 停止条件

- 对应级别门禁全绿；CI 同口径通过；
- 没有以任何方式绕过或降低门槛。
