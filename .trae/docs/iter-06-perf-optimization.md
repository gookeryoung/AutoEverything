# iter-06: ITab_GearManager 评级识别码缓存

## 本轮目标

诊断 iter-05 之后的遗留优化点，发现 ITab_GearManager FillTab 中 L259-261 的
`GetPawnLookupName`/`GetSystemTier`/`TryGetCustomTier` 每帧调用未纳入 60 tick 缓存。
`GetSystemTier` 涉及 9 技能查询 + 配偶递归 `GetAutoCombatTier(spouse)`，每帧重算浪费。

## 改动文件清单

1. `Source/AutoEverything/UI/ITab_GearManager.cs`
   - 新增 4 个静态缓存字段：`cachedPawnLookupName`/`cachedAutoTier`/`cachedHasCustom`/`cachedCustomTier`
   - 在 60 tick 缓存块内计算 `GetPawnLookupName`/`GetSystemTier`/`TryGetCustomTier`
   - 评级识别码展示区域改用缓存变量，删除每帧调用

## 关键决策与依据

**为何纳入缓存而非每帧计算**：
- `GetSystemTier` 调用 `GetAutoCombatTier`（遍历 9 技能 + 多次特质查询）
- `HasSpouseTierAtLeast` 递归调用 `GetAutoCombatTier(spouse)`（配偶再算一次）
- UI 路径 60 FPS，选中 Pawn 时每帧重算 = 60 次/秒评级计算
- 评级不会突变（技能/特质变化极少），60 tick 缓存安全

**`cachedCustomTier` 设计**：
- `TryGetCustomTier(pawnName, out cachedCustomTier)` 的 out 参数写入静态字段
- 调用方通过 `cachedCustomTier` 读取（L274 tierCode 拼接）
- 未命中自定义档时 `cachedHasCustom=false`，`cachedCustomTier` 为默认值 0（X 档），不影响显示

## 验证结果

- `make check` 通过：0 警告 0 错误
- 评级识别码显示逻辑不变：`autoTier#pawnName` 或 `autoTier(customTier)#pawnName`
- 设置/清除自定义档按钮仍通过 `pawnName`（缓存变量）操作，正确引用

## 遗留事项

本轮完成后，已识别的主要性能优化点均已处理：
- iter-03: PawnMarker 缓存 + GlobalAllocator 日志 + WorkTagIsDisabled 合并 + SidearmAllocator 日志
- iter-04: BeltAllocator 日志 + GlobalAllocator 逆序遍历
- iter-05: ContextDetector/RoleDetector 日志 + ScoreBreakdown 静态缓存
- iter-06: ITab_GearManager 评级识别码缓存

剩余可选方向（ROI 低，暂不处理）：
- GearWeights 是 struct（栈分配，无 GC），无需缓存
- ScoringPipelineFactory 已懒加载静态缓存，无需优化
- WorkAllocator AEDebug.Log 闭包（非 Tick 路径，ROI 低）
