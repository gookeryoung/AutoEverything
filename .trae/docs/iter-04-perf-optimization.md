# 迭代 04：性能优化与 BUG 修复（第四轮）

## 本轮目标

延续第三轮性能优化，修复 BeltAllocator 与 SidearmAllocator 相同模式的日志问题，消除 GlobalAllocator 临时 List 创建。

## 改动文件清单

1. `Source/AutoEverything/Allocation/BeltAllocator.cs` — L220 Log.Message 改 AEDebug.Log 守卫
2. `Source/AutoEverything/Allocation/GlobalAllocator.cs` — L306 放下护甲避免临时 List 创建

## 关键决策与依据

### 1. BeltAllocator.AssignBelt 日志优化（与 SidearmAllocator 一致）

**问题**：L220 `Log.Message` 无条件输出 + 重复调用 `CombatEvaluator.GetAutoCombatTier(pawn)`，与上轮 SidearmAllocator L198 完全相同的模式。

**修复**：改为 `if (AEDebug.IsActive) AEDebug.Log(() => ...)`。腰带分配是周期触发（3000 tick），不应无条件刷屏。

**依据**：与上轮 SidearmAllocator 修复保持一致，README 已在 iter-03 更新日志策略说明。

### 2. GlobalAllocator.ReallocateApparel 避免临时 List 创建

**问题**：L306 `new List<Apparel>(pawn.apparel.WornApparel)` 在放下护甲时创建临时列表副本，每个殖民者创建一个临时 List。50 个殖民者 = 50 个临时 List 分配。

**修复**：改为逆序遍历 `WornApparel` + `Remove`。`WornApparel` 返回内部列表引用，逆序遍历 + Remove 当前索引元素后，后续元素前移，但逆序遍历已处理过这些索引，安全。

**依据**：`WornApparel` 属性返回 `Pawn_ApparelTracker` 内部 `List<Apparel>` 的引用（非副本），RimWorld 原生代码也用此模式遍历。逆序遍历是消除"遍历中修改列表"问题的标准做法。

### 跳过的优化项

- **WorkAllocator AEDebug.Log 闭包一致性**：WorkAllocator 非Tick路径（事件驱动+手动触发），每次调用仅分配约 600 bytes 闭包，ROI 低。project_memory 规则限定"Tick 路径"才需守卫，保持现状。
- **ScoreBreakdown 对象池**：复杂度高（需改 IScorer 接口或 ScoreBreakdown 设计），ROI 不确定，暂缓。

## 验证结果

- `make check` 通过：0 警告 0 错误
- BeltAllocator 日志改动不改变玩家可见行为（与 SidearmAllocator 上轮一致）
- GlobalAllocator 逆序遍历不改变行为（等价于复制副本遍历）

## 遗留事项

1. **ScoreBreakdown 对象池**：每次评分 `new ScoreBreakdown()`，50 Pawn × 100 武器 = 5000 次/500 tick。复杂度高，需评估是否值得改 IScorer 接口。
2. **ITab_GearManager 60 tick 缓存**：已有缓存，但可检查是否有遗漏的重复计算。
3. **GearContext.cs 战斗 Job 判断**：上轮修复的 alwaysShowWeapon 判断，可检查是否有遗漏的战斗 Job 类型。
