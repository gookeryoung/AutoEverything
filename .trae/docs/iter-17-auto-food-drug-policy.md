# iter-17: 新增自动食物方案与自动用药方案功能

## 本轮目标

响应用户请求：
1. 分析确认 Tick 性能无隐患（前序已完成）
2. 新增自动食物方案功能：按人员数量/文化（信仰）变动事件驱动配置 AE_常规/AE_人肉/AE_虫肉方案，人肉虫肉是"允许"而非"仅含"
3. 新增自动用药方案功能：参考食物方案，按特质/信仰配置

## 改动文件清单

### 新增文件
- `Source/AutoEverything/AutoFood/FoodPolicyAllocator.cs` — 食物方案自动分配器
- `Source/AutoEverything/AutoDrug/DrugPolicyAllocator.cs` — 用药方案自动分配器

### 修改文件
- `Source/AutoEverything/Core/AutoExecutor.cs` — 集成食物/用药方案的事件驱动触发 + 信仰变化检测
- `Source/AutoEverything/Core/AESettings.cs` — 新增 autoFoodPolicyEnabled/autoDrugPolicyEnabled 字段 + ExposeData + DrawSettings 勾选框
- `Source/AutoEverything/UI/ITab_GearManager.cs` — 底部从 3 勾选框扩展为 5 勾选框（+食物+用药），面板高度 420→484
- `Languages/ChineseSimplified/Keyed/AE_Keyed.xml` — 新增 AE_AutoFoodPolicy/AE_AutoDrugPolicy/AE_FoodPolicyResult/AE_DrugPolicyResult + tooltip
- `Languages/English/Keyed/AE_Keyed.xml` — 同步英文翻译
- `README.md` — 功能概览表 + AutoExecutor 章节 + AutoFood/AutoDrug 专门章节 + 目录结构 + 评估周期表
- `project_memory.md` — 新增 AutoFood/AutoDrug/AutoExecutor ideo 检测硬约束

## 关键决策与依据

1. **事件驱动而非 Tick 轮询**：食物/用药方案仅在殖民者数量变化或信仰变化（被传教成功）时标记 pendingFoodDrugRealloc，冷却 2500 tick 后执行。无周期 Tick 触发，降低性能开销。

2. **信仰变化检测用 Ideo.id（int）**：Ideo 类没有 def 属性，有 public int id 字段。用 Dictionary<int, int>（pawnId → ideoId）做快照比对，60 tick 轮询，比字符串比较更高效。

3. **食物/用药方案无需战斗过滤**：修改 pawn.foodRestriction.CurrentFoodPolicy / pawn.drugs.CurrentPolicy 不会取消当前 Job（与 SetPriority 不同），因此食物/用药方案重配只受冷却限制，不受 AnyCombatActive 限制。

4. **DrugPolicy.InitializeIfNeeded 是 private**：反射确认后改用 DrugPolicyDatabase.NewDrugPolicyFromDef(def) 创建从模板初始化的 DrugPolicy。已存在的方案不重新初始化，尊重玩家手动修改。

5. **人肉/虫肉是"允许"而非"仅含"**：在 AE_常规 基础上 SetAllow(filter, true)，殖民者仍可吃普通食物，只是不再排斥人肉/虫肉。

6. **食人族特质查询**：Cannibal 不在 TraitDefOf 中，用 DefDatabase<TraitDef>.GetNamed("Cannibal", false) 查询，缺失时 WarningOnce 并仅依赖信仰信条。

7. **三种方案共享基础禁止（iter-17 补充）**：AE_常规/AE_人肉/AE_虫肉 均禁止生食（FoodRaw ThingCategoryDef）、尸体（Corpses ThingCategoryDef）、动物饲料（Kibble ThingDef），仅允许熟食等正常食物。人肉/虫肉通过 AllowCannibal/AllowInsectMeat 特殊过滤器覆盖 FoodRaw 类别禁止——特殊过滤器优先于类别过滤。

8. **精神茶定时使用（iter-17 补充）**：AE_常规 方案在 SocialDrugs 模板基础上，通过反射访问 DrugPolicy.entriesInt 私有字段追加 PsychiteTea 条目（allowScheduled=true, daysFrequency=2, takeToInventory=1）。每次 ReallocateAll 确保精神茶条目存在（缺失则追加，已有则不修改参数，尊重玩家手动修改）。

## 验证结果

- `make check` 通过：0 警告 0 错误
- 待游戏内验证：食物/用药方案创建、信仰变化触发、特质检测

## 遗留事项

- 游戏内验证 ITab 5 勾选框布局（面板高度 484f）
- 游戏内验证信仰变化检测（被传教后自动重配方案）
- 游戏内验证无意识形态 DLC 环境下仅按特质判定
