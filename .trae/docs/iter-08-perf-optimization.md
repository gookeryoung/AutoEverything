# iter-08: 统一换装/纠错决策日志为 AEDebug.Log

> 承接 iter-07（fallback 装备路径改用 EvaluateFast）

## 本轮目标

CompGearManager 中 EvaluateWeapon L438/L447 已改用 `if (AEDebug.IsActive) AEDebug.Log`，
但其他换装决策与纠错日志仍是 `Log.Message`，50 Pawn 同时换装/旧存档加载集中纠错时刷屏。
统一为 `AEDebug.Log`（受 `debugLogging` 开关控制），保持日志策略一致性。

## 改动文件清单

1. `Source/AutoEverything/AutoEquipment/CompGearManager.cs`
   - L496 `TryFallbackWeapon` 过渡武器日志 → AEDebug.Log
   - L622 `EvaluateApparel` 决策详情日志 → AEDebug.Log
   - L683 `TryFallbackApparel` 过渡防具日志 → AEDebug.Log
   - L714 `RemoveWrongShieldBelt` 卸下护盾腰带日志 → AEDebug.Log
   - L744 `RemoveSlaveCollar` 卸下奴隶项圈日志 → AEDebug.Log

## 关键决策与依据

**为何统一改为 AEDebug.Log**：
- 这 5 处都在 Tick 周期触发的评估路径中（CompTick → EvaluateWeapon/EvaluateApparel）
- `Log.Message` 无条件输出到玩家日志，50 Pawn 同时换装时刷屏淹没真实错误
- 玩家日常游戏不需看到换装/纠错细节，调试时打开 `debugLogging` 即可查看
- 与 EvaluateWeapon L438/L447、EvaluateApparel L526/L530/L630 等已改 AEDebug.Log 的路径一致

**保留 Log.Message 的路径（低频，玩家关心）**：
- L323 `ReloadAllColonists` 手动换装统计：玩家手动触发，反馈扫描/触发数量
- L808/L847 `EvaluateInventory` 药品拾取/丢弃：受 2500 tick 冷却限制，玩家关心药品管理
- L977/L1017 `OnUndraft` 主武器恢复：事件驱动，玩家关心征召结束后的装备状态

**闭包零开销**：
- 所有 AEDebug.Log 都用 `if (AEDebug.IsActive) AEDebug.Log(() => ...)` 守卫
- debug 关闭时 lambda 不构造，避免字符串插值分配

## 验证结果

- `make check` 通过：0 警告 0 错误
- 决策日志行为不变：debug 开启时输出到 `[AE]` 日志，关闭时静默
- 低频日志保留 Log.Message：药品/手动触发/OnUndraft 仍无条件输出

## 遗留事项

iter-08 完成后，CompGearManager 的日志策略已完全统一：
- 评估过程日志（每 tick）：AEDebug.Log + IsActive 守卫（iter-06/07 已完成）
- 换装/纠错决策日志：AEDebug.Log + IsActive 守卫（iter-08 完成）
- 手动触发统计/药品/OnUndraft：Log.Message（低频保留）

至此 Tick 路径的日志策略优化基本完成。剩余可选方向（ROI 低，暂不处理）：
- WorkAllocator AEDebug.Log 闭包优化（非 Tick 高频路径，且已有 IsActive 守卫）
- ScoreBreakdown 对象池（已用静态 fastBreakdown 复用，无需进一步池化）
