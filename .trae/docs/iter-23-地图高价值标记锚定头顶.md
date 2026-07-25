# 迭代 23：修复地图高价值标记锚点偏离（名字标签 → 头顶）

## 需求清单

- [x] 修复"高价值人员的地图标记位置偏离 Pawn"的问题（req-11）

来源：用户反馈「高价值人员的标记还是偏离了，请分析原因并解决」。

## 迭代目标

定位 `PawnUIOverlay_DrawPawnGUIOverlay_Patch.DrawMapMarker` 标记锚点偏离 Pawn 的根因，将锚点从名字标签位置改为头顶位置，与原生头顶图标（问号图标）对齐。

## 改动文件清单

| 文件 | 改动 |
|------|------|
| `Source/AutoEverything/Core/HarmonyPatches.cs` | `DrawMapMarker` 锚点从 `GenMapUI.LabelDrawPosFor(pawn, -0.6f)`（名字标签，脚下方向）改为 `GenMapUI.LabelDrawPosFor(pawn, pawn.def.size.z - 0.45f)`（头顶，与原生问号图标一致）；同步更新类/方法 XML 注释 |
| `.trae/req/req-11-地图高价值标记锚定头顶.md` | 新增需求记录（已完成，移入 done/） |

## 关键决策与依据

### 1. 根因：锚点选在名字标签，而名字标签在 Pawn 脚下方向

**决策**：锚点改用 `pawn.def.size.z - 0.45f`（人类 = +0.55），与原生问号图标一致。

**依据**：用 `ilspycmd` 反编译当前游戏 `Assembly-CSharp.dll`（与 iter-22 之后游戏版本同步验证）：

```csharp
// GenMapUI.LabelDrawPosFor(Thing, float)
Vector3 drawPos = thing.DrawPos;
drawPos.z += worldOffsetZ;   // Z 轴地面平面偏移，北正南负

// PawnUIOverlay.DrawPawnGUIOverlay（原生名字标签）
Vector2 pos = GenMapUI.LabelDrawPosFor(pawn, -0.6f);   // z-0.6 = 南向偏移

// OverlayDrawer.RenderQuestionMarkOverlay（原生头顶问号图标）
drawPos.x += (float)t.def.size.x - 0.52f;   // 右上角的 x 分量（我们不取）
drawPos.z += (float)t.def.size.z - 0.45f;   // 头顶的 z 分量：人类 size.z=1 → +0.55
```

RimWorld 相机位于南侧俯视北方，Z 轴南向偏移（-0.6）投影到屏幕上是**向下**——名字标签实际绘制在 Pawn 脚下。iter-22 的实现在名字标签上方仅 12px 处绘制标记，扣除标签与脚下的间距后，标记实际落在 Pawn 下半身/脚部区域，视觉上即为"偏离"。

原生"头顶"锚点由 `OverlayDrawer.RenderQuestionMarkOverlay` 给出：Pawn 的头顶偏移为 `drawPos.z + (def.size.z - 0.45f)`，本修复直接复用该公式（只取 z 分量保持水平居中，x 分量是问号图标"右上角"布局所需，不适用于居中标记）。

### 2. 继续复用 LabelDrawPosFor 投影

**决策**：仍用 `GenMapUI.LabelDrawPosFor(pawn, offsetZ)` 做世界 → 屏幕投影，仅替换 offsetZ。

**依据**：
- iter-22 已确认 Z 轴地面平面偏移随相机缩放与 Pawn 精灵同步缩放，缩放过程不飘移；`UI.screenHeight`/`Prefs.UIScale` 处理与原生完全一致
- 头顶与脚下只是同一投影函数的两个不同 Z 偏移，修复不改变坐标系方案，风险最小
- 相机无偏航角（正对北方），Z 轴偏移投影为纯屏幕竖直方向，无 x 分量偏差

## 代码实现情况

```csharp
private static void DrawMapMarker(Pawn pawn)
{
    // 头顶锚点与原生问号图标一致：z + (def.size.z - 0.45f)。
    Vector2 pos = GenMapUI.LabelDrawPosFor(pawn, pawn.def.size.z - 0.45f);

    // 标记底部在头顶上方 2px（屏幕 y 减小方向），不遮挡头部
    pos.y -= MarkerSize * 0.5f + 2f;
    // ...（边界检查、圆形 + 档位字母绘制不变）
}
```

## 测试验证结果

### 编译验证

```
$ make check
AutoEverything net472 已成功 → Assemblies\AutoEverything.dll
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

单元测试不覆盖 Harmony patch 运行时坐标变换（需 RimWorld 运行时），本次改动仅替换锚点偏移量，不影响纯逻辑核心。

### 根因确认方法

`ilspycmd` 反编译 `Assembly-CSharp.dll` 中 `Verse.PawnUIOverlay`、`Verse.GenMapUI`、`RimWorld.OverlayDrawer`，对比名字标签锚点（z-0.6）与头顶问号图标锚点（z + size.z - 0.45f），确认 iter-22 实现锚定的是脚下位置。

## 遗留事项

- 用户需进行游戏内验证：
  - 敌对/中立/野生 S+ 高价值单位的圆形 + 档位字母标记应位于 Pawn **头顶上方**，不再落在身体上
  - 相机缩放时标记与头顶相对位置不飘移
  - 名字标签仍在 Pawn 脚下，与标记互不遮挡

## 下一轮计划

无。等待用户游戏内验证反馈。
