# 重构 / BUG 修复 / 性能优化 / UI 改进计划

> 目标：结合规范整合精简代码逻辑，修复已发现 BUG，提高 Tick 路径性能与 UI 一致性。
> 遵循 Karpathy 四原则：简单优于复杂、删除优于扩展、理解优于记忆、原型优于规划。

## 当前状态分析

### 已完成（前一阶段）
- BUG-1：`AutoExecutor.ExecuteGear` L204-210 新增 `IsChild` 检查，未成年仅评估防具 ✓
- BUG-2：`SidearmAllocator.CollectCandidateWeapons` L139 新增 `IsForbidden` 检查 ✓
- BUG-3 部分：`BeltAllocator.AllocateAllColonists` L59-135 已包 try-catch + `BeltErrorSalt = 0xB1A0` ✓

### 待修复（本计划范围）

#### P0 BUG（阶段一）
- **BUG-3 剩余**：`SidearmAllocator.AllocateAllColonists`（L59-103）**未包 try-catch**，与 `BeltAllocator` 不一致。
  异常会冒泡到 `CompGearManager.CompTick`，可能影响其他 Pawn 评估。

#### P0 性能（阶段二）
- **PERF-1**：`BeltAllocator` L74-75 + `SidearmAllocator` L74-75 的 `Sort` 比较器内调用 `CombatEvaluator.GetAutoCombatTier`，
  每次比较都全量重算（18+ 次技能查询 + 特质扫描），O(n log n) 次重复计算。
  50 人时约 300+ 次重复调用，Tick 路径性能瓶颈。
- **PERF-2**：`GlobalAllocator.ReallocateApparel` L387 重复调用 `CombatEvaluator.GetCombatTier(pawn)`，
  L255 已将结果缓存到 `tierCache`，但 L387 未复用。
- **PERF-3**：`GlobalAllocator` L84/250/251 每次 `new Dictionary<Pawn,float/int>`，
  与 L34-41 静态字段复用模式不一致（手动触发但应保持一致性）。
- **PERF-4**：`AESettings` 三个 Sort 比较器（L302-336）每次比较都调用 `GetCombatTier` + `ComputeCombatValue` / `DetectRole`，
  O(n log n) 次重复计算。虽是低频路径（玩家点按钮触发），但与 PERF-1 模式一致优化。

#### P1 代码精简（阶段三）
- **SIMPLE-1**：`CombatEvaluator` L40-43 与 `WeaponTraitScorer` L20/24/25/26 重复定义
  `shootingAccuracyDef` / `toughDef` / `nimbleDef` / `bloodlustDef` 4 个 TraitDef。
  违反"单一职责"与"DRY"，应抽取到 `Core/TraitDefCache.cs`。
- **SIMPLE-2**：`CombatEvaluator.StripTierTagPrefixFromLabel`（L471-482）与 `AESettings.StripTierTagPrefix`
  逻辑重复，仅独立实现避免跨类耦合。应抽取到 `Core/TierTagHelper.cs` 统一。
- **SIMPLE-3**：`BeltAllocator.HasBelt`（L233-242）+ `WeaponTraitScorer.IsWearingShieldBelt`（L126-135）
  各自实现"遍历 WornApparel 检测 belt 层"逻辑，应统一到 `GearDefClassifier.HasBeltLayerApparel(Pawn)`。
- **SIMPLE-4**：`ITab_GearManager.DrawLabeledRow`（L836-856）无调用方，是死代码。
  应删除（"删除优于扩展"）。

#### P2 UI 改进（阶段四）
- **UI-1**：`Dialog_GlobalReallocate` L24 `private float contentHeight = 900f` 是实例字段，
  每次重开窗口会丢失滚动位置记忆。应改 `private static float`。
- **UI-2**：`ITab_GearManager` L266-275 `tierCodeRect` 绘制 `当前评级: SSS(SSS)#王五` 长字符串，
  虽已设 `WordWrap=false`，但未用 `CalcSize` 验证宽度，超宽时直接截断。
  改进：用 `Text.CalcSize` 验证，超宽时缩字号或截断尾部（保留前缀可读）。

## 实施方案

### 阶段一：P0 BUG 修复（1 项）

#### Task 1：SidearmAllocator AllocateAllColonists 加 try-catch

**文件**：`Source/AutoEverything/Allocation/SidearmAllocator.cs`

**修改**：将 `AllocateAllColonists` 方法体（L60-102）包入 try-catch，
新增 `SidearmErrorSalt = 0xB1A1` 常量（与 `BeltAllocator.BeltErrorSalt = 0xB1A0` 区分）。

**模式参照**（BeltAllocator 已完成）：
```csharp
private static void AllocateAllColonists()
{
    try
    {
        // ... 原有逻辑 ...
    }
    catch (Exception ex)
    {
        Log.ErrorOnce("[AutoEverything] EMP手雷分配失败: " + ex.Message, SidearmErrorSalt);
    }
}

// EMP 手雷分配错误去重 salt，与 BeltAllocator.BeltErrorSalt 区分
private const int SidearmErrorSalt = 0xB1A1;
```

**需新增 using**：`using System;`（Exception 类型）

#### Task 2：阶段一 make check 验证

**命令**：`make check`
**预期**：0 警告 0 错误。

---

### 阶段二：P0 性能优化（4 项）

#### Task 3：BeltAllocator/SidearmAllocator 新增 tierCache 预计算

**文件**：
- `Source/AutoEverything/Allocation/BeltAllocator.cs`
- `Source/AutoEverything/Allocation/SidearmAllocator.cs`

**修改**：两个类各新增静态字段 `Dictionary<Pawn, CombatTier> tierCache`，
在 `AllocateAllColonists` 排序前预计算，比较器读 cache 替代重复调用 `GetAutoCombatTier`。

**BeltAllocator 修改点**：
1. L37 后新增字段：
   ```csharp
   // 评级缓存：排序前预计算，避免 Sort 比较器内 O(n log n) 次重复调用 GetAutoCombatTier
   private static readonly Dictionary<Pawn, CombatTier> tierCache = new Dictionary<Pawn, CombatTier>();
   ```
2. L61（Clear 段）后新增 `tierCache.Clear();`
3. L74-75 比较器前插入预计算循环：
   ```csharp
   // 预计算评级缓存：避免 Sort 比较器内重复调用 GetAutoCombatTier（O(n log n) 次技能查询）
   for (int i = 0; i < candidatePawns.Count; i++)
   {
       Pawn p = candidatePawns[i];
       tierCache[p] = CombatEvaluator.GetAutoCombatTier(p);
   }
   ```
4. L74-75 比较器改为：
   ```csharp
   candidatePawns.Sort((a, b) => tierCache[a].CompareTo(tierCache[b]));
   ```

**SidearmAllocator 修改点**：完全相同的模式（L39 后加字段、L62 后加 Clear、L73 前加预计算、L74-75 改比较器）。

#### Task 4：GlobalAllocator L387 改用已有 tierCache

**文件**：`Source/AutoEverything/Allocation/GlobalAllocator.cs`

**修改**：L387 `CombatEvaluator.GetCombatTier(pawn)` 改为 `(CombatTier)tierCache[pawn]`。
`tierCache` 在 L255 已缓存 `(int)CombatEvaluator.GetCombatTier(p)`，直接复用避免重复计算。

**修改前**（L387-388）：
```csharp
CombatTier pawnTier = CombatEvaluator.GetCombatTier(pawn);
score += (float)pawnTier * 0.5f;
```

**修改后**：
```csharp
// 复用 L255 已缓存的 tierCache，避免重复调用 GetCombatTier
CombatTier pawnTier = (CombatTier)tierCache[pawn];
score += (float)pawnTier * 0.5f;
```

#### Task 5：GlobalAllocator Dictionary 静态化

**文件**：`Source/AutoEverything/Allocation/GlobalAllocator.cs`

**修改**：将 L84/250/251 的 `new Dictionary<...>` 改为静态字段，与 L34-41 `candidateWeapons` / `assignedWeaponIds` 模式一致。

1. L41 后新增静态字段：
   ```csharp
   // 战斗价值/评级/价值评分缓存：排序前预计算，避免 Sort 比较器内重复调用
   // 与 candidateWeapons 等同模式：Clear + 复用，避免 GC
   private static readonly Dictionary<Pawn, float> combatValueCache = new Dictionary<Pawn, float>();
   private static readonly Dictionary<Pawn, int> tierCache = new Dictionary<Pawn, int>();
   private static readonly Dictionary<Pawn, float> valueScoreCache = new Dictionary<Pawn, float>();
   ```
2. L53-55（ReallocateAll 开头 Clear 段）新增：
   ```csharp
   combatValueCache.Clear();
   tierCache.Clear();
   valueScoreCache.Clear();
   ```
3. L84 `var combatValueCache = new Dictionary<Pawn, float>();` → 删除（用静态字段）
4. L250 `var tierCache = new Dictionary<Pawn, int>();` → 删除
5. L251 `var valueScoreCache = new Dictionary<Pawn, float>();` → 删除

#### Task 6：AESettings Sort 比较器加预计算缓存

**文件**：`Source/AutoEverything/Core/AESettings.cs`

**修改**：三个 Sort 比较器（`ComparePawnByTierThenValueDesc` L302-311、`ComparePawnByCombatValueOnlyDesc` L318-323、`ComparePawnByRoleThenValueDesc` L330-336）每次比较都调用 `GetCombatTier` + `ComputeCombatValue` / `DetectRole`，应预计算缓存。

**模式**：在 `ReorderColonistBar` L277-294 排序前预计算 cache，比较器读 cache。

1. L294 后新增静态缓存字段（与 `tierTagOriginals` 同位置）：
   ```csharp
   // 殖民者栏排序缓存：排序前预计算，避免 Sort 比较器内重复调用
   // GetCombatTier/ComputeCombatValue/DetectRole（均涉及技能/特质查询）
   private static readonly Dictionary<Pawn, CombatTier> sortTierCache = new Dictionary<Pawn, CombatTier>();
   private static readonly Dictionary<Pawn, float> sortValueCache = new Dictionary<Pawn, float>();
   private static readonly Dictionary<Pawn, int> sortRoleCache = new Dictionary<Pawn, int>();
   ```
2. L279-285（ReorderColonistBar 收集 pawns 后）新增预计算：
   ```csharp
   sortTierCache.Clear();
   sortValueCache.Clear();
   sortRoleCache.Clear();
   for (int i = 0; i < pawns.Count; i++)
   {
       Pawn p = pawns[i];
       sortTierCache[p] = CombatEvaluator.GetCombatTier(p);
       sortValueCache[p] = CombatEvaluator.ComputeCombatValue(p);
       sortRoleCache[p] = GetRoleOrder(RoleDetector.DetectRole(p));
   }
   ```
3. L286 `pawns.Sort(comparison);` 不变（comparison 是参数）
4. 三个比较器改为读 cache：
   ```csharp
   private static int ComparePawnByTierThenValueDesc(Pawn a, Pawn b)
   {
       CombatTier ta = sortTierCache[a];
       CombatTier tb = sortTierCache[b];
       if (ta != tb) return tb.CompareTo(ta);
       return sortValueCache[b].CompareTo(sortValueCache[a]);
   }
   
   private static int ComparePawnByCombatValueOnlyDesc(Pawn a, Pawn b)
   {
       return sortValueCache[b].CompareTo(sortValueCache[a]);
   }
   
   private static int ComparePawnByRoleThenValueDesc(Pawn a, Pawn b)
   {
       int ra = sortRoleCache[a];
       int rb = sortRoleCache[b];
       if (ra != rb) return ra.CompareTo(rb);
       return ComparePawnByTierThenValueDesc(a, b);
   }
   ```

**注**：`ApplyTierTagsToAllPawns` 内部也调用 `GetAutoCombatTier`（L154），但那是单次循环非 Sort，不优化。

#### Task 7：阶段二 make check 验证

**命令**：`make check`
**预期**：0 警告 0 错误。

---

### 阶段三：P1 代码精简（4 项）

#### Task 8：新建 Core/TraitDefCache.cs 统一 TraitDef

**新文件**：`Source/AutoEverything/Core/TraitDefCache.cs`

**内容**：集中定义 CombatEvaluator/WeaponTraitScorer 重复的 TraitDef 查询。
标记 `[StaticConstructorOnStartup]` 确保主线程加载（虽然 DefDatabase.GetNamed 在静态字段初始化器中
调用通常 OK，但规则要求"含 ContentFinder/DefDatabase 的工具类需标记"）。

```csharp
using RimWorld;
using Verse;

namespace AutoEverything.Core
{
    /// <summary>
    /// TraitDef 查询缓存：集中定义多 degree/非原生 DefOf 的特质查询。
    /// 抽取自 CombatEvaluator 与 WeaponTraitScorer 的重复定义，
    /// 用 GetNamed(false) 安全查询，未加载 DLC 时返回 null 跳过。
    /// 注：原生 DefOf（如 TraitDefOf.Brawler）始终存在，直接引用无需缓存。
    /// </summary>
    [StaticConstructorOnStartup]
    public static class TraitDefCache
    {
        public static readonly TraitDef ShootingAccuracy = DefDatabase<TraitDef>.GetNamed("ShootingAccuracy", false);
        public static readonly TraitDef Tough = DefDatabase<TraitDef>.GetNamed("Tough", false);
        public static readonly TraitDef Nimble = DefDatabase<TraitDef>.GetNamed("Nimble", false);
        public static readonly TraitDef Bloodlust = DefDatabase<TraitDef>.GetNamed("Bloodlust", false);
        public static readonly TraitDef Industriousness = DefDatabase<TraitDef>.GetNamed("Industriousness", false);
        public static readonly TraitDef Neurotic = DefDatabase<TraitDef>.GetNamed("Neurotic", false);
        public static readonly TraitDef Beauty = DefDatabase<TraitDef>.GetNamed("Beauty", false);
        public static readonly TraitDef Pyromaniac = DefDatabase<TraitDef>.GetNamed("Pyromaniac", false);
        public static readonly TraitDef SlowLearner = DefDatabase<TraitDef>.GetNamed("SlowLearner", false);
        public static readonly TraitDef Wimp = DefDatabase<TraitDef>.GetNamed("Wimp", false);
        public static readonly TraitDef TooSmart = DefDatabase<TraitDef>.GetNamed("TooSmart", false);
        public static readonly TraitDef Joyous = DefDatabase<TraitDef>.GetNamed("Joyous", false);
        public static readonly TraitDef BodyMastery = DefDatabase<TraitDef>.GetNamed("BodyMastery", false);
        public static readonly TraitDef VoidFascination = DefDatabase<TraitDef>.GetNamed("VoidFascination", false);
        public static readonly TraitDef Occultist = DefDatabase<TraitDef>.GetNamed("Occultist", false);
        public static readonly TraitDef Disturbing = DefDatabase<TraitDef>.GetNamed("Disturbing", false);
    }
}
```

**修改 CombatEvaluator.cs**：
- 删除 L40-43、L49-54、L63-68 共 16 个 TraitDef 字段
- 改为引用 `TraitDefCache.Xxx`
- 新增 `using AutoEverything.Core;`（已存在 L3）

**修改 WeaponTraitScorer.cs**：
- 删除 L20、L24-26 共 4 个 TraitDef 字段
- 改为引用 `TraitDefCache.Xxx`
- 新增 `using AutoEverything.Core;`

#### Task 9：新建 Core/TierTagHelper.cs 统一 StripTierTagPrefix

**新文件**：`Source/AutoEverything/Core/TierTagHelper.cs`

**内容**：统一 `CombatEvaluator.StripTierTagPrefixFromLabel`（L471-482）与 `AESettings.StripTierTagPrefix` 的重复逻辑。

```csharp
using System;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.Core
{
    /// <summary>
    /// 评级前缀剥离工具：统一 CombatEvaluator 与 AESettings 的重复实现。
    /// 格式：档次名 + # + 原名（如 "S#王五"），支持多字母前缀 SS#/SSS#。
    /// 必须是合法 CombatTier 枚举名才剥离，避免误把玩家自定义 Nick 当评级前缀。
    /// </summary>
    public static class TierTagHelper
    {
        /// <summary>
        /// 剥离 Label/Nick 上的评级前缀。若无前缀返回原值。
        /// </summary>
        public static string Strip(string label)
        {
            if (string.IsNullOrEmpty(label)) return label;
            int hashIdx = label.IndexOf('#');
            // hashIdx <= 0：无 # 或 # 在首位；hashIdx > 3：前缀超长（最长 SSS=3 字符）
            if (hashIdx <= 0 || hashIdx > 3) return label;
            string prefix = label.Substring(0, hashIdx);
            return Enum.TryParse(prefix, out CombatTier _)
                ? label.Substring(hashIdx + 1)
                : label;
        }

        /// <summary>
        /// 检查 Label/Nick 是否带有合法评级前缀。
        /// </summary>
        public static bool HasPrefix(string label)
        {
            if (string.IsNullOrEmpty(label)) return false;
            int hashIdx = label.IndexOf('#');
            if (hashIdx <= 0 || hashIdx > 3) return false;
            string prefix = label.Substring(0, hashIdx);
            return Enum.TryParse(prefix, out CombatTier _);
        }
    }
}
```

**修改 CombatEvaluator.cs**：
- 删除 `StripTierTagPrefixFromLabel` 方法（L471-482）
- L464 `GetPawnLookupName` 改为 `return TierTagHelper.Strip(pawn.LabelShort ?? string.Empty);`
- 新增 `using AutoEverything.Core;`（已存在）

**修改 AESettings.cs**：
- 删除 `StripTierTagPrefix` 私有方法（需先确认位置）
- 删除 `HasTierTagPrefix` 私有方法（如果有）
- 改为调用 `TierTagHelper.Strip` / `TierTagHelper.HasPrefix`
- 新增 `using AutoEverything.Core;`（已存在 L8）

**注**：需先读 AESettings 完整文件确认 StripTierTagPrefix/HasTierTagPrefix 位置，本计划阶段再读。

#### Task 10：GearDefClassifier 新增 HasBeltLayerApparel

**文件**：`Source/AutoEverything/AutoEquipment/GearDefClassifier.cs`

**新增方法**：
```csharp
/// <summary>
/// 检查 Pawn 是否已穿戴 belt 层附件（护盾腰带/消防背包等）。
/// 统一 BeltAllocator.HasBelt 与 WeaponTraitScorer.IsWearingShieldBelt 的重复遍历逻辑。
/// </summary>
public static bool HasBeltLayerApparel(Pawn pawn)
{
    if (pawn?.apparel?.WornApparel == null) return false;
    List<Apparel> worn = pawn.apparel.WornApparel;
    for (int i = 0; i < worn.Count; i++)
    {
        Apparel ap = worn[i];
        if (ap.def?.apparel?.layers == null) continue;
        if (ap.def.apparel.layers.Contains(ApparelLayerDefOf.Belt)) return true;
    }
    return false;
}
```

**需新增 using**：`using System.Collections.Generic;`（List<T>）、`using Verse;`（Pawn/Apparel）

**修改 BeltAllocator.cs**：
- 删除 `HasBelt` 方法（L233-242）
- L83 `if (HasBelt(pawn)) continue;` 改为 `if (GearDefClassifier.HasBeltLayerApparel(pawn)) continue;`
- L159 `if (HasBelt(pawn)) continue;` 同改

**修改 WeaponTraitScorer.cs**：
- 删除 `IsWearingShieldBelt` 方法（L126-135）
- L47 `if (isRanged && IsWearingShieldBelt(pawn))` 改为 `if (isRanged && GearDefClassifier.HasBeltLayerApparel(pawn))`
- 注：语义略有变化——原 `IsWearingShieldBelt` 检测"shield+belt"，新 `HasBeltLayerApparel` 检测"任意 belt 层"。
  但实际游戏中 belt 层只有护盾腰带/消防背包/个人护盾等，且 WeaponTraitScorer 的目的是
  "穿护盾腰带时拒绝远程武器"，而消防背包不影响远程武器。
  **决策**：为保留原语义，GearDefClassifier 新增 `HasShieldBelt` 而非 `HasBeltLayerApparel`，
  并让 BeltAllocator 的 `HasBelt` 保留（它需要检测任意 belt 层空位）。
  
  **修订方案**：
  - GearDefClassifier 新增 `HasShieldBelt(Pawn)`（检测护盾腰带，统一 WeaponTraitScorer 的语义）
  - WeaponTraitScorer 改调 `GearDefClassifier.HasShieldBelt(pawn)`
  - BeltAllocator 的 `HasBelt` 保留（语义不同：检测任意 belt 层空位，需自己遍历）
  
  **进一步简化**：实际上 BeltAllocator 的 `HasBelt` 也是遍历 WornApparel 检测 belt 层，
  与 `HasBeltLayerApparel` 语义完全一致。所以：
  - GearDefClassifier 新增 `HasBeltLayerApparel(Pawn)`（检测任意 belt 层）
  - BeltAllocator 删除 `HasBelt`，改调 `GearDefClassifier.HasBeltLayerApparel`
  - WeaponTraitScorer 的 `IsWearingShieldBelt` 保留（语义不同：只检测护盾腰带，非任意 belt）
    但内部改调 `GearDefClassifier.IsShieldBelt`（已存在 L34-41）
  
  **最终决策**（最小改动）：
  - GearDefClassifier 新增 `HasBeltLayerApparel(Pawn)`
  - BeltAllocator 删除 `HasBelt`，改调 `GearDefClassifier.HasBeltLayerApparel`
  - WeaponTraitScorer 不改（`IsWearingShieldBelt` 已用 `GearDefClassifier.IsShieldBelt`，无重复）

#### Task 11：删除 ITab_GearManager DrawLabeledRow 死代码

**文件**：`Source/AutoEverything/UI/ITab_GearManager.cs`

**修改**：删除 L832-856 `DrawLabeledRow` 方法（无调用方）。

**验证**：先 Grep 确认无调用方再删除。

#### Task 12：阶段三 make check 验证

**命令**：`make check`
**预期**：0 警告 0 错误。

---

### 阶段四：P2 UI 改进（2 项）

#### Task 13：Dialog_GlobalReallocate contentHeight 改 static

**文件**：`Source/AutoEverything/UI/Dialog_GlobalReallocate.cs`

**修改**：L24 `private float contentHeight = 900f;` 改为 `private static float contentHeight = 900f;`

**理由**：与 L21 `private static Vector2 scrollPos` 一致，避免多次开关窗口时丢失高度记忆。

#### Task 14：ITab_GearManager tierCodeRect 防截断改进

**文件**：`Source/AutoEverything/UI/ITab_GearManager.cs`

**修改**：L266-275 `tierCodeRect` 绘制前用 `Text.CalcSize` 验证宽度，超宽时缩小字号（Tiny）避免截断。

**修改前**（L266-275）：
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
string tierCodeLabel = "AE_ReallocRules_CurrentTier".Translate() + ": " + tierCode;
// 超宽时缩字号避免截断（tierCode 可能很长，如 "当前评级: SSS(SSS)#王五"）
Vector2 labelSize = Text.CalcSize(tierCodeLabel);
if (labelSize.x > tierCodeRect.width)
{
    Text.Font = GameFont.Tiny;
}
Widgets.Label(tierCodeRect, tierCodeLabel);
Text.Font = GameFont.Small;
Text.WordWrap = prevWrap;
Text.Anchor = TextAnchor.UpperLeft;
GUI.color = Color.white;
TooltipHandler.TipRegion(tierCodeRect, "AE_ReallocRules_CustomTier_Desc".Translate());
```

#### Task 15：阶段四 make rebuild-check 最终验证

**命令**：`make rebuild-check`
**预期**：0 警告 0 错误。

---

## 假设与决策

### 假设
1. `make check` 与 `make rebuild-check` 命令在仓库根目录可用（前一阶段已验证）。
2. `CombatEvaluator.GetAutoCombatTier` 本身不改（保持无状态评估器模式），仅在调用方加 cache。
3. `WorkAllocator.cs` L198 注释明确"两组要同时存在，复用静态列表需栈内分配"——不改。
4. 新建文件的命名空间与文件夹匹配（IDE0130）：
   - `Source/AutoEverything/Core/TraitDefCache.cs` → `namespace AutoEverything.Core`
   - `Source/AutoEverything/Core/TierTagHelper.cs` → `namespace AutoEverything.Core`

### 决策
1. **CombatEvaluator 不加内部缓存**：保持"无状态评估器"模式，缓存由调用方管理。
   理由：单一职责 + 调用方已知生命周期（BeltAllocator/SidearmAllocator 周期触发，AESettings 手动触发）。
2. **Task 10 最终决策**：GearDefClassifier 仅新增 `HasBeltLayerApparel`，
   BeltAllocator 改调它，WeaponTraitScorer 不改（其 `IsWearingShieldBelt` 已用 `IsShieldBelt`，无重复）。
3. **Task 9 需先读 AESettings 完整文件**确认 `StripTierTagPrefix`/`HasTierTagPrefix` 位置再删除。
4. **PERF-5 不做**（ReorderColonistBar 的 `new List<Pawn>()`）：手动触发非热路径，避免过度设计。

### 取消项
- **PERF-5**（ReorderColonistBar pawnsBuffer 静态化）：手动触发，非热路径，不做。
- **CombatEvaluator 内部缓存**：违反无状态原则，不做。
- **WeaponTraitScorer 改 GearDefClassifier.HasShieldBelt**：已用，无重复，不改。

## 验证步骤

### 每阶段验证
1. **阶段一**：`make check` 通过
2. **阶段二**：`make check` 通过
3. **阶段三**：`make check` 通过
4. **阶段四**：`make rebuild-check` 通过（完整重建验证）

### 最终验证
- 0 警告 0 错误
- 输出 DLL 存在
- 无新增 using 循环依赖

### 文档同步
本计划不修改玩家可见契约（评分公式、阈值、规则），无需同步 README。
仅代码内部重构，README 已与当前规则一致。

## 实施顺序

1. Task 1 → Task 2（阶段一）
2. Task 3 → Task 4 → Task 5 → Task 6 → Task 7（阶段二）
3. Task 8 → Task 9 → Task 10 → Task 11 → Task 12（阶段三）
4. Task 13 → Task 14 → Task 15（阶段四）

每阶段 make check 通过后再进入下一阶段，避免错误累积。
