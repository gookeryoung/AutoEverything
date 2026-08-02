# iter-25 整理文档与完善测试

## 需求清单

- [x] 修复 README 测试计数错误（原写 163/30/32/101，实际 137/30/38/69）
- [x] 为 `TierTagHelper.Strip` / `HasPrefix` 补单元测试
- [x] 为 `RoleDetector.GetArmorPreference` / `IsBackRow` 补单元测试
- [x] 为 `RoleDetector.GetRoleOrder` 补单元测试（自 `AESettings` 迁移）
- [x] 同步 README「纯逻辑核心模式」表格
- [x] `make test-check` 完整门禁通过

## 迭代目标

用户原话「请整理代码结构和文档并完善测试」。代码结构审查无需重构，本次聚焦两处失同步：README 测试计数错误、纯逻辑方法测试覆盖空白。

## 改动文件清单

| 文件 | 改动 |
|------|------|
| `Test/AutoEverything.Tests/TierTagHelperTests.cs` | 新建：35 个用例覆盖 `Strip`/`HasPrefix` 全部 8 个 CombatTier 前缀 + 边界（无 # / # 首位 / 前缀超长 / 非法前缀 / 空字符串 / null） |
| `Test/AutoEverything.Tests/RoleDetectorTests.cs` | 新建：16 个用例表驱动覆盖 `GetArmorPreference` 与 `IsBackRow`（每个 Role 各一） |
| `Test/AutoEverything.Tests/RoleOrderTests.cs` | 新建：8 个用例覆盖 `GetRoleOrder`（每个 Role 各一） |
| `Test/AutoEverything.Tests/Program.cs` | 注册三个新测试文件到 `Main` 聚合运行 |
| `Source/AutoEverything/RoleEvaluation/PawnRole.cs` | 新增 `RoleDetector.GetRoleOrder`（public static，从 `AESettings` 迁移） |
| `Source/AutoEverything/Core/AESettings.TierTag.cs` | 删除 `GetRoleOrder` private 方法，唯一调用点改为直调 `RoleDetector.GetRoleOrder` |
| `README.md` | 修复 L574 测试计数（196 = 30/38/69/35/16/8）；「纯逻辑核心模式」表格新增 `TierTagHelper` / `RoleDetector` 两行 |
| `.trae/req/req-13-整理文档与完善测试.md` | 新建需求记录 |
| `.trae/docs/iter-20-新增Tough坚韧图标.md` | 删除（rule-02：迭代记录保留最新 5 条） |

## 关键决策与依据

### 决策1：`GetRoleOrder` 从 `AESettings` 迁移至 `RoleDetector`

- 原计划改为 `internal static` 直接测试，但 `AESettings` 继承 `ModSettings`（Assembly-CSharp），测试项目仅引用 `UnityEngine.CoreModule`，编译时报 `CS0012: 类型"ModSettings"在未引用的程序集中定义`。
- 两个备选方案：(a) 给测试项目加 `Assembly-CSharp` 引用；(b) 迁移 `GetRoleOrder` 至 `RoleDetector`。
- 选 (b)：`GetRoleOrder` 是 `Role → int` 映射，与 `RoleDetector` 既有的 `Role → ArmorPreference` / `Role → bool` 同质，迁移后内聚性更高，且 `RoleDetector` 已可测，无需引入重依赖。遵循 Karpathy 四原则之「删除优于扩展」。
- 调用方 `AESettings.ReorderColonistBar` 改为直调 `RoleDetector.GetRoleOrder(RoleDetector.DetectRole(p))`，无外部 API 契约变更（原方法是 private）。

### 决策2：保留 `TierTagHelper.Strip`/`HasPrefix` 为 public，不做 `*Core` 抽离

- 这两个方法本就是纯 `string → string` / `string → bool`，无 Pawn/Verse/RimWorld 依赖，符合「纯逻辑」定义。
- README 原表述「所有可测纯逻辑统一抽取为 `*Core` 静态方法」会误导读者以为必须抽离才能测。本次在表格中补充两行并注明「已是纯 public，无需 `*Core` 抽离」。

### 决策3：`RoleDetectorTests` 表驱动而非逐个 if 断言

- 8 个 Role 枚举值 × 2 方法 = 16 用例，表驱动用两个 `Check` helper 各 8 行，比 16 个独立断言简洁。
- 与既有 `ApplySkillFloorCoreTests` 风格一致（参数化 `Check` helper + 失败打印标签）。

## 代码实现情况

### `RoleDetector.GetRoleOrder`（PawnRole.cs 末尾）

```csharp
public static int GetRoleOrder(Role role)
{
    switch (role)
    {
        case Role.Brawler: return 0;
        case Role.Shooter: return 1;
        case Role.Doctor: return 2;
        case Role.Worker: return 3;
        case Role.Pacifist: return 4;
        case Role.Hunter: return 5;
        case Role.Leader: return 6;
        default: return 99;
    }
}
```

### `AESettings.TierTag.cs` 调用点

```csharp
sortRoleCache[p] = RoleDetector.GetRoleOrder(RoleDetector.DetectRole(p));
```

### 测试用例分布

| 测试文件 | 用例数 | 覆盖范围 |
| --- | --- | --- |
| `TierTagHelperTests` | 35 | Strip 8 前缀 + 10 边界；HasPrefix 8 前缀 + 9 边界 |
| `RoleDetectorTests` | 16 | GetArmorPreference × 8 Role + IsBackRow × 8 Role |
| `RoleOrderTests` | 8 | GetRoleOrder × 8 Role（按优先级升序） |

## 整合优化情况

- 删除 `AESettings.GetRoleOrder` private 包装方法（已迁移至 `RoleDetector`），消除一层无意义间接调用。
- 删除最旧迭代记录 `iter-20-新增Tough坚韧图标.md`（rule-02：迭代文件保留最新 5 条，当前 iter-21~25 共 5 条）。

## 测试验证结果

`make test-check` 完整门禁：

```
[check] PASS: No errors
=== AutoEverything.Tests ===
[ApplySkillFloorCoreTests] 30/30 passed
[EvaluateAutoTierCoreTests] 38/38 passed
[PawnMarkerTests/Category] 14/14 passed
[PawnMarkerTests/FormatMessage] 23/23 passed
[PawnMarkerTests/ComputeNewlyMarked] 32/32 passed
[TierTagHelperTests] 35/35 passed
[RoleDetectorTests] 16/16 passed
[RoleOrderTests] 8/8 passed
All tests passed.
```

- 编译零警告零错误（`-warnaserror`）
- 全部 196 个单元测试通过（原 137 + 新增 59）
- README 测试计数已同步更新为 196

## 遗留事项

无。

## 下一轮计划

无。本次需求已闭环交付。
