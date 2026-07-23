# 迭代 22：修复地图高价值标记相机缩放偏离

## 需求清单

- [x] 修复"敌人标记在相机缩放过程中偏离 Pawn 头顶"的问题

来源：用户反馈「敌人标记又偏离了位置，请分析解决，避免缩放过程偏离」。

## 迭代目标

定位 `PawnUIOverlay_DrawPawnGUIOverlay_Patch.DrawMapMarker` 坐标变换在相机缩放时偏离 Pawn 头顶的根因，改用 RimWorld 1.6 原生坐标变换方案，使标记精准跟随 Pawn。

## 改动文件清单

| 文件 | 改动 |
|------|------|
| `Source/AutoEverything/Core/HarmonyPatches.cs` | 重写 `DrawMapMarker` 坐标变换：删除手写的 `worldPos.y += HeadOffsetY` + `WorldToScreenPoint + / Prefs.UIScale + Screen.height - y`，改用 `GenMapUI.LabelDrawPosFor(pawn, -0.6f)`；删除 `HeadOffsetY` 常量；边界检查改用 `Verse.UI.screenWidth/Height`；更新类与方法 XML 注释，说明 Z 轴偏移与 Y 轴偏移的本质差异 |

## 关键决策与依据

### 1. 根因：偏移轴选错导致相机缩放飘移

**决策**：改用 `GenMapUI.LabelDrawPosFor(pawn, -0.6f)` 取代手写的世界 Y 轴偏移 + 屏幕坐标变换。

**依据**：用 `ilspycmd` 反编译 RimWorld 1.6.4871 `Assembly-CSharp.dll` 中 `Verse.PawnUIOverlay.DrawPawnGUIOverlay` 与 `Verse.GenMapUI.LabelDrawPosFor`，发现原生实现为：

```csharp
// PawnUIOverlay.DrawPawnGUIOverlay 内部
Vector2 pos = GenMapUI.LabelDrawPosFor(pawn, -0.6f);
GenMapUI.DrawPawnLabel(pawn, pos);

// GenMapUI.LabelDrawPosFor
public static Vector2 LabelDrawPosFor(Thing thing, float worldOffsetZ)
{
    Vector3 drawPos = thing.DrawPos;
    drawPos.z += worldOffsetZ;                                   // 关键：Z 轴偏移
    Vector2 result = Find.Camera.WorldToScreenPoint(drawPos) / Prefs.UIScale;
    result.y = (float)UI.screenHeight - result.y;                // 关键：UI.screenHeight
    // ...
    return result;
}
```

我们之前的实现：

```csharp
Vector3 worldPos = pawn.DrawPos;
worldPos.y += HeadOffsetY;                                       // 错：Y 轴（世界高度）偏移
Vector2 screenPos = Find.Camera.WorldToScreenPoint(worldPos) / Prefs.UIScale;
screenPos.y = Screen.height - screenPos.y;                       // 错：Screen.height 而非 UI.screenHeight
```

**本质差异**：

- RimWorld 世界坐标系：X=东西，Y=高度（垂直），Z=南北（地面平面）
- RimWorld 相机斜视俯视（约 60 度倾角），世界 Y 轴（高度）方向的偏移在屏幕投影上会因相机角度而同时影响屏幕 x/y 方向，且偏移幅度随相机距离（缩放）变化——这正是用户反馈"缩放过程偏离"的根因
- Z 轴是地面平面上的偏移，与 Pawn.DrawPos 同一平面，WorldToScreenPoint 投影后偏移方向与幅度仅依赖相机角度（固定 60 度），与相机缩放完全解耦
- `UI.screenHeight` 是 RimWorld 内部计算的 UI 渲染高度，在多显示器/全屏窗口模式下与 Unity 的 `Screen.height` 可能不同；用 `Screen.height` 会在这些场景下产生额外的 y 偏移

### 2. 标记向上偏移避开 Pawn 名字标签

**决策**：`pos.y -= MarkerSize * 0.5f + 2f`，让标记圆形底部在 Pawn 名字标签上方 2px 间距。

**依据**：
- `LabelDrawPosFor(pawn, -0.6f)` 返回的 `pos` 与原生 `GenMapUI.DrawPawnLabel` 使用的基准点完全一致，即 Pawn 名字标签的顶部左上角
- 原生标签从 `pos.y` 向下绘制 12-16 像素（`NameBGHeight_Tiny=12` / `NameBGHeight_Small=16`）
- 标记若直接以 `pos` 为中心，圆形（半径 10px）会完全覆盖标签
- 向上偏移 `MarkerSize/2 + 2 = 12px` 让标记底部在标签顶部上方 2px，不遮挡标签且仍紧邻 Pawn 头顶
- 标记仍位于 Pawn 头顶区域（屏幕上方），符合用户原意

### 3. 边界检查用 `Verse.UI.screenWidth/Height`

**决策**：防御性边界检查改用 `Verse.UI.screenWidth` / `Verse.UI.screenHeight`。

**依据**：
- 与 `LabelDrawPosFor` 内部使用的 `UI.screenHeight` 同源，坐标系一致
- 用完全限定名 `Verse.UI` 避开本命名空间 `AutoEverything.UI` 的歧义（直接写 `UI.screenWidth` 编译期会被解析到 `AutoEverything.UI`，CS0234 错误）
- 边界检查仅用于粗略剔除屏幕外标记，避免无意义绘制；具体值不影响标记精准度

### 4. 删除 `HeadOffsetY` 常量

**决策**：移除 `private const float HeadOffsetY = 1.6f;`。

**依据**：
- 新方案偏移由 `GenMapUI.LabelDrawPosFor(pawn, -0.6f)` 内部处理，`HeadOffsetY` 不再被引用
- 删除优于扩展（Karpathy 四原则之一）：无用代码立即清理，避免误导后续维护者

## 代码实现情况

### DrawMapMarker 重写

```csharp
private static void DrawMapMarker(Pawn pawn)
{
    // 复用 RimWorld 原生坐标变换：GenMapUI.LabelDrawPosFor(pawn, -0.6f) 与
    // 原生 PawnUIOverlay.DrawPawnGUIOverlay 内部调用完全一致。
    Vector2 pos = GenMapUI.LabelDrawPosFor(pawn, -0.6f);

    // 标记向上偏移（屏幕 y 减小），避开 Pawn 名字标签
    pos.y -= MarkerSize * 0.5f + 2f;

    // 防御性：相机视角外跳过
    if (pos.x < -MarkerSize || pos.x > Verse.UI.screenWidth + MarkerSize) return;
    if (pos.y < -MarkerSize || pos.y > Verse.UI.screenHeight + MarkerSize) return;

    // 圆形背景 + 档位字母（不变）
    // ...
}
```

## 测试验证结果

### 编译验证

```
$ make check
AutoEverything net472 已成功 (0.3 秒) → Assemblies\AutoEverything.dll
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

单元测试不覆盖 Harmony patch 的运行时坐标变换（需 RimWorld 运行时），但本次改动仅替换坐标计算方式，不影响纯逻辑核心，全部测试通过。

### 根因确认方法

用 `ilspycmd 8.2.0` 反编译 `Assembly-CSharp.dll` 中 `Verse.PawnUIOverlay` 与 `Verse.GenMapUI`，对比原生 `DrawPawnGUIOverlay` 与我们 patch 中的坐标变换代码，定位到偏移轴（Y vs Z）与高度 API（`Screen.height` vs `UI.screenHeight`）两处差异。

## 遗留事项

- 用户需进行游戏内验证：
  - 鼠标滚轮缩放相机时，敌对/中立/野生 S+ 高价值单位头顶的圆形 + 档位字母标记应精准跟随 Pawn，不再飘移
  - 标记应位于 Pawn 名字标签上方，不遮挡标签
  - 多显示器/全屏窗口模式下标记位置仍正确

## 下一轮计划

无。本次迭代完成坐标偏离修复，等待用户游戏内验证反馈。
