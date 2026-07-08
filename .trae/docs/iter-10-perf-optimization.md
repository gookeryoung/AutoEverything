# iter-10: GearContext/PawnRole 情境角色变化日志补加 IsActive 守卫

> 承接 iter-09（WorkAllocator 守卫统一）

## 本轮目标

全局扫描发现 GearContext.cs L150/L158 和 PawnRole.cs L188/L195 的 AEDebug.Log 缺少
`if (AEDebug.IsActive)` 守卫。这两处在 CompTick 路径（ContextDetector.GetContext /
RoleDetector.DetectRole 被 CompGearManager.CompTick 调用），虽仅在情境/角色变化时触发，
但 project_memory 规则要求"Tick 路径所有 AEDebug.Log(() => ...) 调用必须用 if 守卫包裹"。
补加守卫完成日志策略统一的最后一块。

## 改动文件清单

1. `Source/AutoEverything/RoleEvaluation/GearContext.cs`
   - L150 情境变化日志 → 加 `if (AEDebug.IsActive)` 守卫
   - L158 初始情境日志 → 加 `if (AEDebug.IsActive)` 守卫

2. `Source/AutoEverything/RoleEvaluation/PawnRole.cs`
   - L189 角色变化日志 → 加 `if (AEDebug.IsActive)` 守卫
   - L196 初始角色日志 → 加 `if (AEDebug.IsActive)` 守卫

## 关键决策与依据

**为何这两处也要加守卫**：
- GearContext.LogContextIfChanged 被 ContextDetector.GetContext 调用，后者在 CompTick L148
- RoleDetector.DetectRole 在 CurrentRole getter 中调用，后者在 CompTick L149
- 战斗中 Combat↔Work↔Normal 频繁切换时，情境变化日志会多次触发
- 4 处 lambda 都捕获 pawn/prev/newContext/reason 局部变量，会分配闭包
- 加守卫后 debug 关闭时完全跳过闭包分配

**确认无需处理的低频路径**：
- AutoExecutor.cs L183/L210/L286/L316：周期触发（3000-10000 tick），单次 1 个闭包，ROI 极低
- GlobalAllocator.cs silent 分支 AEDebug.Log：事件/周期触发，且有 silent/!silent 双分支结构，加守卫会增加代码复杂度
- GearScorer.cs L41/L83：已有 `if (AEDebug.IsActive && ...)` 守卫（L38/L80），grep 误报
- BeltAllocator/SidearmAllocator：已有 `if (AEDebug.IsActive)` 守卫（单独行），grep 误报

## 验证结果

- `make check` 通过：0 警告 0 错误
- 4 处日志行为不变：debug 开启时输出，关闭时跳过（含闭包分配）
- CompTick 路径 AEDebug.Log 守卫覆盖率：100%（CompGearManager + GearContext + PawnRole）

## 遗留事项

至此 CompTick 路径所有 AEDebug.Log 调用均已加守卫。剩余无守卫的 AEDebug.Log 全在低频路径：
- AutoExecutor（周期触发，3000-10000 tick）
- GlobalAllocator silent 分支（事件/周期触发）
- 这些路径单次调用分配 1 个闭包，GC 影响可忽略，ROI 低于处理成本，不再优化

日志策略优化系列（iter-05~10）至此完成。
