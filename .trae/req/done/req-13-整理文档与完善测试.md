# req-13 整理文档与完善测试

## 需求清单

- [x] 修复 README 测试计数错误（原写 163/30/32/101，实际 137/30/38/69）
- [x] 为 `TierTagHelper.Strip` / `HasPrefix` 补单元测试（8 个 CombatTier 前缀 + 边界）
- [x] 为 `RoleDetector.GetArmorPreference` / `IsBackRow` 补单元测试（表驱动覆盖 8 个 Role）
- [x] 为 `AESettings.GetRoleOrder` 补单元测试（迁移至 `RoleDetector` 后测试）
- [x] 同步 README「纯逻辑核心模式」表格，新增 `TierTagHelper` / `RoleDetector` 行
- [x] `make test-check` 完整门禁通过

## 背景

用户原话「请整理代码结构和文档并完善测试」。代码结构经审查无需重构（模块按职责分目录、partial 类拆分合理、无死代码），但发现两处问题：README 测试计数严重失同步（L574 写 163/30/32/101，实际 137/30/38/69）；三个纯逻辑模块（`TierTagHelper` / `RoleDetector` / `AESettings.GetRoleOrder`）缺乏单元测试覆盖。
