# 迭代 12：星标显示改用殖民者栏固定标签

## 需求清单

- [x] 参考 UsefulMarks MOD 设计，改进殖民者标记的显示方式
- [x] 固定标签而不是在图层中显示，避免缩放视角带来的影响

来源：req-06-星标显示改用殖民者栏.md

## 迭代目标

将高价值自动标记（AutoMarkPawn）模块的可视化方式从"世界图层 Pawn 头顶绘制 ★"改为"殖民者栏固定位置绘制 ★"，彻底解决相机缩放导致星标与 Pawn 头顶相对位置飘移的问题。

## 改动文件清单

- `Source/AutoEverything/Core/HarmonyPatches.cs` —— 移除 `PawnUIOverlay_DrawPawnGUIOverlay_Patch` 与 `pawnUIOverlayPawnField`，新增 `ColonistBarDrawer_DrawColonist_Patch`
- `Source/AutoEverything/Core/ModController.cs` —— 同步类注释与 Log.Message 文本
- `Source/AutoEverything/Core/AutoExecutor.cs` —— 同步类注释与字段注释（Mark 触发说明）
- `Source/AutoEverything/Core/AESettings.cs` —— 同步 `autoMarkPawn` 字段注释
- `Source/AutoEverything/Core/TierTagHelper.cs` —— 同步 `StarMarker` 常量注释
- `Source/AutoEverything/AutoMarkPawn/PawnMarker.cs` —— 同步模块类注释（显示方式与可视范围限制）
- `Source/AutoEverything/UI/ITab_GearManager.cs` —— 同步勾选框注释
- `README.md` —— 同步模块功能表、AutoMarkPawn 章节、模块职责说明、评估周期表

## 关键决策与依据

### 决策 1：完全移除地图图层星标，仅在殖民者栏显示

依据：
- 用户明确要求"固定标签而不是在图层中显示，避免缩放视角带来的影响"
- 早期方案 hook `PawnUIOverlay.DrawPawnGUIOverlay`，依赖 `GenMapUI.LabelDrawPosFor` 换算世界坐标到屏幕坐标，再叠加固定像素偏移；但 Pawn 模型在屏幕上的高度随相机缩放变化，固定像素偏移无法稳定对齐头顶
- 改为 hook `ColonistBarColonistDrawer.DrawColonist`：殖民者栏是固定 UI 元素，Rect 由 RimWorld 内部计算（已含 UI Scale 缩放），与相机缩放完全解耦

### 决策 2：保留 ScanAndMark 通知消息逻辑覆盖所有人类单位

依据：
- 殖民者栏只显示玩家阵营的殖民者/奴隶/食尸鬼等，非殖民者栏中的高价值单位（囚犯/敌对/中立/野生）无可视星标
- 但 PawnMarker 模块的核心价值之一是"优先俘虏高价值敌人"——这需要扫描发现非殖民者单位
- 折中方案：可视星标仅限殖民者栏，但 ScanAndMark 仍扫描所有人类单位，玩家通过消息提示仍可知晓非殖民者高价值目标

### 决策 3：Postfix 参数直接注入，无需反射

依据：
- 原 PawnUIOverlay 补丁需要通过反射获取私有字段 `pawnUIOverlayPawnField`，存在跨 RimWorld 版本兼容性风险
- 新方案 hook 的 `ColonistBarColonistDrawer.DrawColonist` 是公开实例方法，参数 `Rect rect, Pawn colonist` 与原方法同名同型，Harmony 自动注入，无需反射
- 补丁优先级 `Priority.Last`，避免与其他 MOD 的同方法 patch 顺序冲突

### 决策 4：星标位置选 Rect 右上角，边长 18px

依据：
- 用户确认右上角位置
- 殖民者栏头像约 48x48 像素，星标占右上角约 1/3，醒目不喧宾夺主
- 内缩 2px 留白避免与殖民者栏边框重叠
- 字体 `GameFont.Small`（与原 Medium 相比更适配殖民者栏小尺寸）

## 代码实现情况

### HarmonyPatches.cs 关键改动

```csharp
// 新补丁：ColonistBarColonistDrawer.DrawColonist Postfix
public static class ColonistBarDrawer_DrawColonist_Patch
{
    private const float StarSize = 18f;

    public static void Postfix(Rect rect, Pawn colonist)
    {
        if (!AESettings.enabled || !AESettings.autoMarkPawn) return;
        if (colonist == null) return;
        if (colonist.Dead) return;
        if (!PawnSuitabilityChecker.CanManageGear(colonist)) return;
        if (!PawnMarker.IsHighValue(colonist)) return;

        try { DrawStarOnColonistBar(rect, colonist); }
        catch (Exception ex) { Log.ErrorOnce(...); }
    }

    private static void DrawStarOnColonistBar(Rect rect, Pawn pawn)
    {
        Rect starRect = new Rect(
            rect.xMax - StarSize - 2f,
            rect.yMin + 2f,
            StarSize,
            StarSize);
        // ... 颜色取色 + 绘制 ★ + 恢复 GUI 状态
    }
}
```

### 关键差异：判定条件

- 旧 `PawnUIOverlay_DrawPawnGUIOverlay_Patch` 用 `PawnMarker.IsMarkableTarget`（强制 Spawned）
- 新 `ColonistBarDrawer_DrawColonist_Patch` 不强制 Spawned——殖民者栏包含卧床/运输中的殖民者，仍应标记其高价值状态
- 检查项：`colonist != null && !colonist.Dead && PawnSuitabilityChecker.CanManageGear(colonist) && PawnMarker.IsHighValue(colonist)`

## 整合优化情况

- 移除 `using System.Reflection;` 与 `pawnUIOverlayPawnField` 静态字段，减少反射开销与版本兼容性风险
- 同步更新所有相关注释（HarmonyPatches/ModController/AutoExecutor/AESettings/TierTagHelper/PawnMarker/ITab_GearManager）
- README.md 多处描述同步：模块功能表、AutoMarkPawn 章节、模块职责说明、评估周期表

## 测试验证结果

- `make check` 通过（零警告零错误）
- 输出：`AutoEverything net472 已成功 (0.3 秒) → Assemblies\AutoEverything.dll`
- 单元测试无需改动：PawnMarker 核心逻辑（IsHighValue/IsMarkableTarget/GetMarkerCategory/FormatMessage/ComputeNewlyMarkedIds）未变更

## 遗留事项

- 游戏内验证（无 DLC / 有 DLC 启动无报错、旧存档加载正常、殖民者栏星标显示正确）由用户在游戏中执行
- 非殖民者栏中的高价值单位（囚犯/敌对/中立/野生）无可视星标——这是用户选择的"固定标签"方案的必然结果，已知权衡

## 下一轮计划

无。需求已闭环，等待用户游戏内验证反馈。
