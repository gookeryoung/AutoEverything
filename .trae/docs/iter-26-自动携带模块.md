# iter-26 自动携带模块

## 需求清单

- [x] 新增【自动携带】模块，勾选后殖民者自动携带食物 x3、活力水 x1、清醒丸 x1、思滞血清 x1
- [x] 判定时机：周期 6000 tick 派发 TakeInventory Job；战斗中暂停触发；医疗 Job 守卫
- [x] 适用人群：仅自由殖民者；排除食尸鬼、机械族、奴隶
- [x] 食物优先级：包装食物 → 干粮 → 奢侈餐 → 精致餐 → 一般餐
- [x] 殖民者判定：不需要睡眠不带清醒丸；禁止活力水不带活力水

## 迭代目标

为 AutoEverything 新增第 4 个自动模块 AutoCarry，让殖民者周期性自动从仓库拾取食物与药品到背包，战斗/手术中不打断。

## 改动文件清单

新增：
- `Source/AutoEverything/AutoCarry/CarryAllocator.cs` - 携带分配器主入口
- `Source/AutoEverything/AutoCarry/CarryPolicy.cs` - 携带物品策略（食物分类/药品规则）
- `Source/AutoEverything/AutoCarry/PawnCarryChecker.cs` - 殖民者携带适用性检查
- `Test/AutoEverything.Tests/PawnCarryCheckerTests.cs` - 携带适用性纯逻辑测试（NeedSleep/LuciferiumAllowed）
- `Test/AutoEverything.Tests/CarryPolicyTests.cs` - 携带清单与食物优先级测试

修改：
- `Source/AutoEverything/Core/AESettings.cs` - 新增 autoCarryEnabled 字段
- `Source/AutoEverything/Core/AutoExecutor.cs` - 新增 Carry 周期触发阶段与 TriggerCarryNow
- `Source/AutoEverything/UI/ITab_GearManager.cs` - 底部 3 列改 4 列布局加第 4 勾选框
- `Languages/ChineseSimplified/Keyed/AE_Keyed.xml` - 加 AE_AutoCarry 等翻译
- `Languages/English/Keyed/AE_Keyed.xml` - 加英文翻译
- `README.md` - 同步新增模块章节 + 目录结构 + AutoExecutor 段

## 关键决策与依据

1. **判定时机选择**：用户在三个方案中选"周期派发 HaulJob"——每 6000 tick 扫描，缺物品时派发 JobDefOf.TakeInventory 让殖民者通过原生 HaulJob 系统去仓库拾取，物品从仓库真实消失，行为最自然。

2. **战斗过滤**：复用 AutoExecutor.AnyCombatActive()。战斗中殖民者可能消耗活力水/清醒丸补充战力，但用户选"战斗中暂停触发"避免殖民者离开战位去拿物品导致伤亡。

3. **医疗守卫**：复用 PawnJobGuard.ShouldSkipForMedical。TryTakeOrderedJob 会取消当前 Job，正在手术/治疗的医生与卧床休养的伤员必须跳过。

4. **食物分类**：RimWorld 食物 DefName 集合按用户排序：
   - 包装食物：PackagedSurvivalMeal
   - 干粮：Pemmican、JerkedDriedMeat（CORE 1.4+ 已有）
   - 奢侈：MealLavish
   - 精致：MealFine
   - 一般：MealSimple、MealNutrientPaste

5. **不需要睡眠判定**：`pawn.needs?.rest == null`。RimWorld 中 NeedDef Sleep 缺失（如某些基因/种族）的殖民者不需要睡眠，跳过清醒丸（WakeUp 主要用于抵抗睡眠）。

6. **禁止活力水判定（反射方案）**：RimWorld 1.6 中 `DrugPolicy.entries` 与 `DrugPolicyEntry.allowed` 的可见性在编译 DLL 中受限，直接访问报 CS1061。改为反射获取 `entries` 字段与 `allowed` 字段，绕过编译时可见性检查。反射失败时默认允许（安全降级）。

7. **预约检查**：原计划 `Find.ReservationManager.CanReserve` 在 RimWorld 1.6 中不存在（API 已变更）。改用 `map.reservationManager.CanReserve(pawn, t, 1, -1, null, false)`，ReservationManager 实例位于 Map 上。

8. **目标人群**：用户选"仅自由殖民者"。奴隶不携带（玩家可能用奴隶专做苦力）。食尸鬼不携带（用户明确要求）。机械族已通过 CanManageGear 排除。

9. **物品数量配置**：硬编码在 CarryPolicy 中（食物 3、活力水 1、清醒丸 1、思滞血清 1）。未来如需可配置再扩展，遵循 KISS。

10. **默认关闭**：autoCarryEnabled 默认 false（与 autoWorkEnabled/autoTierTag/autoMarkPawn 默认 true 不同）。携带是侵入性功能（会派发 Job 打断当前任务），玩家主动开启更安全。

## 代码实现情况

### CarryPolicy.cs

- 食物 DefName 列表（按优先级排序）+ 数量 3，FoodDefNames internal 暴露供测试
- 药品配置：Luciferium x1 / WakeUp x1 / Penoxycyline x1
- FillCarryItems(Pawn, List<CarryEntry>) 根据 Pawn 状态填充清单
- FillCarryItemsCore(needSleep, luciferiumAllowed, List) 纯逻辑方法供测试
- 懒加载 ThingDef 缓存避免跨线程 DefDatabase 访问

### PawnCarryChecker.cs

- ShouldSkipForCarry(Pawn)：综合判定是否跳过携带（食尸鬼/机器人/奴隶/医疗/死亡倒下）
- NeedSleep(Pawn) / NeedSleepCore(object) 纯逻辑供测试
- LuciferiumAllowed(Pawn) 通过反射读取 DrugPolicy.entries
- LuciferiumAllowedCore(hasEntry, entryAllowed) 纯逻辑供测试

### CarryAllocator.cs

- ReallocateAll()：遍历自由殖民者，对每个缺物品的殖民者派发 TakeInventory Job
- 单次单 Pawn 单物品：每周期每 Pawn 最多派发一个 Job，避免互相覆盖
- FindNearestAvailableThing：`map.listerThings.ThingsOfDef(def)` + 手动最近搜索，跳过 Spawned=false / Forbidden / 已被他人预约（`map.reservationManager.CanReserve`）
- 派发数量 `Math.Min(缺失量, 目标堆叠数)`，避免一次拿空整个仓库堆
- 错误隔离：单 Pawn 失败 Log.ErrorOnce(salt=0xA400) 不影响其他 Pawn

### AESettings.cs

- 新增 `autoCarryEnabled = false`（默认关闭）
- ExposeData 持久化
- DrawSettings 自动系统段加 AE_AutoCarry 勾选框

### AutoExecutor.cs

- 新增 PhaseState carry + CarryExecuteInterval = 6000
- TryTick 中加 carry 周期触发，复用 AnyCombatActive 过滤
- 新增 TriggerCarryNow() 供 ITab 调用
- 新增 ExecuteCarry(tick, showMessage)
- 错误 salt Carry=0xA400

### ITab_GearManager.cs

- 底部从 3 列改 4 列布局：评级/工作/星标/携带
- 第 4 勾选框：勾选时 TriggerCarryNow，状态变化检测

## 测试验证结果

新增 24 个测试，全套 220 测试通过：

**PawnCarryCheckerTests（8 个）**：
- NeedSleepCore：null → 不需要睡眠；非 null → 需要睡眠
- LuciferiumAllowedCore：4 种组合（无条目默认允许；有条目按 allowed）

**CarryPolicyTests（16 个）**：
- 数量常量：FoodCount=3 / LuciferiumCount=1 / WakeUpCount=1 / PenoxycylineCount=1
- 食物优先级顺序：7 项 DefName 验证 + 数组长度验证 + 8 项顺序验证

`make check` 0 警告 0 错误；`make test` 220/220 passed。

## 整合优化情况

- 反射方案统一：DrugPolicy.entries 与 DrugPolicyEntry.allowed 都用反射，避免可见性差异
- 预约检查复用 Map.reservationManager，与 RimWorld 1.6 API 一致
- FoodDefNames internal 暴露供测试，避免测试硬编码字符串

## 遗留事项

无。后续如玩家反馈需调整间隔/物品清单，可加 Mod 选项滑块。

## 下一轮计划

无（任务收尾）。
