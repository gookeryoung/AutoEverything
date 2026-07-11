# 迭代 16：移除所有自动更换装备功能

## 本轮目标

用户要求："请移除所有自动更换装备所有功能，保留自动评级和自动工作的部分。"

完整移除 AutoEquipment（装备评分系统）与 Allocation（全局分配策略）两个模块的所有代码、
集成点、UI 控件、翻译键、文档，仅保留 AutoTier（自动评级）、AutoWork（自动工作）、
AutoMarkPawn（高价值标记）三个模块。

## 改动文件清单

### 删除的源文件（40 个）

#### Allocation 模块（3 个，整体删除）
- `Source/AutoEverything/Allocation/BeltAllocator.cs`
- `Source/AutoEverything/Allocation/GlobalAllocator.cs`
- `Source/AutoEverything/Allocation/SidearmAllocator.cs`

#### AutoEquipment 模块（37 个，整体删除）
- `CompGearManager.Apparel.cs` / `CompGearManager.Inventory.cs` / `CompGearManager.Weapon.cs`（3 个 partial）
- `GearDefClassifier.cs` / `GearScorer.cs`（2 个门面/分类）
- `Scoring/` 下全部评分管线：`IScorer.cs`、`ScoreBreakdown.cs`、`ScoringPipeline.cs`、`ScoringPipelineFactory.cs`、`GearPolicyEngine.cs`、`GearPreset.cs`、`GearWeights.cs`
- `Scoring/Weapon/` 下 11 个武器 Scorer：`WeaponBiocodedScorer`、`WeaponContextScorer`、`WeaponDpsScorer`、`WeaponDurabilityScorer`、`WeaponForbiddenScorer`、`WeaponIdeologyScorer`、`WeaponQualityScorer`、`WeaponRangeHelper`、`WeaponRangeScorer`、`WeaponSkillScorer`、`WeaponTraitScorer`
- `Scoring/Apparels/` 下 14 个防具 Scorer：`ApparelArmorScorer`、`ApparelContextScorer`、`ApparelCurrentWornScorer`、`ApparelDurabilityScorer`、`ApparelForbiddenScorer`、`ApparelIdeologyScorer`、`ApparelInsulationScorer`、`ApparelLabCoatScorer`、`ApparelMoveSpeedScorer`、`ApparelQualityScorer`、`ApparelRoyaltyScorer`、`ApparelShieldBeltScorer`、`ApparelTaintScorer`、`ApparelTraitScorer`、`ApparelWorkScorer`

#### UI（2 个，整体删除）
- `Source/AutoEverything/UI/Dialog_GlobalReallocate.cs`
- `Source/AutoEverything/UI/PresetDetailsWindow.cs`

#### Core（1 个，整体删除）
- `Source/AutoEverything/Core/DebugMonitor.cs`

### 修改的源文件

- `Source/AutoEverything/AutoEquipment/CompGearManager.cs`：
  精简为薄壳——仅保留 CompTick 调用 AutoExecutor.TryTick()、角色缓存（供 ITab 显示）、
  不适用 Pawn 自移除、死亡 Pawn 字典清理。移除所有装备评估方法与 partial 类引用。
- `Source/AutoEverything/Core/AutoExecutor.cs`：
  移除装备重配触发（ExecuteGear/TriggerGearNow）、SidearmAllocator/BeltAllocator 调用、
  GlobalAllocator 引用。仅保留 ExecuteWork/ExecuteTier/ExecuteMark 三个执行方法。
  移除 Gear 触发器（殖民者数量增加时不再触发装备重配）。
- `Source/AutoEverything/Core/AESettings.cs`：
  移除装备相关设置字段（autoGearEnabled、locked、customTierEntries、tierTagOriginals、
  gearWeights 各项、medicineCount、brawlerCarryMedicine 等），保留 enabled/autoWorkEnabled/
  autoTierTag/autoMarkPawn/debugLogging/defaultSortMode。
  新增战斗价值权重设置（cvSkillWeight/cvPassionNoneMult 等 8 项，供 ITab 显示与调参）。
- `Source/AutoEverything/Core/HarmonyPatches.cs`：
  移除 CompGearManager 装备相关补丁，保留 Pawn_SpawnSetup（注入 Comp）与
  PawnUIOverlay（星标绘制）。
- `Source/AutoEverything/UI/ITab_GearManager.cs`：
  移除装备重配勾选框、全局重配按钮、装备锁控件、预设详情窗口入口。
  底部精简为 3 勾选框（评级/工作/星标）+ 即时触发按钮。
  新增战斗价值权重调整区（8 个浮点数调整控件）。
- `Source/AutoEverything/RoleEvaluation/GearContext.cs`：
  移除装备分配相关情境逻辑，仅保留情境检测用于 ITab 显示。

### 修改的翻译文件

- `Languages/ChineseSimplified/Keyed/AE_Keyed.xml`：244→133 行，移除约 80 个装备翻译键，新增 8 个战斗价值权重键
- `Languages/English/Keyed/AE_Keyed.xml`：243→132 行，同步清理

### 修改的文档

- `README.md`：836→517 行，移除所有装备功能章节（主武器选择/禁止类装备/腰带分配/评分模型/全局重配/护甲分配等），保留评级/工作/标记功能，更新目录结构与评估周期表
- `c:\Users\...\project_memory.md`：移除约 15 条装备相关硬约束，更新医疗守卫入口列表与 VSE 兼容使用点列表

### 归档

- `.trae/skills/iteration-archives-11-15.md`：按 dev-workflow.md 规则归档 iter-11~15 的可复用模式
- 删除 `.trae/docs/iter-11~15` 原文件（已归档）

## 关键决策与依据

### 1. CompGearManager 保留为薄壳

CompGearManager 作为 ThingComp 注入 Pawn，是 AutoExecutor.TryTick() 的全局 Tick 入口，
且 ITab_GearManager 依赖其 CurrentRole 缓存显示角色。删除整个类会丢失这两个功能点，
因此精简为薄壳：仅保留 CompTick→AutoExecutor.TryTick()、角色缓存、自移除兜底、字典清理。

### 2. GearContext 保留用于 ITab 显示

GearContext（情境检测：Combat/Work/Hunting/Cold/Hot/Normal）原用于装备评分。
移除装备评分后，情境信息仍有展示价值（ITab 面板显示当前情境），
因此保留 RoleEvaluation/GearContext.cs，仅移除装备分配相关逻辑。

### 3. AutoExecutor 移除 Gear 触发器

原殖民者数量增加时立即触发 Tier/Gear/Mark。Gear 触发已无意义（装备评估已移除），
故移除。Tier（只改 Nick，不打断 Job）与 Mark（头顶绘制，不打断 Job）保留立即触发。

### 4. 战斗价值权重迁移到 AESettings

原 GearWeights.cs（装备评分权重）已删除，但战斗价值公式（CombatEvaluator）仍在使用
技能权重与兴趣乘数。将这些权重迁移到 AESettings 作为可调字段，并在 ITab 提供调整控件，
保持玩家可调性。

### 5. 医疗守卫入口缩减

CompGearManager 不再有 ForceEvaluate/EvaluateInventory（装备评估入口已移除），
GlobalAllocator/SidearmAllocator/BeltAllocator 整体删除。
医疗守卫入口仅剩 WorkAllocator.ReallocateAll，PawnJobGuard 保留。

## 验证结果

- `make check` 通过（0 警告 0 错误，DLL 已生成）
- Grep 确认无遗漏引用：GearScorer/GearDefClassifier/SidearmAllocator/BeltAllocator/
  GlobalAllocator/ScoringPipeline 等均无匹配
- 翻译文件无残留装备翻译键
- README 目录结构与评估周期表已同步

## 遗留事项

- 游戏内验证待用户测试：确认 ITab 面板布局正常、评级/工作/星标功能正常、
  旧存档加载无报错（旧 ae_ 装备字段由 LookCompat 双读兼容，未迁移字段被 RimWorld 忽略）
