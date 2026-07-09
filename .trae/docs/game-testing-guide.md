# AutoEverything 游戏内测试指南

> 本文档指导如何在 RimWorld 游戏内实测 P4 新增的三个自动模块（AutoDrug/AutoFood/AutoSerum）。
> 所有子功能均可在 ITab 面板底部即时触发，绕过 3000 tick 周期便于反复测试。

## 前置准备

1. **启用 MOD**：在 RimWorld 启动器勾选 AutoEverything，确保依赖（如 Anomaly DLC 测试增益血清）已启用
2. **加载存档**：建议用一个有 5+ 殖民者、含医生角色、地图上有药品/食物/血清的存档
3. **开启调试日志**：MOD 选项 → 勾选"调试日志"，便于在 Player.log 观察详细日志（前缀 `[AE]`）
4. **准备测试物品**：
   - 药品：penoxycyline（防疟疾药）、medicine（医疗用品）
   - 食物：MealSurvivalPack（行军口粮）、各类 Meal
   - 血清：MechSerumHealer（治愈血清，Core）、MetalbloodSerum/JuggernautSerum/MindNumbSerum（增益血清，Anomaly DLC）

## 通用测试方法

### ITab 测试控件（推荐）

选中任一殖民者 → 打开"自动万物"ITab 面板（右下角标签页）→ 面板底部可见：

| 控件 | 作用 |
|------|------|
| 7 个勾选框 | 评级/工作/装备/星标/药物/食物/血清 总开关 |
| 即时触发按钮行（3 小按钮）| 立即药物 / 立即食物 / 立即血清 |
| 全局装备重配按钮 | 占满宽度的全局重配 |

**测试流程**：
1. 勾选对应模块总开关（如"自动药物"）→ 勾选时立即触发一次
2. 点击即时按钮（如"立即药物"）→ 绕过 3000 tick 周期，立即执行一次
3. 反复点击即时按钮 → 可多次触发，便于观察 Messages 消息反馈与 Pawn 行为变化
4. 取消勾选 → 仅停止周期自动，保留当前配置

### MOD 选项子开关（精细控制）

MOD 选项 → 自动万物 → 展开各模块，可单独开关 9 个子功能：

- **自动药物**：药品政策 / 自动治疗 / 预防服药
- **自动食物**：携带口粮 / 喂食伤员 / 食物限制
- **自动血清**：携带血清 / 治愈注射 / 增益注射

子开关默认全开，测试时按需关闭其他子功能以隔离观察单个子功能行为。

---

## AutoDrug 测试（3 个子功能）

### 子功能1：DrugPolicyManager（药品政策）

**触发条件**：Pawn 角色为 Doctor 或战斗员（Brawler/Shooter/Hunter/Leader）

**测试步骤**：
1. 确保有 1 个医生角色殖民者（Medicine 技能 ≥8）和 1 个战斗员
2. ITab 勾选"自动药物" → 点击"立即药物"
3. 选中医生 → 检视面板"药品政策"应显示 `AE_Doctor`（含 penoxycyline）
4. 选中战斗员 → 检视面板"药品政策"应显示 `AE_Combat`（空政策，禁成瘾品）
5. 选中普通角色（Worker/Default）→ 政策应保持玩家手动设置不变

**预期行为**：
- 医生自动配置带 penoxycyline 的政策（防疟疾/瘟疫/嗜睡病）
- 战斗员禁成瘾品（空 entries 政策）
- 普通角色保留玩家手动设置

### 子功能2：TreatmentAllocator（自动治疗）

**触发条件**：地图存在 `HealthAIUtility.ShouldBeTendedNowByPlayer` 判定的伤员 + 空闲医生

**测试步骤**：
1. 用开发模式（`GodMode`）伤害一个殖民者使其需要治疗（或等野生动物攻击）
2. 确保有 1 个空闲医生（非征召、非医疗 Job 中）
3. ITab 勾选"自动药物" → 点击"立即药物"
4. 观察医生是否自动走向伤员执行 TendPatient Job
5. 检视医生 → 应显示"治疗"Job

**预期行为**：
- 扫描伤员，按 medSkill 降序找空闲医生
- 安排 `JobDefOf.TendPatient` Job
- 医生正在执行 TendPatient/DoBill/Rescue 时不安排新 Job（医疗守卫）
- 伤员正在床上休养（IsRecoveringInBed）时不强制安排（由原生 work giver 处理）

### 子功能3：MedicationAllocator（预防服药）

**触发条件**：Pawn 无 `PenoxycylineHigh` hediff（药效已过）

**测试步骤**：
1. 选中一个殖民者，用开发模式清除其 PenoxycylineHigh hediff（或等 5.5 天药效过）
2. 确保地图上有 penoxycyline（药品架或地上）
3. ITab 勾选"自动药物" → 点击"立即药物"
4. 观察 Pawn 是否自动走向 penoxycyline 并服用
5. 检视 Pawn → 健康面板应显示 PenoxycylineHigh hediff

**预期行为**：
- 检测 Pawn 无 PenoxycylineHigh → 安排 Ingest Job
- 优先库存中的 penoxycyline，无则找地图上最近的
- 2500 tick 拾取冷却避免反复拾取

---

## AutoFood 测试（3 个子功能）

### 子功能1：MealCarrier（行军口粮）

**触发条件**：Pawn 处于征召状态（`pawn.Drafted == true`）+ 库存无行军口粮

**测试步骤**：
1. 征召一个殖民者
2. 确保地图上有 MealSurvivalPack（行军口粮）
3. ITab 勾选"自动食物" → 点击"立即食物"
4. 观察征召中的 Pawn 是否自动拾取行军口粮到库存
5. 检视 Pawn → 库存应有 1 份行军口粮（`carryMealCount` 默认 1）

**预期行为**：
- 仅征召中的 Pawn 拾取（远征/战斗应急）
- 拾取数量由 `AESettings.carryMealCount` 控制（默认 1 份）
- 2500 tick 拾取冷却避免反复拾取
- 医疗 Job 中的 Pawn 跳过（医疗守卫）

### 子功能2：FeedingAllocator（喂食伤员）

**触发条件**：地图存在卧床且饥饿（`CurLevelPercentage < 0.3`）的伤员 + 空闲医生

**测试步骤**：
1. 用开发模式让一个殖民者受伤并卧床（或等野生动物攻击致 Downed）
2. 等该伤员饥饿度降到 30% 以下（或用开发模式调整 Food 需求）
3. 确保有 1 个空闲医生 + 地图上有可食用食物
4. ITab 勾选"自动食物" → 点击"立即食物"
5. 观察医生是否自动取食物走向伤员执行 FeedPatient Job

**预期行为**：
- 扫描卧床饥饿伤员（CurLevelPercentage < 0.3）
- 找空闲医生（非征召、非医疗 Job 中、非床上休养）
- 安排 `JobDefOf.FeedPatient` Job（targetA=食物, targetB=伤员）
- 食物来源：优先医生库存，其次地图上最近可食用食物
- 伤员不应用医疗守卫（伤员本身 IsRecoveringInBed=true 会让守卫永真，无法喂食）

### 子功能3：FoodRestrictionManager（食物限制）

**触发条件**：Pawn 角色为 Doctor 或卧床饥饿伤员

**测试步骤**：
1. 选中医生角色殖民者
2. ITab 勾选"自动食物" → 点击"立即食物"
3. 检视医生 → "食物限制"应显示 `AE_Patient` 或 `Lavish`（高营养政策）
4. 选中普通角色 → 食物限制应保持玩家手动设置不变

**预期行为**：
- Doctor/卧床伤员 → 切到高营养食物政策（AE_Patient 优先，否则 Lavish）
- 普通角色保留玩家手动设置
- 医生正在执行医疗 Job 时不修改政策（避免 Job 重评估打断治疗）

---

## AutoSerum 测试（3 个子功能）

### 子功能1：SerumCarrier（血清携带）

**触发条件**：Pawn 角色为 Doctor 或高评级战斗员（CombatTier ≥ S）

**测试步骤**：
1. 确保有医生角色或 S+ 评级战斗员
2. 在地图上放置 MechSerumHealer（治愈血清，Core 物品）
3. ITab 勾选"自动血清" → 点击"立即血清"
4. 检视医生/S+ 战斗员 → 库存应有 1 件 MechSerumHealer

**预期行为**：
- 医生/高评级战斗员库存携带 1 件治愈血清备用
- 携带数量 1 件（治愈血清稀有，避免一人囤积）
- 2500 tick 拾取冷却避免反复拾取
- 征召中/医疗 Job 中/床上休养均跳过（医疗守卫）

### 子功能2：HealerSerumAllocator（治愈血清注射）

**触发条件**：重伤员（`SummaryHealthPercent < 0.3` + `ShouldBeTendedNowByPlayer`）+ 伤员库存有 MechSerumHealer

**测试步骤**：
1. 先用"子功能1：SerumCarrier"给医生携带治愈血清
2. 用开发模式重伤害医生（SummaryHealthPercent < 30%），或让其在战斗中重伤
3. 确保该伤员库存有 MechSerumHealer（由 SerumCarrier 携带）
4. ITab 勾选"自动血清" → 点击"立即血清"
5. 观察伤员是否自注射 MechSerumHealer，最严重伤情被治愈

**预期行为**：
- 全局扫描重伤员（SummaryHealthPercent < 0.3）
- 重伤员库存有治愈血清 → 自注射（`JobDefOf.UseItem`）
- MechSerumHealer 的 `CompUseEffect_FixWorstHealthCondition` 自动治愈最严重伤情
- 伤员不应用医疗守卫（救命优先于"床上不注射"规则，UseItem Job 短暂 600 tick）
- 仅限伤员自身库存，不走医生手术给药（简化实现）

### 子功能3：BoostSerumAllocator（增益血清注射，Anomaly DLC）

**前置条件**：启用 Anomaly DLC

**触发条件**：Pawn 处于战斗情境（`ContextDetector.GetContext == Combat`）+ 60000 tick 冷却已过

**测试步骤**：
1. 确保启用 Anomaly DLC，地图上有 MetalbloodSerum/JuggernautSerum/MindNumbSerum
2. 征召一个战斗员（触发战斗情境）
3. ITab 勾选"自动血清" → 点击"立即血清"
4. 观察战斗员是否自动注射增益血清：
   - Brawler 角色 → 注射 Metalblood（减伤增强生存）
   - 其他战斗员 → 注射 Juggernaut（力量/速度↑）
   - 低血量（< 50%）→ 注射 MindNumb（防精神崩溃）
5. 检视战斗员 → 健康面板应显示对应 hediff

**预期行为**：
- 战斗情境自动注射增益血清（Anomaly DLC）
- Brawler → Metalblood / 其他 → Juggernaut / 低血量<50% → MindNumb
- 优先库存，其次地图上找最近可用
- 60000 tick（1 天）注射冷却避免频繁注射
- 已有对应 hediff 时跳过（避免重复注射浪费）
- 未启用 Anomaly DLC 时静默跳过（`ModsConfig.AnomalyActive` 检查）

---

## 异常排查

### 即时按钮点击无反应

1. **检查总开关**：ITab 勾选框是否已勾选（药物/食物/血清对应总开关）
2. **检查子开关**：MOD 选项 → 对应子功能是否已开启
3. **检查 Player.log**：搜索 `[AE]` 前缀日志，查看是否有 ErrorOnce 错误
4. **检查触发条件**：确认当前场景满足子功能触发条件（如征召状态、伤员存在、库存无物品等）

### 重复执行问题

- **周期门控**：自动模式每 3000 tick 执行一次，即时按钮可绕过周期
- **拾取冷却**：MealCarrier/MedicationAllocator/SerumCarrier 有 2500 tick 拾取冷却
- **注射冷却**：BoostSerumAllocator 有 60000 tick（1 天）注射冷却
- **政策去重**：DrugPolicyManager/FoodRestrictionManager 跳过已设置相同 label 的 Pawn

### DLC 相关

- **BoostSerumAllocator**：必须启用 Anomaly DLC，否则静默跳过
- **SerumCarrier/HealerSerumAllocator**：MechSerumHealer 是 Core 物品，无需 DLC
- **食尸鬼**：不参与任何自动模块（PawnSuitabilityChecker.CanManageGear 过滤）

### 日志位置

- **Windows**：`%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Player.log`
- **搜索关键字**：`[AE]` 前缀（AutoEverything 日志）
- **错误去重**：`Log.ErrorOnce` 同一错误只记录一次，id = `thingIDNumber ^ salt`

---

## 测试检查清单

- [ ] AutoDrug：药品政策自动设置（医生/战斗员/普通角色）
- [ ] AutoDrug：自动治疗（伤员+空闲医生）
- [ ] AutoDrug：预防服药（penoxycyline）
- [ ] AutoFood：征召携带行军口粮
- [ ] AutoFood：喂食卧床饥饿伤员
- [ ] AutoFood：食物限制自动切换（医生/伤员）
- [ ] AutoSerum：医生/高评级携带治愈血清
- [ ] AutoSerum：重伤员自注射治愈血清
- [ ] AutoSerum：战斗注射增益血清（需 Anomaly DLC）
- [ ] ITab 即时按钮可反复点击触发
- [ ] 取消勾选仅停止自动，保留当前配置
- [ ] 无异常报错（Player.log 无 `[AE]` ErrorOnce）
