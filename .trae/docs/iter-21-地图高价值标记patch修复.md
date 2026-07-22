# 迭代 21：修复地图高价值标记 patch 静默失败

## 需求清单

- [x] 修复"高价值交易盟友/敌人进入地图后只有日志提示、地图上无标记"的问题

来源：用户反馈「高价值交易盟友进入地图，以及敌人进入地图，只有日志提示，没有标记出来，请解决」。

## 迭代目标

定位并修复 `PawnUIOverlay_DrawPawnGUIOverlay_Patch` 不生效的根因，使敌对/中立/野生高价值单位在地图上能被可视标记。

## 改动文件清单

| 文件 | 改动 |
|------|------|
| `Source/AutoEverything/Core/HarmonyPatches.cs` | 将 `AccessTools.TypeByName("RimWorld.PawnUIOverlay")` 改为 `typeof(PawnUIOverlay)`，方法名改为 `nameof(PawnUIOverlay.DrawPawnGUIOverlay)`；同步更新注释（说明 PawnUIOverlay 在 Verse 命名空间） |

## 关键决策与依据

### 1. 根因：类型名字符串命名空间错误

**决策**：用 `typeof(PawnUIOverlay)` 替换字符串 `"RimWorld.PawnUIOverlay"`。

**依据**：
- 用 Mono.Cecil 反射 `Assembly-CSharp.dll` 确认 RimWorld 1.6.4871 中 `PawnUIOverlay` 类位于 `Verse` 命名空间（`Verse.PawnUIOverlay`），不是 `RimWorld`
- 字段 `Pawn pawn`（private）与方法 `Void DrawPawnGUIOverlay()`（public 实例方法，无参数）与原 patch 代码假设一致，仅类型名错误
- `AccessTools.TypeByName("RimWorld.PawnUIOverlay")` 返回 null → 进入 else 分支输出 `Log.Warning("[AutoEverything] PawnUIOverlay.DrawPawnGUIOverlay 未找到，地图高价值标记降级为无显示")` → patch 未安装 → 地图上无任何高价值标记
- `PawnMarker.ScanAndMark` 扫描机制独立于 Harmony patch，仍能正常工作并弹消息（与用户反馈"只有日志提示"完全吻合）

### 2. 用 typeof + nameof 替代字符串

**决策**：方法名也改用 `nameof(PawnUIOverlay.DrawPawnGUIOverlay)`。

**依据**：
- `HarmonyPatches.cs` 顶部已 `using Verse;`，`typeof(PawnUIOverlay)` 编译期解析，类型不存在时编译报错而非运行时静默降级
- 与同文件中 `typeof(Game)` / `typeof(ColonistBarColonistDrawer)` 风格一致
- 保留 try-catch 降级：`AccessTools.Method` 返回 null 或 `harmony.Patch` 抛异常时仍只 Log.Warning 不崩溃
- 保留 `Log.Warning` 路径以应对未来 RimWorld 版本变更（如方法被重命名）

## 代码实现情况

### HarmonyPatches.cs Init 中的 patch 注册

```csharp
// 修复前（错误）：
var overlayType = AccessTools.TypeByName("RimWorld.PawnUIOverlay");
var overlayMethod = overlayType != null
    ? AccessTools.Method(overlayType, "DrawPawnGUIOverlay")
    : null;

// 修复后（正确）：
var overlayMethod = AccessTools.Method(typeof(PawnUIOverlay), nameof(PawnUIOverlay.DrawPawnGUIOverlay));
```

Postfix 逻辑（`PawnUIOverlay_DrawPawnGUIOverlay_Patch.Postfix`）无需修改——`___pawn` 参数注入、坐标变换、圆形纹理、档位字母绘制等均与 RimWorld 1.6 API 兼容。

## 测试验证结果

### 编译验证

```
$ make check
AutoEverything net472 已成功 (0.4 秒) → Assemblies\AutoEverything.dll
0 个警告
0 个错误
[check] PASS: No errors
```

### 单元测试

```
$ make test
[ApplySkillFloorCoreTests] 30/30 passed
[EvaluateAutoTierCoreTests] 43/43 passed
[PawnMarkerTests/Category] 14/14 passed
[PawnMarkerTests/FormatMessage] 23/23 passed
[PawnMarkerTests/ComputeNewlyMarked] 32/32 passed
All tests passed. (142 个测试全部通过)
```

单元测试不覆盖 Harmony patch 注册（需运行时反射），但修复不影响纯逻辑核心，全部测试通过。

### 根因确认方法

用 `dotnet new console + Mono.Cecil 0.11.5` 临时程序反射 `Assembly-CSharp.dll`，列出所有含 `Pawn` + `Overlay/Drawer/Renderer` 的类型，确认 `Verse.PawnUIOverlay` 存在且字段/方法签名与 patch 假设一致。临时程序用完即删。

## 遗留事项

- 用户需进行游戏内验证：
  - S+ 评级敌对单位进入地图时，头顶应显示红色圆形 + 档位字母（S/SS/SSS）
  - S+ 评级中立/盟友（如贸易访客）进入地图时，头顶应显示青色圆形 + 档位字母
  - S+ 评级野生人类（无派系）头顶应显示白色圆形 + 档位字母
  - 殖民者/奴隶/囚犯不绘制地图标记（由殖民者栏角色图标覆盖）
- 如运行时仍有 `Log.Warning("[AutoEverything] PawnUIOverlay 补丁失败: ...")`，需检查 RimWorld 后续版本是否调整了 PawnUIOverlay API

## 下一轮计划

无。本次迭代完成 bug 修复，等待用户游戏内验证反馈。
