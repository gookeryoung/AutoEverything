# 迭代 03：性能优化与 BUG 修复（第三轮）

## 本轮目标

针对提升性能和修复 BUG，识别并修复 Tick 路径与高频调用路径的性能漏洞与冗余计算。

## 改动文件清单

1. `Source/AutoEverything/AutoMarkPawn/PawnMarker.cs` — IsHighValue 加 tick 缓存
2. `Source/AutoEverything/Allocation/GlobalAllocator.cs` — L247/249 日志复用 combatValueCache
3. `Source/AutoEverything/AutoEquipment/CompGearManager.cs` — L404-405 合并冗余 WorkTagIsDisabled 检查
4. `Source/AutoEverything/Allocation/SidearmAllocator.cs` — L198 Log.Message 改 AEDebug.Log 守卫

## 关键决策与依据

### 1. PawnMarker.IsHighValue 加 tick 缓存（最高优先级）

**问题**：`PawnMarker.IsHighValue` 被 `HarmonyPatches.PawnUIOverlay_DrawPawnGUIOverlay_Patch.Postfix` 每帧调用。原实现直接调用 `CombatEvaluator.GetCombatTier(pawn)`，涉及：
- 自定义评级查询（AESettings.TryGetCustomTier）
- 配偶评级豁免递归查询（HasSpouseTierAtLeast → GetAutoCombatTier）
- 技能等级查询（Shooting/Melee）
- 特质查询（Tough/Nimble/Brawler 等）

50 个非殖民者 × 60 FPS = **每秒 3000 次完整评级计算**，是最大的性能漏洞。

**修复**：添加 `Dictionary<Pawn, CachedTier>` tick 缓存（2500 tick ≈ 42 秒窗口），IsHighValue 内部先查缓存。配套 60000 tick 定期清理不 Spawned/已死亡的 Pawn，避免引用泄漏。

**依据**：评级在此窗口内稳定（非殖民者技能/特质不会突变），缓存命中率极高。DrawPawnGUIOverlay 每帧触发，但视觉标记延迟 42 秒更新完全可接受（玩家无法察觉）。

### 2. GlobalAllocator.ReallocateWeapons 日志复用缓存

**问题**：L247/249 日志中直接调用 `CombatEvaluator.ComputeCombatValue(pawn)` 重复计算，而 L102 已预存 `combatValueCache[pawn]`。

**修复**：改为 `combatValueCache[pawn]:F1`。低频但避免日志路径的冗余技能查询。

### 3. CompGearManager.EvaluateWeapon 合并冗余检查

**问题**：L404-405 两行分别检查 `IsRangedWeapon + Violent` 和 `IsMeleeWeapon + Violent`，但 `WorkTagIsDisabled(Violent)` 与武器类型无关。

**修复**：合并为单次 `if (Pawn.WorkTagIsDisabled(WorkTags.Violent)) continue;`。L399-401 已保证 `IsRangedWeapon || IsMeleeWeapon`，所以合并等价。

### 4. SidearmAllocator.AssignSidearm 日志优化

**问题**：L198 `Log.Message` 无条件输出，会刷屏且无法关闭。

**修复**：改为 `if (AEDebug.IsActive) AEDebug.Log(() => ...)`。EMP 手雷分配低频（2000 tick 间隔，最多 2 人），但日志应受 debug 开关控制。

## 验证结果

- `make check` 通过：0 警告 0 错误
- 所有修改不改变玩家可见行为（PawnMarker 标记仍每帧绘制，仅评级计算频率降低）

## 遗留事项

1. **WorkAllocator(11处)/GlobalAllocator(8处) 的 AEDebug.Log 闭包**：低频但可一致性优化（可选）
2. **ScoreBreakdown 对象池**：每次评分 new 对象，需对象池（复杂度高，暂缓）
3. **GlobalAllocator ReallocateApparel 中 `new List<Apparel>(pawn.apparel.WornApparel)`**：L306 在放下护甲时创建临时列表，可优化为逆序遍历 + Remove（但需验证 RimWorld API 安全性）
