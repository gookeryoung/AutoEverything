# 迭代 14：移除"丢弃多余药品"逻辑

## 本轮目标

修复用户报告的 bug：殖民者隔段时间丢弃多余的药。
处理方式：删除 EvaluateInventory 中的"丢弃多余药品"功能块。

## 根因分析

`CompGearManager.Inventory.cs` 的 `EvaluateInventory` 方法包含三个分支：
1. `medsInInventory > targetCount` → 丢弃多余药品
2. `medsInInventory >= targetCount` → 已满，return
3. `medsInInventory < targetCount` → 拾取药品

分支1（丢弃逻辑）的设计初衷是处理"搬运时混入药品"的情况，
但实际表现为殖民者每隔一段时间就把库存中超过 `medicineCount` 的药品丢到地上，
玩家看到的是"殖民者反复丢弃药品"的干扰行为。

## 改动文件清单

- `Source/AutoEverything/AutoEquipment/CompGearManager.Inventory.cs`：
  - 删除 `if (medsInInventory > targetCount) { ... return; }` 整个丢弃逻辑块（原第48-67行）
  - 注释从"统计会导致反复拾取/丢弃死循环"简化为"统计会导致反复拾取死循环"
  - 保留拾取逻辑不变：医生/有医疗技能的战斗员仍会拾取药品到 `targetCount`

## 关键决策与依据

### 1. 只删除丢弃逻辑，保留拾取逻辑

用户说的"该功能"指代 bug 对应的"丢弃多余药品"行为，非整个携带药品功能。
最小改动原则：删除直接导致 bug 的代码，保留正常工作的拾取逻辑。

### 2. 删除后行为

- `medsInInventory > targetCount` 时落入 `>= targetCount` 判断，直接 return
- 不丢弃、不拾取，库存保持现状
- 搬运时混入的药品会留在库存（可接受，优于反复丢弃的干扰）

### 3. 不需要同步 README

README 未描述"丢弃多余药品"行为，Grep 搜索"丢弃|多余药"无相关章节，无需同步。

## 验证结果

- `make check` 通过（0 警告 0 错误）

## 遗留事项

- 游戏内验证待用户测试：确认殖民者不再反复丢弃药品
