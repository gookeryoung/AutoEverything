# iter-07: fallback 装备路径改用 EvaluateFast

## 本轮目标

诊断 iter-06 后发现 `TryFallbackWeapon`/`TryFallbackApparel` 在 Tick 路径用
`ScoreWeaponWithBreakdown`/`ScoreApparelWithBreakdown` 遍历所有候选装备，
每件候选创建新 `ScoreBreakdown` + `List<ScoreItem>`，产生 GC 压力。

## 改动文件清单

1. `Source/AutoEverything/AutoEquipment/CompGearManager.cs`
   - `TryFallbackWeapon`：改用 `pipeline.EvaluateFast` 复用 `fastBreakdown`
   - `TryFallbackApparel`：同样改用 `pipeline.EvaluateFast`
   - 两处都改为直接获取 `ScoringPipelineFactory` pipeline + `GearPolicyEngine.GetWeights()`

## 关键决策与依据

**为何 fallback 路径值得优化**：
- fallback 在 CompTick 路径（每 500 tick），属于 Tick 路径
- 虽然频率低（空手/赤身时才触发），但每次遍历 50-100 件候选
- 每件候选 `new ScoreBreakdown(true)` + `new List<ScoreItem>` = 100 次 GC 分配

**EvaluateFast 复用安全性**：
- `fastBreakdown` 是 `ScoringPipeline<TThing>` 静态字段，RimWorld 单线程
- fallback 循环中每次迭代：`Reset()` → 管线填充 → 读取 `Total`/`Vetoed` → 下次 `Reset()`
- `bd.Total` 在下次迭代前已被读取，安全复用 ✅

**pre-Veto Total 语义保留**：
- `TryFallbackWeapon` 用 `bd.Total`（pre-Veto）选技能最契合的过渡武器（忽略 Veto）
- `TryFallbackApparel` 用 `bd.Vetoed` 排除 + `bd.Total` 比较
- `EvaluateFast` 返回的 `fastBreakdown` 同时提供 `Vetoed`/`Total`，语义一致 ✅

## 验证结果

- `make check` 通过：0 警告 0 错误
- 剩余 `WithBreakdown` 调用全在 `monitorEnabled`/`AEDebug.IsActive` 守卫内（调试路径），无需优化

## 遗留事项

经过 5 轮迭代（iter-03~07），Tick 路径所有已识别的性能问题均已处理：
- 评分路径 GC 压力消除（ScoreBreakdown 静态缓存 + fallback 路径复用）
- 每帧重算评级消除（PawnMarker 2500 tick + ITab 60 tick 缓存）
- 日志刷屏消除（AEDebug.IsActive 守卫 + AEDebug.Log）
- 临时 List 创建消除（逆序遍历 + 静态缓存字段）
