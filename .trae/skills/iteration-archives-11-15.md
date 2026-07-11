# 迭代归档 11-15：可复用模式与设计决策

> 来源：iter-11~15 过程性记录，已归档。保留长期可复用的模式、踩坑总结、设计决策。

## 1. 功能模块完整移除模式（iter-13/14/16）

当需要完整移除一个功能模块时，按以下顺序操作，避免遗漏：

### 步骤清单

1. **删除源文件**：模块目录下所有 .cs 文件 + 相关 XML Def
2. **修改集成点**：
   - CompGearManager.CompTick：移除模块的 AllocateForPawn 调用块
   - AutoExecutor：移除 using、lastXxxTick 字段、XxxErrorSalt 常量、ExecuteXxx/TriggerXxxNow 方法、首次初始化与周期触发中的调用
3. **修改设置**（AESettings.cs）：
   - 移除模块相关字段
   - 移除 ExposeData 中对应 LookCompat 调用
   - 移除 DrawSettings 中 UI 区块
   - 调整 contentHeight 适应新布局
4. **修改 UI**（ITab_GearManager.cs）：
   - 调整面板高度（size = new Vector2(360f, Xf)）
   - 调整底部区计算公式（checkboxHeight * N + buttonGap * M）
   - 移除对应勾选框/按钮
5. **清理翻译**（中英文 AE_Keyed.xml）：用 Grep 确认代码不再引用后删除死键
6. **更新文档**（README.md）：移除功能章节、目录结构、模块职责、评估周期表格、文档同步清单
7. **更新 project_memory.md**：移除相关硬约束条目
8. **验证**：`make check` + Grep 确认无残留引用

### 踩坑总结

- **brawlerCarryMedicine 连带删除**：删除 AutoDrug 模块时，`CompGearManager.Inventory.cs` 仍引用 `AESettings.brawlerCarryMedicine`，需同步简化逻辑。删除模块前用 Grep 搜索字段名确认所有引用点。
- **PawnJobGuard 保留判断**：删除模块时检查其引入的工具类是否仍被其他模块使用。PawnJobGuard 由 AutoDrug 引入，但 CompGearManager/GlobalAllocator/WorkAllocator 仍在用，必须保留。
- **game-testing-guide.md 整体删除**：若文档完全围绕已删除模块编写，无适用于现存模块的内容，整体删除而非部分保留。
- **面板高度回退**：移除勾选框/按钮后需同步调整面板高度，避免空白区域。

## 2. VSE 兼容模式（iter-11）

兼容第三方 MOD 扩展的枚举类型时，采用"反射检测 + O(1) 查询 + 降级策略"模式：

### 实现要点

1. **启动时反射检测**：在 `[StaticConstructorOnStartup]` 或静态构造中遍历 AppDomain 查找目标类型，构建映射表
2. **运行时 O(1) 查询**：映射表存静态字段，运行时直接查表，无反射开销
3. **降级策略**：反射失败时降级为原版行为，不阻断主功能
4. **枚举值冲突规避**：若第三方扩展的枚举值与原版语义重合（如 VSE_Natural 等同双火），不设重复枚举值（C# CS0152 错误），改为在映射时归并

### VSE 具体实现

- `PassionTier` 枚举：Apathy=-1, None=0, Minor=1, Major=2, Critical=3（无 Natural，避免 CS0152）
- VSE_Natural 映射为 Major（语义等同双火）
- VSE_Apathy 视为"无火及以下"（乘数 = 无火 × 0.5，不加分）
- VSE_Critical 视为"高于双火"（乘数 = 双火 × 1.5）
- Major 计数用 `tier >= Major`（含 Natural/Critical），Minor 计数用 `tier == Minor`（严格匹配避免双计数）

## 3. 医疗守卫一致性（iter-15）

所有全局重配入口必须统一调用 `PawnJobGuard.ShouldSkipForMedical(pawn)` 跳过医疗中/休养中的 Pawn。

### 守卫位置

守卫放在候选收集阶段（CollectCandidatePawns / ReallocateAll 入口），而非分配阶段。原因：AllocateAllColonists 是同步执行的（同一 Tick 内完成收集+分配），在候选收集时过滤即可确保候选池不含医疗中的 Pawn。

### 已覆盖入口

- CompGearManager.CompTick / ForceEvaluate / EvaluateInventory
- GlobalAllocator.ReallocateAll
- WorkAllocator.ReallocateAll
- SidearmAllocator.CollectCandidatePawns
- BeltAllocator.CollectCandidatePawns

### 设计依据

`SetPriority`/`TryTakeOrderedJob`/`apparel.Remove`/`TryDropEquipment` 都会取消当前 Job，导致：
- 手术死循环（TendPatient/DoBill Bill_Medical 被取消后重新排队）
- 患者死亡（LayDown 休养被中断，免疫力/治疗进度丢失）

## 4. 食物搜索谓词陷阱（iter-12）

`ThingRequestGroup.FoodSourceNotPlantOrTree` 组不排除 `NeverForNutrition` 的物品（如 Go-juice）。`FoodUtility.WillEat` 用 `careIfNotAcceptable=false` 时也不拒绝 `NeverForNutrition` 物品。

### 解决方案

在地图食物搜索谓词中显式添加 `t.def.ingestible.preferability != FoodPreferability.NeverForNutrition` 检查，而非把 `WillEat` 的 `careIfNotAcceptable` 改为 `true`（后者可能影响其他低质量食物选择）。

## 5. 丢弃逻辑设计陷阱（iter-14）

"丢弃多余物品"逻辑容易导致反复拾取/丢弃循环。设计携带数量限制时：
- **保留拾取逻辑**：达到上限时停止拾取，状态保持
- **避免主动丢弃**：超过上限时不主动丢弃，让物品留在库存（可接受，优于反复丢弃的干扰）
- 例外：若物品会持续累加导致问题，才考虑丢弃，但需配合冷却防循环
