# 迭代 13：星标颜色统一深红

## 需求清单

- [x] 星星标记改为深红色，避免按类别变色时与头像对比不足导致"差异不清"

来源：用户反馈「星星标记为深红色，避免和头像差异不清」。

## 迭代目标

将殖民者栏星标从「按类别动态取色（金/橙/黄/红/青/白）」改为「统一深红色」，
消除金色/橙色/黄色与浅色头像对比不足的问题。同时遵循「删除优于扩展」原则：
按类别变色逻辑（`GetMarkerColor` 方法 + 对应单元测试）已无意义，整体删除。

## 改动文件清单

| 文件 | 改动 |
|------|------|
| `Source/AutoEverything/AutoMarkPawn/PawnMarker.cs` | 删除 `GetMarkerColor` 方法与 `using UnityEngine`；更新类注释与 `MarkerCategory` 用途说明 |
| `Source/AutoEverything/Core/HarmonyPatches.cs` | 新增 `StarColor` 深红色常量；`DrawStarOnColonistBar` 改用常量；更新 Postfix 与方法注释 |
| `Test/AutoEverything.Tests/PawnMarkerTests.cs` | 删除 `RunColorTests` 与 `CheckColor` 辅助方法及 `using UnityEngine`；更新文件头注释；后续测试编号顺移 |
| `Test/AutoEverything.Tests/AutoEverything.Tests.csproj` | 更新 `UnityEngine.CoreModule` 引用注释（不再为 Color 类型，改为主项目传递依赖） |
| `README.md` | 模块功能表、AutoMarkPawn 章节、目录结构、模块职责、评估周期表 5 处描述同步更新 |

## 关键决策与依据

### 1. 统一深红色而非按类别变色

**决策**：所有 S+ 档次人类单位的星标统一使用深红色 `RGB (0.6, 0.1, 0.1)`，不再按 `MarkerCategory` 区分颜色。

**依据**：
- 殖民者栏头像多为浅色/皮肤色背景，原方案的「金/橙/黄」浅色系星标与头像对比度不足，玩家反馈"差异不清"
- 深红色饱和度高、明度低，与浅色头像形成强对比，玩家一眼可辨
- 「按类别变色」的视觉收益有限——殖民者栏只显示玩家阵营单位（殖民者/奴隶/食尸鬼），类别差异本身就不大
- 类别信息仍通过 `ScanAndMark` 消息文本（"- 殖民者 张三 (S)"）保留，玩家需要类别细节时可查看消息

### 2. 删除 `GetMarkerColor` 而非保留

**决策**：删除 `PawnMarker.GetMarkerColor` 方法与对应单元测试 `RunColorTests`。

**依据**（Karpathy「删除优于扩展」原则）：
- 统一颜色后，按类别取色逻辑不再存在，保留空壳方法仅增加维护成本
- `GetMarkerCategory` 保留（消息展示中翻译类别名仍需要），仅删除颜色相关 API
- 测试项目中的 `UnityEngine.CoreModule` 引用保留，因为主项目（HarmonyPatches 等）仍依赖 UnityEngine，测试运行时需要传递加载

### 3. 颜色常量放在 `HarmonyPatches` 而非 `PawnMarker`

**决策**：深红色 `StarColor` 常量定义在 `HarmonyPatches.ColonistBarDrawer_DrawColonist_Patch` 内，作为 `private static readonly Color`。

**依据**：
- 该常量仅在 `DrawStarOnColonistBar` 中使用，作用域最小化
- `PawnMarker` 模块定位是"判定 + 通知"，不负责绘制；颜色属于绘制层细节，归 `HarmonyPatches`
- 若未来颜色需可配置（Mod 选项），可上移到 `AESettings`；当前 YAGNI

## 代码实现情况

### `HarmonyPatches.cs` 核心改动

```csharp
// 新增颜色常量
private static readonly Color StarColor = new Color(0.6f, 0.1f, 0.1f);

// DrawStarOnColonistBar 中替换动态取色
// 旧：Color starColor = PawnMarker.GetMarkerColor(PawnMarker.GetMarkerCategory(pawn));
// 新：直接使用 StarColor 常量
GUI.color = StarColor;
```

### `PawnMarker.cs` 删除内容

- `using UnityEngine;`（Color 类型不再使用）
- `GetMarkerColor(MarkerCategory category)` 方法整体（17 行）
- 类注释中"按单位类别用不同颜色星标区分"段落
- `MarkerCategory` 注释中"用于星标颜色与消息展示"改为"用于消息展示中的类别名翻译"

### `PawnMarkerTests.cs` 删除内容

- `using UnityEngine;`
- `RunColorTests` 方法（14 行，6 个测试用例）
- `CheckColor` 辅助方法（11 行）
- `RunAll` 中 `failures += RunColorTests(ref total);` 调用
- 文件头注释覆盖范围由 4 项缩减为 3 项
- 后续测试编号顺移：原"3. FormatMessageCore"→"2."，原"4. ComputeNewlyMarkedIds"→"3."

## 整合优化情况

- 测试覆盖范围缩减但核心逻辑覆盖不变：颜色契约消失，无需测试；类别判定、消息格式化、dedup 跟踪三大核心逻辑测试全部保留
- `MarkerCategory` 枚举保留：仍用于 `FormatMessage` 中的 `AE_MarkCat_*` 翻译键查找，删除枚举会破坏消息展示
- 主项目 `make check` 通过：`-warnaserror` 零警告
- 测试 `make test` 通过：所有 304 个测试用例全部通过（PawnMarker 测试 69 个：Category 14 + FormatMessage 23 + ComputeNewlyMarked 32）

## 测试验证结果

### 编译验证

```
$ make check
dotnet build -c Release -warnaserror -nologo -clp:Force Source/AutoEverything/AutoEverything.csproj
AutoEverything net472 已成功 (0.7 秒) → Assemblies\AutoEverything.dll
[check] PASS: No errors
```

### 单元测试

```
$ make test
=== AutoEverything.Tests ===
[PawnMarkerTests/Category] 14/14 passed
[PawnMarkerTests/FormatMessage] 23/23 passed
[PawnMarkerTests/ComputeNewlyMarked] 32/32 passed
...（其他模块测试略）
All tests passed.
```

## 遗留事项

- 用户需进行游戏内验证：殖民者栏星标显示为深红色，与浅色头像对比清晰
- 无 DLC / 有 DLC 环境启动无报错（仅颜色常量变更，不影响 DLC 兼容性）

## 下一轮计划

无。本次迭代单点修复完成，等待用户游戏内验证反馈。
