# 阶段三修复 + 阶段四 UI 改进 + DRY 收尾计划

> 目标：修复上一阶段 Task #8 遗留的编译阻塞 BUG，完成阶段三/四剩余任务，并应用同模式 DRY 收尾。
> 遵循 Karpathy 四原则：简单优于复杂、删除优于扩展、理解优于记忆、原型优于规划。

## 当前状态分析

### 上一阶段已完成（验证通过）
- **阶段一 P0 BUG**：SidearmAllocator try-catch + BeltAllocator `using System;` ✓
- **阶段二 P0 性能**：
  - BeltAllocator/SidearmAllocator `tierCache` 预计算 ✓
  - GlobalAllocator 静态化 + L394 复用 `tierCache` ✓
  - AESettings 三个 Sort 比较器加 `sortTierCache/sortValueCache/sortRoleCache` ✓
- **阶段三 P1 代码精简（部分）**：
  - 新建 `Core/TraitDefCache.cs`（16 个 TraitDef）✓
  - 新建 `Core/TierTagHelper.cs`（Strip/HasPrefix）✓
  - `GearDefClassifier.HasBeltLayerApparel` 新增 ✓
  - `BeltAllocator.HasBelt` 删除并改调 `GearDefClassifier.HasBeltLayerApparel` ✓
  - `CombatEvaluator.StripTierTagPrefixFromLabel` 删除，改调 `TierTagHelper.Strip` ✓
  - `AESettings.StripTierTagPrefix/HasTierTagPrefix` 删除，改调 `TierTagHelper` ✓
  - `WeaponTraitScorer` 4 个 TraitDef 迁移到 `TraitDefCache` ✓

### 上一阶段遗留的关键 BUG（编译阻塞）

#### BUG-A：CombatEvaluator.cs 6 处 TraitDef 引用未迁移（CRITICAL）
上一阶段 Task #8 删除了 CombatEvaluator 的 16 个 TraitDef 字段定义，但**仅更新了部分引用**，仍有 6 处引用未迁移，会导致 CS0103 编译失败：

| 行号 | 当前引用（已坏）           | 应改为                       |
|------|----------------------------|------------------------------|
| L236 | `nimbleDef`                | `TraitDefCache.Nimble`       |
| L237 | `brawlerDef`               | `TraitDefOf.Brawler`         |
| L247 | `beautyDef`（声明）         | `TraitDefCache.Beauty`       |
| L248 | `beautyDef`（DegreeOfTrait）| `TraitDefCache.Beauty`        |
| L410 | `pyromaniacDef`            | `TraitDefCache.Pyromaniac`   |
| L411 | `slowLearnerDef`           | `TraitDefCache.SlowLearner`  |
| L412 | `wimpDef`                  | `TraitDefCache.Wimp`         |

**验证**：Grep 确认 `nimbleDef|brawlerDef|beautyDef|pyromaniacDef|slowLearnerDef|wimpDef` 仅在 CombatEvaluator.cs 出现（7 行命中，其中 L247-248 是同一字段的两处使用）。

### 上一阶段未完成的任务

#### Task #126：删除 ITab_GearManager DrawLabeledRow 死代码
- **位置**：`ITab_GearManager.cs` L836-856
- **状态**：Grep 确认 `DrawLabeledRow` 在整个 Source 目录**仅 1 处命中**（即定义本身），无任何调用方
- **决策**：删除（"删除优于扩展"原则）

#### Task #127：Dialog_GlobalReallocate contentHeight 改 static
- **位置**：`Dialog_GlobalReallocate.cs` L24
- **当前**：`private float contentHeight = 900f;`（实例字段，每次 new 窗口丢失）
- **改进**：改为 `private static float contentHeight = 900f;`（与 L21 `scrollPos` 一致）

#### Task #128：ITab_GearManager tierCodeRect 防截断
- **位置**：`ITab_GearManager.cs` L266-275
- **当前**：直接 `Widgets.Label(tierCodeRect, ...)` 绘制 `当前评级: SSS(SSS)#王五` 长字符串，超宽会截断
- **改进**：绘制前用 `Text.CalcSize` 验证，超宽时缩字号为 `GameFont.Tiny`

### 新发现的 DRY 违规（同模式扩展）

#### NEW-1：ApparelTraitScorer 3 个 TraitDef 重复
- **位置**：`ApparelTraitScorer.cs` L16-18
- **现状**：定义 `industriousDef` / `neuroticDef` / `beautyDef`，三者**全部已在 TraitDefCache 中**
- **改进**：删除 3 个字段，引用改为 `TraitDefCache.Industriousness` / `TraitDefCache.Neurotic` / `TraitDefCache.Beauty`
- **理由**：与上一阶段 Task #8（WeaponTraitScorer 迁移）完全同模式，保持一致性

#### NEW-2：CompGearManager nudistDef 未集中
- **位置**：`CompGearManager.cs` L24（定义）、L477（使用）
- **现状**：`nudistDef = DefDatabase<TraitDef>.GetNamed("Nudist", false)` 独立定义
- **改进**：
  1. `TraitDefCache.cs` 新增 `Nudist` 字段
  2. `CompGearManager.cs` 删除 L24 字段定义，L477 改调 `TraitDefCache.Nudist`
- **理由**：集中管理所有非原生 DefOf 的 TraitDef 查询，便于未来维护

## 实施方案

### 阶段 A：修复编译阻塞 BUG（最高优先级）

#### Task 1：CombatEvaluator.cs 修复 6 处 TraitDef 引用

**文件**：`Source/AutoEverything/RoleEvaluation/CombatEvaluator.cs`

**修改 7 处**（L236、L237、L247、L248、L410、L411、L412）：

```csharp
// L236 原：
bool isNimble = hasTraits && nimbleDef != null && pawn.story.traits.HasTrait(nimbleDef);
// 改为：
bool isNimble = hasTraits && TraitDefCache.Nimble != null && pawn.story.traits.HasTrait(TraitDefCache.Nimble);

// L237 原：
bool isBrawler = hasTraits && pawn.story.traits.HasTrait(brawlerDef);
// 改为（Brawler 是原生 DefOf，始终存在）：
bool isBrawler = hasTraits && pawn.story.traits.HasTrait(TraitDefOf.Brawler);

// L247-248 原：
bool beauty2 = hasTraits && beautyDef != null
               && pawn.story.traits.DegreeOfTrait(beautyDef) == 2;
// 改为：
bool beauty2 = hasTraits && TraitDefCache.Beauty != null
               && pawn.story.traits.DegreeOfTrait(TraitDefCache.Beauty) == 2;

// L410 原：
if (pyromaniacDef != null && pawn.story.traits.HasTrait(pyromaniacDef)) return true;
// 改为：
if (TraitDefCache.Pyromaniac != null && pawn.story.traits.HasTrait(TraitDefCache.Pyromaniac)) return true;

// L411 原：
if (slowLearnerDef != null && pawn.story.traits.HasTrait(slowLearnerDef)) return true;
// 改为：
if (TraitDefCache.SlowLearner != null && pawn.story.traits.HasTrait(TraitDefCache.SlowLearner)) return true;

// L412 原：
if (wimpDef != null && pawn.story.traits.HasTrait(wimpDef)) return true;
// 改为：
if (TraitDefCache.Wimp != null && pawn.story.traits.HasTrait(TraitDefCache.Wimp)) return true;
```

**注**：`using AutoEverything.Core;` 已在 L3 存在，无需新增 using。

#### Task 2：阶段 A make check 验证

**命令**：`make check`
**预期**：0 警告 0 错误（修复编译阻塞后应能通过）。

---

### 阶段 B：完成阶段三剩余 + DRY 收尾

#### Task 3：ApparelTraitScorer 迁移 TraitDef 到 TraitDefCache

**文件**：`Source/AutoEverything/AutoEquipment/Scoring/Apparels/ApparelTraitScorer.cs`

**修改**：
1. 新增 `using AutoEverything.Core;`（当前文件未引用 Core 命名空间）
2. 删除 L14-18 共 3 个 TraitDef 字段定义（`industriousDef`/`neuroticDef`/`beautyDef`）及注释
3. L28 `industriousDef` → `TraitDefCache.Industriousness`
4. L30 `industriousDef` → `TraitDefCache.Industriousness`
5. L39 `neuroticDef` → `TraitDefCache.Neurotic`
6. L41 `neuroticDef` → `TraitDefCache.Neurotic`
7. L47 `beautyDef` → `TraitDefCache.Beauty`（2 处）

**修改前**（L14-18）：
```csharp
// 缓存 TraitDef 查找，避免 Tick 路径每次重复字典查询
// 使用 GetNamed(defName, false) 安全查询：未找到返回 null 而非抛异常
private static readonly TraitDef industriousDef = DefDatabase<TraitDef>.GetNamed("Industriousness", false);
private static readonly TraitDef neuroticDef = DefDatabase<TraitDef>.GetNamed("Neurotic", false);
private static readonly TraitDef beautyDef = DefDatabase<TraitDef>.GetNamed("Beauty", false);
```

**修改后**：整段删除（TraitDef 已集中到 TraitDefCache）。

#### Task 4：TraitDefCache 新增 Nudist + CompGearManager 迁移

**文件 1**：`Source/AutoEverything/Core/TraitDefCache.cs`

在 L34（Wimp 字段后）新增：
```csharp
// 裸体主义者（影响服装评分，CompGearManager 使用）
public static readonly TraitDef Nudist = DefDatabase<TraitDef>.GetNamed("Nudist", false);
```

**文件 2**：`Source/AutoEverything/AutoEquipment/CompGearManager.cs`

1. 确认 `using AutoEverything.Core;` 是否已存在（CompGearManager 在 `AutoEverything.AutoEquipment` 命名空间，需 using Core）
2. 删除 L24 `private static readonly TraitDef nudistDef = DefDatabase<TraitDef>.GetNamed("Nudist", false);`
3. L477 `nudistDef` → `TraitDefCache.Nudist`（2 处：null 检查 + HasTrait 参数）

**修改前**（L477）：
```csharp
if (nudistDef != null && Pawn.story?.traits?.HasTrait(nudistDef) == true)
```

**修改后**：
```csharp
if (TraitDefCache.Nudist != null && Pawn.story?.traits?.HasTrait(TraitDefCache.Nudist) == true)
```

#### Task 5：删除 ITab_GearManager DrawLabeledRow 死代码

**文件**：`Source/AutoEverything/UI/ITab_GearManager.cs`

**修改**：删除 L832-856 共 25 行（含上方 `/// <summary>` 注释块）。

**验证**：已通过 Grep 确认整个 Source 目录无 `DrawLabeledRow` 调用方，仅 L836 定义本身 1 处命中。

**注**：上方 L798-830 存在另一个类似方法（`DrawLabeledRowAutoSize`，按上下文推断），需保留——只删 L832-856 的无调用版本。

#### Task 6：阶段 B make check 验证

**命令**：`make check`
**预期**：0 警告 0 错误。

---

### 阶段 C：阶段四 UI 改进

#### Task 7：Dialog_GlobalReallocate contentHeight 改 static

**文件**：`Source/AutoEverything/UI/Dialog_GlobalReallocate.cs`

**修改**：L24

```csharp
// 修改前：
private float contentHeight = 900f;

// 修改后：
private static float contentHeight = 900f;
```

**理由**：与 L21 `private static Vector2 scrollPos` 一致，避免多次开关窗口时丢失高度记忆。

#### Task 8：ITab_GearManager tierCodeRect 防截断

**文件**：`Source/AutoEverything/UI/ITab_GearManager.cs`

**修改**：L266-275

**修改前**：
```csharp
Rect tierCodeRect = l.GetRect(22f);
GUI.color = ColorLabelGray;
Text.Anchor = TextAnchor.MiddleLeft;
bool prevWrap = Text.WordWrap;
Text.WordWrap = false;
Widgets.Label(tierCodeRect, "AE_ReallocRules_CurrentTier".Translate() + ": " + tierCode);
Text.WordWrap = prevWrap;
Text.Anchor = TextAnchor.UpperLeft;
GUI.color = Color.white;
TooltipHandler.TipRegion(tierCodeRect, "AE_ReallocRules_CustomTier_Desc".Translate());
```

**修改后**：
```csharp
Rect tierCodeRect = l.GetRect(22f);
GUI.color = ColorLabelGray;
Text.Anchor = TextAnchor.MiddleLeft;
bool prevWrap = Text.WordWrap;
Text.WordWrap = false;
GameFont prevFont = Text.Font;
string tierCodeLabel = "AE_ReallocRules_CurrentTier".Translate() + ": " + tierCode;
// 超宽时缩字号避免截断（tierCode 可能很长，如 "当前评级: SSS(SSS)#王五"）
if (Text.CalcSize(tierCodeLabel).x > tierCodeRect.width)
{
    Text.Font = GameFont.Tiny;
}
Widgets.Label(tierCodeRect, tierCodeLabel);
Text.Font = prevFont;
Text.WordWrap = prevWrap;
Text.Anchor = TextAnchor.UpperLeft;
GUI.color = Color.white;
TooltipHandler.TipRegion(tierCodeRect, "AE_ReallocRules_CustomTier_Desc".Translate());
```

**关键点**：
1. 用 `Text.CalcSize(label).x` 验证宽度
2. 超宽时 `Text.Font = GameFont.Tiny`
3. 必须保存 `prevFont` 并在绘制后恢复（避免影响后续渲染）
4. `WordWrap = false` 已设置，超宽时不换行只截断，缩字号可避免截断

#### Task 9：阶段 C make rebuild-check 最终验证

**命令**：`make rebuild-check`
**预期**：0 警告 0 错误，输出 DLL 存在。

---

## 假设与决策

### 假设
1. `make check` 与 `make rebuild-check` 命令在仓库根目录可用（前一阶段已验证）。
2. `TraitDefCache.Nimble/Beauty/Pyromaniac/SlowLearner/Wimp/Industriousness/Neurotic` 均已在 TraitDefCache 中定义（已通过 Read 验证）。
3. `ApparelTraitScorer.cs` 当前未引用 `AutoEverything.Core`，需新增 `using`。
4. `CompGearManager.cs` 是否已引用 `AutoEverything.Core` 需在实施时确认（如无则新增 using）。
5. ITab_GearManager L798-830 的另一个 `DrawLabeledRowAutoSize` 方法有调用方，保留不动。

### 决策
1. **优先级**：阶段 A（BUG-A 修复）最高，必须先做且单独 make check 验证，避免后续任务被编译错误阻塞。
2. **NEW-1/NEW-2 纳入本计划**：与上一阶段 Task #8 同模式，统一收尾 DRY，避免遗留半成品。
3. **Task 8 字体恢复**：用 `GameFont prevFont = Text.Font` 保存恢复，避免污染后续渲染状态。
4. **不修改 `IsWearingShieldBelt`**：WeaponTraitScorer 中该方法已用 `GearDefClassifier.IsShieldBelt`，语义正确（只检测护盾腰带，非任意 belt），保留。
5. **不优化 `ReorderColonistBar` 的 `new List<Pawn>()`**：手动触发非热路径，避免过度设计（与原计划 PERF-5 决策一致）。

### 取消项
- **CombatEvaluator 内部缓存**：保持无状态评估器模式，缓存由调用方管理（与原计划决策一致）。
- **ReorderColonistBar pawnsBuffer 静态化**：手动触发非热路径，不做。
- **WeaponTraitScorer.IsWearingShieldBelt 改 GearDefClassifier**：已用 IsShieldBelt，无重复，不改。

## 验证步骤

### 每阶段验证
1. **阶段 A**：`make check` 通过（修复 6 处编译阻塞后应能编译）
2. **阶段 B**：`make check` 通过（DRY 收尾 + 死代码删除）
3. **阶段 C**：`make rebuild-check` 通过（完整重建验证）

### 最终验证
- 0 警告 0 错误
- 输出 DLL 存在
- 无新增 using 循环依赖（Core 命名空间不依赖 RoleEvaluation/AutoEquipment）

### 文档同步
本计划不修改玩家可见契约（评分公式、阈值、规则），无需同步 README。
仅代码内部重构 + BUG 修复，README 已与当前规则一致。

## 实施顺序

1. **Task 1 → Task 2**（阶段 A：修复编译阻塞，单独验证）
2. **Task 3 → Task 4 → Task 5 → Task 6**（阶段 B：DRY 收尾 + 死代码删除）
3. **Task 7 → Task 8 → Task 9**（阶段 C：UI 改进 + 最终重建验证）

每阶段 make check 通过后再进入下一阶段，避免错误累积。
