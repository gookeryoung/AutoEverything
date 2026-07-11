# 迭代 13：移除 AutoDrug/AutoFood/AutoSerum 三个模块

## 本轮目标

用户反馈"过多 Ticks 性能下降了"，要求取消自动喂食、自动血清、自动药物三个功能。
完整移除三个模块的所有代码、集成点、UI 控件、翻译键、文档，降低 Tick 性能开销。

## 改动文件清单

### 删除的源文件（13 个）

- `Source/AutoEverything/AutoDrug/DrugAllocator.cs`
- `Source/AutoEverything/AutoDrug/DrugPolicyManager.cs`
- `Source/AutoEverything/AutoDrug/TreatmentAllocator.cs`
- `Source/AutoEverything/AutoDrug/MedicationAllocator.cs`
- `Source/AutoEverything/AutoFood/FoodAllocator.cs`
- `Source/AutoEverything/AutoFood/MealCarrier.cs`
- `Source/AutoEverything/AutoFood/FeedingAllocator.cs`
- `Source/AutoEverything/AutoFood/FoodRestrictionManager.cs`
- `Source/AutoEverything/AutoSerum/SerumAllocator.cs`
- `Source/AutoEverything/AutoSerum/SerumCarrier.cs`
- `Source/AutoEverything/AutoSerum/HealerSerumAllocator.cs`
- `Source/AutoEverything/AutoSerum/BoostSerumAllocator.cs`
- `Defs/DrugPolicyDefs/AE_DrugPolicyDefs.xml`

### 删除的文档

- `.trae/docs/game-testing-guide.md`：完全围绕已删除三模块编写，无保留价值

### 修改的源文件

- `Source/AutoEverything/AutoEquipment/CompGearManager.cs`：
  - 移除 `using AutoDrug/AutoFood/AutoSerum`
  - 移除 CompTick 中三个模块的 AllocateForPawn 调用块
- `Source/AutoEverything/AutoEquipment/CompGearManager.Inventory.cs`：
  - 移除 `AESettings.brawlerCarryMedicine` 引用（AutoDrug 删除连带删除该字段）
  - 简化 targetCount 逻辑（原 Brawler 特殊分支变为死代码，一并移除）
- `Source/AutoEverything/Core/AutoExecutor.cs`：
  - 移除 using、lastXxxTick 字段、XxxErrorSalt 常量
  - 移除首次初始化、colonistCount 增加、周期触发中的三模块调用
  - 移除 TriggerXxxNow 公共方法和 ExecuteXxx 私有方法
- `Source/AutoEverything/Core/AESettings.cs`：
  - 移除 16 个 AutoDrug/AutoFood/AutoSerum 相关字段
  - 移除 ExposeData 中 16 个 LookCompat 调用
  - 移除 DrawSettings 中三个模块的 UI 区块
  - contentHeight 从 820f 改回 540f
- `Source/AutoEverything/UI/ITab_GearManager.cs`：
  - 面板高度从 766f 改回 632f
  - 底部从 7 勾选框 + 2 按钮行 改为 4 勾选框 + 1 按钮行
  - 移除药物/食物/血清三个勾选框 + 即时触发按钮行（3 小按钮）

### 修改的翻译文件

- `Languages/ChineseSimplified/Keyed/AE_Keyed.xml`：移除自动药物/食物/血清的所有翻译键
- `Languages/English/Keyed/AE_Keyed.xml`：同上

### 修改的文档

- `README.md`：
  - 简介移除三模块提及
  - 移除 ITab 面板测试控件章节中的三模块内容
  - 移除三个完整章节（## 自动药物 / ## 自动食物 / ## 自动血清）
  - 目录结构移除 AutoDrug/AutoFood/AutoSerum 三个文件夹
  - 模块职责说明移除三模块条目
  - 未来扩展引用从 DrugAllocator 改为 BeltAllocator
  - 评估周期表格移除三行
  - 文档同步清单移除三模块条目和 AE_DrugPolicyDefs.xml 条目
- `c:\Users\zhou\.trae-cn\memory\projects\...\project_memory.md`：
  - 移除所有 AutoDrug/AutoFood/AutoSerum 相关硬约束
  - 保留 VSE 兼容性条目

## 关键决策与依据

### 1. PawnJobGuard 保留

`Core/PawnJobGuard.cs` 虽由 AutoDrug 模块引入，但 CompGearManager（CompTick/ForceEvaluate/EvaluateInventory）、
GlobalAllocator、WorkAllocator 仍在使用——医疗 Job 守卫逻辑独立于已删除模块，必须保留。

### 2. brawlerCarryMedicine 字段简化

`brawlerCarryMedicine`（重甲前排带药）原本属于 AutoDrug 模块设置，删除 AutoDrug 时一并删除。
CompGearManager.Inventory.cs 的 EvaluateInventory 仍引用该字段，需同步简化逻辑：
- 移除 `|| (role == Role.Brawler && AESettings.brawlerCarryMedicine)` 条件
- 原 Brawler 特殊 targetCount 分支（`role == Role.Brawler && medSkill < 4 ? 1 : ...`）
  变为死代码（Brawler 不再被 shouldCarryMeds 判定），一并移除
- targetCount 直接使用 `AESettings.medicineCount`

### 3. game-testing-guide.md 整体删除

该文档 26 行引用全部围绕已删除的三模块（AutoDrug 3 子功能 + AutoFood 3 子功能 + AutoSerum 3 子功能），
无任何适用于现存模块的内容，整体删除而非部分保留。

### 4. ITab 面板高度回退

原 766f 高度为容纳 7 勾选框 + 2 按钮行（3 即时触发 + 全局重配）专门设计。
移除 3 勾选框 + 1 即时触发按钮行后，回退到 632f（4 勾选框 + 1 全局重配按钮行）。

## 验证结果

- `make check` 通过（0 警告 0 错误，DLL 已生成）
- Grep 搜索源代码确认无遗漏引用：
  - `autoDrugEnabled|autoFoodEnabled|autoSerumEnabled|brawlerCarryMedicine` → 无匹配
  - `DrugAllocator|FoodAllocator|SerumAllocator|MealCarrier|FeedingAllocator` → 无匹配
  - `TriggerDrugNow|TriggerFoodNow|TriggerSerumNow` → 无匹配
- 翻译文件无残留 Keyed 键

## 遗留事项

- 游戏内验证待用户测试：确认 Tick 性能改善，确认 ITab 面板布局正常，确认旧存档加载无报错
  （旧存档中 ae_xxx 字段的双读兼容由 LookCompat 处理，未迁移字段将被 RimWorld 忽略）
