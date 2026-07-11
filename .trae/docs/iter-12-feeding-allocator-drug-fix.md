# 迭代 12：修复 FeedingAllocator 误把 Go-juice 当作食物源

## 本轮目标

修复用户报告的 bug：殖民者反复拾取活力水（Go-juice）和丢弃，导致不做专业工作。

## 根因分析

FeedingAllocator 的 `FoodSourceNotPlantOrTree` 搜索谓词允许 Go-juice 被当作食物源：
- `ThingRequestGroup.FoodSourceNotPlantOrTree` 组不排除 `NeverForNutrition` 的物品（Go-juice 的 `preferability = NeverForNutrition`，继承自 DrugBase）
- `FoodUtility.WillEat(patient, t, doctor, false, false)` 用 `careIfNotAcceptable=false`，不拒绝 `NeverForNutrition` 物品
- 导致医生反复拾取 Go-juice 喂给伤员，Go-juice 不提供营养（`NeverForNutrition`），伤员始终饥饿，形成循环

排除的方向：
- EvaluateInventory 只处理 `IsMedicine`，Go-juice 的 `thingCategories=[Drugs]`，`IsMedicine=false`，不处理
- BoostSerumAllocator/MedicationAllocator/SerumCarrier/MealCarrier 都用 `ThingRequest.ForDef` 精确匹配，不会误拾取
- DrugPolicyManager 把战斗员 DrugPolicy 改为 AE_Combat（空 entries），不会导致 RimWorld 丢弃库存 Go-juice

## 改动文件清单

1. `Source/AutoEverything/AutoFood/FeedingAllocator.cs`：地图食物搜索谓词添加 `t.def.ingestible.preferability != FoodPreferability.NeverForNutrition` 检查，排除药物/成瘾品
2. `README.md`：喂食 Job 结构说明补充排除 `NeverForNutrition` 的说明

## 关键决策与依据

- **在谓词中显式排除 `NeverForNutrition`**，而非把 `WillEat` 的 `careIfNotAcceptable` 改为 `true`：后者可能影响其他低质量食物（如 Raw）的选择，前者更精准
- **Go-juice 的 `IsMedicine = false`**（`thingCategories = [Drugs]`），AE 的 EvaluateInventory 不处理 Go-juice

## 验证结果

- `make check` 通过（0 警告 0 错误）
- git commit 845e83b 已 push 到 gk/main

## 遗留事项

- 游戏内验证待用户测试（确认 Go-juice 不再被当作食物源，医生不再反复拾取喂食）
