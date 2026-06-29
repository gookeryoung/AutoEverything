# 项目重构：剩余执行计划

## Context

本计划承接 `project-restructure-plan.md`（已批准）。重启上下文后核对实际文件系统，确认以下工作已经完成：

### 已完成（无需重复）

1. **显示名"自动装备" → "自动万物"**：
   - `Languages/ChineseSimplified/Keyed/AE_Keyed.xml`：6 处替换完成
   - `Languages/English/Keyed/AE_Keyed.xml`：使用 "Auto Everything"
   - `About/About.xml`：description 改为"智能自动万物管理 MOD"（保留动词"自动装备合适的武器"）
   - `README.md`：标题改为"智能自动万物管理 MOD"（保留"传统的'自动装备'MOD"作为类别引用）
   - `.trae/rules/autoeverything-project.md`：已完成
   - `Source/AutoEverything/CompGearManager.cs`、`ITab_GearManager.cs`：注释已改

2. **拆分 SidearmAllocator.cs**（已创建新文件，旧文件待删除）：
   - `Source/AutoEverything/Core/CombatTier.cs` ✓（namespace AutoEverything.Core，仅 CombatTier 枚举）
   - `Source/AutoEverything/RoleEvaluation/CombatEvaluator.cs` ✓（namespace AutoEverything.RoleEvaluation，含 5 个公共方法 + 14 个 TraitDef 字段 + 私有辅助）
   - `Source/AutoEverything/Allocation/SidearmAllocator.cs` ✓（namespace AutoEverything.Allocation，仅分配逻辑，引用 CombatEvaluator）

3. **AutoEquipment → AutoEverything 重命名**：全部 .cs 文件 namespace 已是 `AutoEverything`，Harmony ID 已是 `gookeryoung.autoeverything`。

### 待完成（本计划范围）

实际文件系统核对发现：根目录下 **`SidearmAllocator.cs`（旧版）与 `SGSettings.cs`（38KB 未拆分）仍存在**；其他 35 个 .cs 文件仍是平铺结构，未移入新文件夹；命名空间仍是 `AutoEverything`，未按功能模块化；调用点仍引用 `SidearmAllocator.ComputeCombatValue` 等已迁移的方法。

---

## 目标文件夹结构（确认版）

```
Source/AutoEverything/
├── AutoEverything.csproj            # SDK 风格，默认 globbing，无需改
├── Core/                            → namespace AutoEverything.Core
│   ├── ModController.cs
│   ├── HarmonyPatches.cs
│   ├── AutoEverythingMod.cs         # 从 SGSettings 拆出
│   ├── AESettings.cs                # 从 SGSettings 拆出
│   ├── ColonistBarSortMode.cs      # 从 SGSettings 拆出
│   ├── DLCCompat.cs
│   ├── AEDebug.cs                   # 由 DebugHelper.cs 重命名（类名不变）
│   ├── DebugMonitor.cs
│   ├── PawnSuitabilityChecker.cs
│   └── CombatTier.cs                # 已创建
├── RoleEvaluation/                  → namespace AutoEverything.RoleEvaluation
│   ├── PawnRole.cs
│   ├── GearContext.cs
│   ├── PawnStateCleaner.cs
│   └── CombatEvaluator.cs           # 已创建
├── AutoEquipment/                   → namespace AutoEverything.AutoEquipment
│   ├── CompGearManager.cs
│   ├── GearScorer.cs
│   ├── GearDefClassifier.cs
│   └── Scoring/                     → namespace AutoEverything.AutoEquipment.Scoring
│       ├── IScorer.cs, ScoringPipeline.cs, ScoringPipelineFactory.cs
│       ├── ScoreBreakdown.cs, GearWeights.cs, GearPreset.cs, GearPolicyEngine.cs
│       ├── Weapon/                  → ...AutoEquipment.Scoring.Weapon
│       └── Apparels/                → ...AutoEquipment.Scoring.Apparels
├── Allocation/                      → namespace AutoEverything.Allocation
│   ├── GlobalAllocator.cs
│   ├── SidearmAllocator.cs           # 已创建
│   ├── BeltAllocator.cs
│   └── PawnCombatProfile.cs
└── UI/                              → namespace AutoEverything.UI
    ├── ITab_GearManager.cs
    ├── Dialog_GlobalReallocate.cs
    └── PresetDetailsWindow.cs        # 从 SGSettings 拆出
```

---

## 执行步骤

### 步骤 1：拆分 SGSettings.cs

源文件 `Source/AutoEverything/SGSettings.cs`（801 行）包含 4 个不相关类型，按以下边界拆分：

**新建 `Core/ColonistBarSortMode.cs`**（namespace AutoEverything.Core）：
- 移动 `enum ColonistBarSortMode : byte`（源文件 9-24 行）
- 添加 `using` 无需（纯枚举）

**新建 `Core/AESettings.cs`**（namespace AutoEverything.Core）：
- 移动 `class AESettings : ModSettings`（源文件 26-735 行）
- 添加 `using`：
  - `using System.Collections.Generic;`（List/Dictionary）
  - `using RimWorld;`（Pawn/ModSettings/PawnsFinder/Map）
  - `using UnityEngine;`（Vector2/Rect）
  - `using Verse;`（Scribe_*）
  - `using AutoEverything.RoleEvaluation;`（RoleDetector/Role/CombatEvaluator 调用）
  - `using AutoEverything.AutoEquipment;`（CompGearManager.ReloadAllColonists 调用）
  - `using AutoEverything.AutoEquipment.Scoring;`（GearPolicyEngine/GearPreset/GearWeights 调用）
  - `using AutoEverything.UI;`（PresetDetailsWindow 实例化）
- **关键调用点改名**（5 处）：
  - L164: `SidearmAllocator.GetAutoCombatTier(pawn)` → `CombatEvaluator.GetAutoCombatTier(pawn)`
  - L315-316: `SidearmAllocator.GetCombatTier(a/b)` → `CombatEvaluator.GetCombatTier(a/b)`
  - L318-319: `SidearmAllocator.ComputeCombatValue(a/b)` → `CombatEvaluator.ComputeCombatValue(a/b)`
  - L330-331: 同上

**新建 `Core/AutoEverythingMod.cs`**（namespace AutoEverything.Core）：
- 移动 `class AutoEverythingMod : Mod`（源文件 737-752 行）
- 添加 `using RimWorld;`（Mod）、`using UnityEngine;`（Rect）

**新建 `UI/PresetDetailsWindow.cs`**（namespace AutoEverything.UI）：
- 移动 `class PresetDetailsWindow : Window`（源文件 754-800 行）
- 添加 `using`：`using RimWorld;`（Window/GameFont/Listing_Standard）、`using UnityEngine;`（Rect/Vector2）、`using AutoEverything.AutoEquipment.Scoring;`（GearPolicyEngine/GearWeights）

**删除 `Source/AutoEverything/SGSettings.cs`**

### 步骤 2：删除旧 SidearmAllocator.cs

`Source/AutoEverything/SidearmAllocator.cs`（旧版）与新建的 `Allocation/SidearmAllocator.cs` + `RoleEvaluation/CombatEvaluator.cs` + `Core/CombatTier.cs` 重复定义 CombatTier 枚举与 SidearmAllocator 类。**必须删除旧文件**否则编译失败（CS0101 重复类型定义）。

### 步骤 3：移动根级 .cs 文件到目标文件夹

| 源文件（根目录） | 目标路径 |
|---|---|
| `ModController.cs` | `Core/ModController.cs` |
| `HarmonyPatches.cs` | `Core/HarmonyPatches.cs` |
| `DLCCompat.cs` | `Core/DLCCompat.cs` |
| `DebugHelper.cs` | `Core/AEDebug.cs`（**重命名**，类名 AEDebug 不变） |
| `DebugMonitor.cs` | `Core/DebugMonitor.cs` |
| `PawnSuitabilityChecker.cs` | `Core/PawnSuitabilityChecker.cs` |
| `PawnRole.cs` | `RoleEvaluation/PawnRole.cs` |
| `GearContext.cs` | `RoleEvaluation/GearContext.cs` |
| `PawnStateCleaner.cs` | `RoleEvaluation/PawnStateCleaner.cs` |
| `CompGearManager.cs` | `AutoEquipment/CompGearManager.cs` |
| `GearScorer.cs` | `AutoEquipment/GearScorer.cs` |
| `GearDefClassifier.cs` | `AutoEquipment/GearDefClassifier.cs` |
| `GlobalAllocator.cs` | `Allocation/GlobalAllocator.cs` |
| `BeltAllocator.cs` | `Allocation/BeltAllocator.cs` |
| `PawnCombatProfile.cs` | `Allocation/PawnCombatProfile.cs` |
| `ITab_GearManager.cs` | `UI/ITab_GearManager.cs` |
| `Dialog_GlobalReallocate.cs` | `UI/Dialog_GlobalReallocate.cs` |

### 步骤 4：移动 Scoring 子树到 AutoEquipment/Scoring/

将 `Source/AutoEverything/Scoring/` 整个子树（含 Weapon/ 与 Apparels/ 子目录及全部 29 个 .cs 文件）移到 `Source/AutoEverything/AutoEquipment/Scoring/`。

移动后目录：
```
AutoEquipment/Scoring/
├── IScorer.cs, ScoringPipeline.cs, ScoringPipelineFactory.cs
├── ScoreBreakdown.cs, GearWeights.cs, GearPreset.cs, GearPolicyEngine.cs
├── Weapon/  (10 个 .cs)
└── Apparels/ (12 个 .cs)
```

### 步骤 5：批量更新命名空间声明

每个文件的 `namespace` 行按目标文件夹更新：

| 文件夹 | namespace |
|---|---|
| `Core/*.cs` | `namespace AutoEverything.Core` |
| `RoleEvaluation/*.cs` | `namespace AutoEverything.RoleEvaluation` |
| `AutoEquipment/*.cs` | `namespace AutoEverything.AutoEquipment` |
| `AutoEquipment/Scoring/*.cs` | `namespace AutoEverything.AutoEquipment.Scoring` |
| `AutoEquipment/Scoring/Weapon/*.cs` | `namespace AutoEverything.AutoEquipment.Scoring.Weapon` |
| `AutoEquipment/Scoring/Apparels/*.cs` | `namespace AutoEverything.AutoEquipment.Scoring.Apparels` |
| `Allocation/*.cs` | `namespace AutoEverything.Allocation` |
| `UI/*.cs` | `namespace AutoEverything.UI` |

**注意**：`Scoring/*.cs` 当前是 `AutoEverything.Scoring` 等，需改为 `AutoEverything.AutoEquipment.Scoring` 等（多加一段 `.AutoEquipment`）。

### 步骤 6：批量更新 `using` 语句

每个文件按其引用的类型添加必要的 `using`。预计需求：

- **几乎所有非 Core 文件**需加 `using AutoEverything.Core;`（引用 AESettings/AEDebug/DLCCompat/CombatTier/PawnSuitabilityChecker/ColonistBarSortMode/AutoEverythingMod）
- **全部 Scoring 文件**需加 `using AutoEverything.RoleEvaluation;`（引用 GearContext）
- **CompGearManager.cs、GearScorer.cs** 需加 `using AutoEverything.AutoEquipment.Scoring;`、`using AutoEverything.AutoEquipment.Scoring.Weapon;`、`using AutoEverything.AutoEquipment.Scoring.Apparels;`、`using AutoEverything.RoleEvaluation;`、`using AutoEverything.Allocation;`
- **ScoringPipelineFactory.cs** 需加 `using AutoEverything.AutoEquipment.Scoring.Weapon;`、`using AutoEverything.AutoEquipment.Scoring.Apparels;`、`using AutoEverything.RoleEvaluation;`
- **GlobalAllocator.cs、SidearmAllocator.cs、BeltAllocator.cs** 需加 `using AutoEverything.AutoEquipment;`、`using AutoEverything.AutoEquipment.Scoring;`、`using AutoEverything.RoleEvaluation;`、`using AutoEverything.UI;`（GlobalAllocator 实例化 Dialog_GlobalReallocate）
- **ITab_GearManager.cs、Dialog_GlobalReallocate.cs** 需加 `using AutoEverything.Core;`、`using AutoEverything.RoleEvaluation;`、`using AutoEverything.AutoEquipment;`、`using AutoEverything.AutoEquipment.Scoring;`、`using AutoEverything.Allocation;`
- **AESettings.cs**（拆分后）需加 `using AutoEverything.RoleEvaluation;`、`using AutoEverything.AutoEquipment;`、`using AutoEverything.AutoEquipment.Scoring;`、`using AutoEverything.UI;`
- **HarmonyPatches.cs** 若注册 ITab 类型，需加 `using AutoEverything.UI;`

精确依赖以 `make check` 报错为准（缺失 using → CS0246 找不到类型），按报错逐个补齐。

### 步骤 7：更新调用点

`SidearmAllocator.X` 中评价类方法已迁移到 `CombatEvaluator`，需在以下文件改名：

| 文件 | 改名调用 |
|---|---|
| `BeltAllocator.cs` | `SidearmAllocator.ComputeCombatValue` → `CombatEvaluator.ComputeCombatValue` |
| `GlobalAllocator.cs` | `SidearmAllocator.GetCombatTier/GetAutoCombatTier/ComputeCombatValue/ComputePawnValueScore/GetPawnLookupName` → `CombatEvaluator.X` |
| `ITab_GearManager.cs` | 同上 5 个方法 |
| `AESettings.cs`（拆分后） | `SidearmAllocator.GetAutoCombatTier/GetCombatTier/ComputeCombatValue` → `CombatEvaluator.X` |
| `AEDebug.cs`（重命名后） | `SidearmAllocator.GetAutoCombatTier/GetPawnLookupName` → `CombatEvaluator.X` |

`SidearmAllocator.AllocateForPawn` 保持不变（仍在 Allocation 命名空间）。

### 步骤 8：更新 XML Patch 与外部引用

`Patches/AE_Patches.xml` 中两处 `AutoEverything.ITab_GearManager` 改为 `AutoEverything.UI.ITab_GearManager`：
- 第 13 行 `<li>AutoEverything.ITab_GearManager</li>` → `<li>AutoEverything.UI.ITab_GearManager</li>`
- 第 20 行同上

### 步骤 9：更新 README.md 与项目规则

**README.md**：
- 更新目录结构图（用新的 6 模块结构替换旧平铺结构）
- 更新命名空间示例
- 更新"项目结构"章节说明各模块职责

**`.trae/rules/autoeverything-project.md`**：
- 更新"命名空间与文件夹结构"章节的映射表
- 添加模块职责说明：Core（基础工具）/RoleEvaluation（角色评价）/AutoEquipment（装备评分）/Allocation（全局分配）/UI（界面）

### 步骤 10：验证

在项目根目录执行：
```powershell
make check
```

必须零警告零错误。失败则按编译错误信息逐个修复：
- CS0246（找不到类型）→ 缺失 `using`
- CS0101（重复类型）→ 旧文件未删除
- CS0120（对象引用需 static）→ 调用点未改名

#### Grep 验证

```powershell
# 不应有 AutoEquipment 残留（除计划文档）
grep -r "AutoEquipment" Source/ About/ Patches/ Languages/
# 应只在 .trae/documents/ 历史计划文档中出现

# "自动装备" 残留仅允许以下三处（动词/类别引用）：
# - About/About.xml:9 "自动装备合适的武器"（动词）
# - README.md:13 "传统的'自动装备'MOD"（类别）
# - .trae/documents/ 历史计划文档
```

---

## 关键风险点

1. **Scoring 子树整体迁移**：29 个 .cs 文件的 `namespace AutoEverything.Scoring*` 必须全部改为 `AutoEverything.AutoEquipment.Scoring*`，遗漏一个即编译失败
2. **SGSettings.cs 拆分时 `using` 列表长**：AESettings 引用 RoleEvaluation/AutoEquipment/Scoring/UI 四个命名空间，必须全部添加
3. **旧 SidearmAllocator.cs 必须先删除**：否则与新文件冲突 CS0101
4. **Patches XML 类型名**：必须同步更新为带 UI 命名空间的完整类型名
5. **HarmonyPatches 若在静态构造中引用 ITab**：可能需要 `using AutoEverything.UI;`

## 验证清单

- [ ] `make check` 通过零警告零错误
- [ ] `grep -r "namespace AutoEverything$" Source/` 应只在已弃用的旧文件中（不应在移动后的文件中）
- [ ] `grep -r "SidearmAllocator\.\(ComputeCombatValue\|GetCombatTier\|GetAutoCombatTier\|ComputePawnValueScore\|GetPawnLookupName\)" Source/` 应无结果（已迁移到 CombatEvaluator）
- [ ] `grep -r "AutoEverything\.Scoring" Source/` 应无结果（已改为 AutoEquipment.Scoring）
- [ ] `grep -r "AutoEverything\.ITab_GearManager" Patches/` 应无结果
- [ ] 游戏内 ITab 标签显示"自动万物"
- [ ] 全局重配按钮可用
- [ ] 装备评级 S/A/B/C/D/X 正常显示
