# iter-09: WorkAllocator AEDebug.Log 补加 IsActive 守卫

> 承接 iter-08（CompGearManager 日志统一）

## 本轮目标

WorkAllocator.cs 有 32 处 `AEDebug.Log(() => ...)` 调用缺少 `if (AEDebug.IsActive)` 守卫。
虽然 WorkAllocator 是 10000 tick 周期路径（低频），但 C# lambda 捕获局部变量会分配闭包实例，
即使 AEDebug.Log 内部 L25 有 `if (IsActive)` 检查不调用 factory，闭包对象在调用前已分配。
补加守卫与 CompGearManager/GearScorer/ContextDetector 等模块的日志策略一致。

## 改动文件清单

1. `Source/AutoEverything/AutoWork/WorkAllocator.cs`
   - 32 处 `AEDebug.Log(() => ...)` 补加 `if (AEDebug.IsActive)` 前置守卫
   - 覆盖方法：ReallocateAll、BuildSkillWorkPhases、AssignWorkType、AssignWorkGroup
   - 单行 lambda 与多行 lambda 均已处理

## 关键决策与依据

**为何低频路径也要加守卫**：
- C# lambda 捕获局部变量时，编译器生成闭包类并实例化，在调用 AEDebug.Log 前已分配
- AEDebug.Log 内部 `if (IsActive)` 只避免字符串构造，不避免闭包分配
- WorkAllocator.ReallocateAll 单次调用遍历所有工作类型 × 候选，32 处日志全执行 = 32 个闭包
- 虽然单次 GC 影响小（≈2.5 KB），但 debug 关闭时这些闭包是纯浪费

**替换策略**：
- 用 `replace_all` 将 `AEDebug.Log(() =>` 替换为 `if (AEDebug.IsActive) AEDebug.Log(() =>`
- 匹配 `AEDebug.Log(() =>` 子串不依赖缩进，12 空格与 16 空格（if 块内）缩进均正确处理
- 多行 lambda（`AEDebug.Log(() =>\n{ ... })`）同样匹配，守卫前置正确

## 验证结果

- `make check` 通过：0 警告 0 错误
- 32 处全部已加守卫（grep 确认）
- 日志行为不变：debug 开启时输出到 `[AE]` 日志，关闭时完全跳过（含闭包分配）

## 遗留事项

至此所有模块的 AEDebug.Log 调用策略已统一：
- CompGearManager：评估/换装/纠错日志全有守卫（iter-05~08）
- GearScorer：可疑评分日志有守卫（iter-05）
- ContextDetector/RoleDetector：情境/角色变化日志有守卫（iter-05）
- SidearmAllocator/BeltAllocator：分配日志有守卫（iter-03/04）
- WorkAllocator：全部 32 处有守卫（iter-09）

剩余可选方向（ROI 极低，不再处理）：
- ScoreBreakdown 对象池：已用静态 fastBreakdown 复用，无需进一步池化
- GearWeights 缓存：struct 栈分配，无 GC
- ScoringPipelineFactory：已懒加载静态缓存
