# 迭代 05：性能优化与日志修复（第五轮）

## 本轮目标

消除 Tick 路径的 GC 压力源，修复情境/角色变化日志刷屏问题。

## 改动文件清单

1. `Source/AutoEverything/RoleEvaluation/GearContext.cs` — LogContextIfChanged L148 Log.Message 改 AEDebug.Log
2. `Source/AutoEverything/RoleEvaluation/PawnRole.cs` — DetectRole L188 Log.Message 改 AEDebug.Log
3. `Source/AutoEverything/AutoEquipment/Scoring/ScoreBreakdown.cs` — 新增 Reset 方法
4. `Source/AutoEverything/AutoEquipment/Scoring/ScoringPipeline.cs` — 静态缓存 fastBreakdown 复用实例

## 关键决策与依据

### 1. ContextDetector/RoleDetector 日志改 AEDebug.Log

**问题**：情境变化（Combat↔Work↔Normal）和角色变化时用 `Log.Message` 无条件输出到游戏内消息栏。战斗中情境频繁切换会刷屏。

**修复**：改为 `AEDebug.Log`，受 `debugLogging` 开关控制。情境/角色变化是调试信息，不应在非 debug 模式下刷屏。

**依据**：与 README L699"评估日志走 AEDebug.Log"策略一致。情境/角色变化属于评估日志，不是决策日志。

### 2. ScoreBreakdown 静态缓存（Tick 路径零 GC）

**问题**：`ScoringPipeline.EvaluateCore` 每次 `new ScoreBreakdown(collectItems)`。性能路径（EvaluateFast）每 500 tick 创建数千对象：
- 50 Pawn × 100 候选武器 = 5000 次/500 tick
- 每个对象 ~32 bytes，总计 ~160 KB/500 tick 的 GC 压力

**修复**：
- `ScoreBreakdown` 新增 `Reset()` 方法重置 Total/Vetoed/VetoScore
- `ScoringPipeline<TThing>` 添加静态字段 `fastBreakdown = new ScoreBreakdown(false)`
- `EvaluateCore` 性能路径（collectItems=false）用 `fastBreakdown.Reset()` 复用，调试路径保持 `new`

**安全性分析**：
- RimWorld 单线程，无并发问题
- 调用方（GearScorer.ScoreWeapon/ScoreApparel）读取 breakdown 后立即使用，在下次调用前完成
- debug 路径用 `new ScoreBreakdown(true)`，不影响静态缓存
- 泛型类每个 TThing 独立缓存：`ScoringPipeline<Thing>.fastBreakdown` 与 `ScoringPipeline<Apparel>.fastBreakdown` 互不干扰

**性能提升**：Tick 路径 ScoreBreakdown 创建从 5000 次/500 tick 降至 0 次。

## 验证结果

- `make check` 通过：0 警告 0 错误
- 日志改动不影响玩家可见行为（debug 关闭时不输出，与之前一致）
- ScoreBreakdown 缓存不影响评分结果（Reset 清零后重新累加，等价于 new）

## 遗留事项

1. **ITab_GearManager 60 tick 缓存**：可检查是否有遗漏的重复计算
2. **GearDefClassifier 查询缓存**：IsEmpWeapon/IsShieldBelt 等用字符串/Def 比较，可检查是否有优化空间
3. **CompGearManager.CheckMeleeSidearm**：30 tick 征召路径，IsUnderMeleeAttack 遍历 attackTargetsCache，可检查性能
