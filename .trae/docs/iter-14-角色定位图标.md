# 迭代 14：角色定位图标

## 需求清单

- [x] 增加更多标记类别，替代原单一星标：
  - 坚韧格斗类 → 前排（盾标识），战斗类用橙色
  - 乱开枪带射击火 → 远程（弓箭），战斗类用橙色
  - 工作狂神经质等特性 → 手工（锤子铁砧），工作类用绿色
  - 俊俏/沉鱼落雁带高社交 → 贸易（钱袋子），交易类用粉红
- [x] 替代原 S+ ★ 星标（不叠加）
- [x] 全部叠加显示（一个殖民者符合多个类别时全部显示）
- [x] Texture2D 自定义纹理（程序化生成，不用 Unicode 符号）

来源：用户反馈「增加更多标记类别...战斗类用橙色，工作类用绿色，交易类用粉红」。

## 迭代目标

将殖民者栏单一深红色 ★ 星标（基于 S+ 评级判定）改为 4 种角色定位图标
（前排盾/远程弓/手工锤/贸易钱袋），基于特质 + 技能组合判定，
颜色按战斗橙/工作绿/交易粉三大类分组，一个殖民者可同时显示多个图标。

## 改动文件清单

| 文件 | 改动 |
|------|------|
| `Source/AutoEverything/AutoMarkPawn/RoleIconDef.cs` | 新增：4 种角色定位判定 + 颜色常量 + 静态复用缓冲区 |
| `Source/AutoEverything/AutoMarkPawn/RoleIconTextures.cs` | 新增：[StaticConstructorOnStartup] 程序化生成 4 个 32x32 RGBA 纹理 |
| `Source/AutoEverything/Core/HarmonyPatches.cs` | 重写 `ColonistBarDrawer_DrawColonist_Patch`：删除 ★ 绘制，新增 `DrawRoleIcons` 横向排列；修复 `Widgets.DrawTexture` → `GUI.DrawTexture`（RimWorld 1.6 无此 API） |
| `Source/AutoEverything/AutoMarkPawn/PawnMarker.cs` | 类注释更新：模块定位从"绘制+通知"变为"纯扫描通知" |
| `Source/AutoEverything/Core/TierTagHelper.cs` | 删除已废弃的 `StarMarker = "★"` 常量 |
| `Source/AutoEverything/Core/AESettings.cs` | `autoMarkPawn` 字段注释更新 |
| `Source/AutoEverything/UI/ITab_GearManager.cs` | 两处注释更新："殖民者栏星标" → "殖民者栏角色定位图标" |
| `Source/AutoEverything/Core/AutoExecutor.cs` | 类 doc 注释 + 字段注释更新：从"彩色星标...PawnMarker.IsHighValue"改为"角色定位图标...RoleIconDef.GetRoleIcons" |
| `README.md` | 模块功能表、AutoMarkPawn 章节（重写为「角色定位图标」+「S+ 高价值扫描通知」两子节）、目录结构、模块职责、评估周期表、多处"星标"措辞修正 |

## 关键决策与依据

### 1. 替代原 ★ 星标而非叠加

**决策**：用 4 种角色定位图标完全替代原 S+ ★ 星标，不保留 ★。

**依据**：
- 用户原始反馈「增加更多标记类别」语义模糊，经 AskUserQuestion 三问澄清后确认是「替代」
- 角色定位图标已包含原 ★ 的核心信息（S+ 单位通常符合多个角色定位），保留 ★ 会信息冗余
- 「删除优于扩展」原则：单一 ★ 已无附加价值，删除避免视觉拥挤

### 2. 全部叠加显示而非单选

**决策**：一个殖民者符合多个角色定位时，所有图标从右往左横向排列（最多 4 个）。

**依据**：
- 用户经 AskUserQuestion 明确选择「全部叠加显示」
- 殖民者可能同时是「坚韧格斗 + 工作狂神经质」或「沉鱼落雁 + 高社交 + 乱开枪射击火」，单选会丢失信息
- 横向排列最多 4 个 = 4×16 + 3×2 = 70px，殖民者栏 Rect 宽度足够容纳

### 3. 程序化生成纹理而非 PNG 资源

**决策**：4 个 32×32 RGBA 纹理由 `RoleIconTextures` 在 `[StaticConstructorOnStartup]` 时机用 `Texture2D.SetPixels` 程序化生成。

**依据**：
- 用户经 AskUserQuestion 明确选择「Texture2D 自定义纹理」（不用 Unicode 符号）
- 程序化生成避免依赖外部 PNG 资源，MOD 单文件分发更简洁
- 白色像素 + 透明背景，绘制时用 `GUI.color` 染色，4 种颜色共用同一套纹理（节省内存）
- `FilterMode.Point` 保持像素风，避免 UI 缩放模糊
- 4 种形状（盾/箭/T/圆）视觉差异明显，玩家通过形状 + 颜色双重区分

### 4. 判定逻辑不依赖评级缓存

**决策**：`RoleIconDef.GetRoleIcons` 直接查询特质与技能，不走 `TierCacheService`。

**依据**：
- 评级缓存 2500 tick TTL，角色定位图标若依赖缓存会出现延迟刷新（玩家调整特质后图标不立即更新）
- 殖民者栏每帧绘制多个 Pawn，但 `GetRoleIcons` 仅查询 6 个特质 + 3 个技能，性能开销可接受
- 复用 `TraitDefCache` 与原生 `TraitDefOf`，与 `CombatEvaluator.CollectTierInput` 保持一致的查询模式
- 静态 `List<RoleIconType> buffer` 每帧 Clear 复用，避免 GC 分配

### 5. 颜色分组按战斗/工作/交易三大类

**决策**：4 种图标颜色按战斗类橙 / 工作类绿 / 交易类粉分组，而非每种图标独立配色。

**依据**：
- 用户原始需求明确：战斗类用橙色（前排+远程）、工作类用绿色（手工）、交易类用粉红（贸易）
- 颜色分组让玩家一眼识别类别归属，形状再细分具体定位
- 颜色常量集中定义在 `RoleIconDef`，便于未来扩展（如新增「医疗类蓝色」）

### 6. 修复 `Widgets.DrawTexture` 编译错误

**决策**：使用 `GUI.DrawTexture(iconRect, tex)` 而非 `Widgets.DrawTexture(iconRect, tex)`。

**依据**：
- RimWorld 1.6 的 `Verse.Widgets` 类不包含 `DrawTexture` 静态方法
- `GUI.DrawTexture` 是 Unity 原生 API，RimWorld UI 渲染路径中可直接使用
- 与其他 MOD 的殖民者栏图标绘制方案一致（如 UsefulMarks、Rocketman 等）

## 代码实现情况

### `RoleIconDef.cs` 核心实现

```csharp
public enum RoleIconType : byte
{
    Frontline,  // 前排（盾，橙色）
    Ranged,     // 远程（弓箭，橙色）
    Crafter,    // 手工（锤子铁砧，绿色）
    Trader      // 贸易（钱袋，粉红）
}

public static List<RoleIconType> GetRoleIcons(Pawn pawn)
{
    buffer.Clear();
    // ... 收集 6 个特质 + 3 个技能状态 ...

    if (isTough && (isBrawler || meleeMajor)) buffer.Add(Frontline);
    if (isTriggerHappy && shootingMajor)       buffer.Add(Ranged);
    if (hasIndustrious && hasNeurotic)         buffer.Add(Crafter);
    if (hasBeauty && (socialMajor || socialLevel >= 8)) buffer.Add(Trader);

    return buffer;
}
```

### `RoleIconTextures.cs` 纹理生成

```csharp
[StaticConstructorOnStartup]
public static class RoleIconTextures
{
    public static readonly Texture2D Frontline;  // 盾形
    public static readonly Texture2D Ranged;     // 箭头形
    public static readonly Texture2D Crafter;    // T 字形
    public static readonly Texture2D Trader;     // 实心圆

    static RoleIconTextures()
    {
        Frontline = CreateTexture(IsShieldFilled);
        Ranged = CreateTexture(IsArrowFilled);
        Crafter = CreateTexture(IsHammerFilled);
        Trader = CreateTexture(IsCoinFilled);
    }

    private static Texture2D CreateTexture(System.Func<int, int, bool> fillPredicate)
    {
        // 32x32 RGBA，白色像素 + 透明背景，FilterMode.Point
    }
}
```

### `HarmonyPatches.cs` 绘制逻辑

```csharp
private static void DrawRoleIcons(Rect rect, Pawn pawn)
{
    List<RoleIconDef.RoleIconType> icons = RoleIconDef.GetRoleIcons(pawn);
    if (icons.Count == 0) return;

    float x = rect.xMax - IconSize - Margin;
    float y = rect.yMin + Margin;

    Color prevColor = GUI.color;
    for (int i = 0; i < icons.Count; i++)
    {
        RoleIconDef.RoleIconType type = icons[i];
        GUI.color = RoleIconDef.GetColor(type);
        Texture2D tex = RoleIconTextures.Get(type);
        Rect iconRect = new Rect(x, y, IconSize, IconSize);
        GUI.DrawTexture(iconRect, tex);
        x -= IconSize + IconSpacing;
    }
    GUI.color = prevColor;
}
```

## 整合优化情况

- 模块职责重新切分：`PawnMarker` 仅负责 S+ 扫描通知，绘制职责移交 `RoleIconDef` + `RoleIconTextures` + `HarmonyPatches`
- 删除已废弃的 `TierTagHelper.StarMarker = "★"` 常量（无任何引用）
- 测试无变化：原 PawnMarkerTests 69 个测试全部保留（角色定位判定是纯 UI 层逻辑，无 *Core 抽取必要）
- README AutoMarkPawn 章节重写为两子节：「角色定位图标（4 种）」+「S+ 高价值扫描通知（PawnMarker）」，模块职责更清晰

## 测试验证结果

### 编译验证

```
$ make check
dotnet build -c Release -warnaserror -nologo -clp:Force Source/AutoEverything/AutoEverything.csproj
AutoEverything net472 已成功 (0.4 秒) → Assemblies\AutoEverything.dll
[check] PASS: No errors
```

### 单元测试

```
$ make test
=== AutoEverything.Tests ===
[ApplySkillFloorCoreTests] 30/30 passed
[EvaluateAutoTierCoreTests] 43/43 passed
[PawnMarkerTests/Category] 14/14 passed
[PawnMarkerTests/FormatMessage] 23/23 passed
[PawnMarkerTests/ComputeNewlyMarked] 32/32 passed
[GearAllocatorTests/*] 119/119 passed
[GearScorerTests/*] 109/109 passed
All tests passed.
```

总计 370 个测试全部通过。

## 遗留事项

- 用户需进行游戏内验证：4 种角色定位图标在殖民者栏右上角正确显示，颜色与形状符合预期
- 多图标叠加场景验证：如坚韧格斗 + 工作狂神经质的殖民者应同时显示前排盾 + 手工锤
- 无 DLC / 有 DLC 环境启动无报错（Anomaly DLC 特质查询走 `ModsConfig.AnomalyActive` 检查）

## 下一轮计划

无。本次迭代完成角色定位图标系统，等待用户游戏内验证反馈。
