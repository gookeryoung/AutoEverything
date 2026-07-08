# 性能优化模式（归档自 iter-03~07）

> 本文件归档 iter-03~07 性能优化迭代的可复用模式与踩坑总结。
> 过程性记录已删除，关键决策同步至 project_memory.md。

## RimWorld MOD 性能优化原则

### 1. Tick 路径零 GC 原则

Tick 路径（CompTick、DrawPawnGUIOverlay Postfix）禁止：
- `new List<>()` / `new Dictionary<>()` / `new HashSet<>()`（用 static readonly 复用）
- LINQ（Where/Select/OrderBy/ToList 等）
- 字符串插值在调用前构造（用 `Func<string>` 延迟构造）
- `AEDebug.Log(() => ...)` 无守卫调用（lambda 闭包即使不执行也分配）

### 2. tick 缓存模式

高频调用（每帧/每 tick）的计算结果按 tick 缓存：
- **PawnMarker.IsHighValue**：2500 tick 缓存 + 60000 tick 清理（DrawPawnGUIOverlay 每帧调用）
- **CompGearManager.CurrentRole**：2500 tick 缓存（CompTick 每 500 tick）
- **ITab_GearManager.FillTab**：60 tick 缓存（UI 每帧 60 FPS）

缓存结构：`Dictionary<Pawn, CachedValue>` + tick 过期检查 + 定期清理不 Spawned/已死亡的 Pawn。

### 3. 静态实例复用模式

评分管线的 `ScoreBreakdown` 用泛型类静态字段缓存：
```csharp
private static readonly ScoreBreakdown fastBreakdown = new ScoreBreakdown(false);
```
性能路径 `EvaluateFast` 调用 `Reset()` 后填充，调试路径（collectItems=true）保持 `new` 独立实例。
RimWorld 单线程，调用方在下次评分前使用完返回值即可安全复用。

### 4. 日志分层策略

- **评估日志**（评分过程、情境/角色变化）：`if (AEDebug.IsActive) AEDebug.Log(() => ...)`
- **决策日志**（换装结果）：手动触发 `Log.Message`（玩家可见），周期触发 `AEDebug.Log`
- **异常日志**：`Log.ErrorOnce(message, id)` 防重复，id 用 `thingIDNumber ^ salt`

### 5. 预计算缓存模式

排序前预计算评级/价值缓存，避免 Sort 比较器内 O(n log n) 次重复调用：
```csharp
tierCache[p] = CombatEvaluator.GetCombatTier(p);
// Sort 比较器内读 tierCache[a] 而非 GetCombatTier(a)
```

### 6. 逆序遍历 + Remove 模式

`WornApparel` 返回内部列表引用，逆序遍历 + Remove 当前索引元素后，
后续元素前移但逆序已处理过，安全。避免 `new List<Apparel>(WornApparel)` 临时 List。

## 已确认无需优化的项

- **GearWeights**：struct（值类型），栈分配无 GC
- **ScoringPipelineFactory**：已懒加载静态缓存管线实例
- **GearDefClassifier**：已用 `IndexOf(OrdinalIgnoreCase)` 避免 ToUpperInvariant 分配
- **TraitDefCache**：`[StaticConstructorOnStartup]` + `static readonly` 一次性查询
- **DLCCompat**：DLC 状态已缓存为静态 bool 字段
- **WorkAllocator AEDebug.Log 闭包**：非 Tick 路径（事件驱动），ROI 低
- **AEDebug.Label**：`if (!IsActive) return pawn.LabelShort` 守卫，非调试零开销
