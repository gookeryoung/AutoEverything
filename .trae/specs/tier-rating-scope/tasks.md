# 任务：人员评级范围配置（S1）

> 需求确认后按顺序执行；每步完成即跑 `make check`。

1. **纯逻辑 + 单测**
   - 新增 `Source/AutoEverything/Core/TierScopeRules.cs`：`PawnScopeKind`（Colonist/Visitor/Slave/Prisoner）、`Classify`、`IsIncluded`。
   - 新增 `Test/AutoEverything.Tests/TierScopeTests.cs`，登记到 `Program.cs`。
2. **设置字段与持久化**
   - `AESettings.Judgement.cs` 增加 `tierScopeColonists` / `tierScopeVisitors` / `tierScopeSlavesPrisoners`（默认 true）与 `ExposeJudgementData` 键。
3. **收集口径**
   - `PawnCollector` 增加按范围收集与全范围收集两个入口；`AESettings.TierTag.cs` 的 Apply 用范围入口、Clear 用全范围入口。
4. **UI + 翻译**
   - 人员评级页新增「评级范围」配置块（三组勾选 + 说明），中英文同步。
5. **文档与门禁**
   - README「全局人物评级标签」覆盖范围与配置项表、CHANGELOG `[Unreleased]` 同步。
   - `make test-check` 全绿后提交。
