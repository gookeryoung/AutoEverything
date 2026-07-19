# 迭代记录 iter-01：自动装备模块（AutoEquipment）

## 需求清单

来源 `.trae/req/req-01-自动装备需求.md`：

- [x] 角色评级：优先给高评级的角色优质装备
- [x] 角色定位：前排重甲/后排射手/工人等定位契合
- [x] 角色文化：避免心情降低，护甲类似时给增加心情的装备
- [x] 殖民者类别：奴隶优先给符合其定位的装备
- [x] 不设计 Ticks 检查策略，基于事件触发装备分配
- [x] 勾选切换自动装备功能，切换时立即执行全局装备分配
- [x] 默认状态下仅在殖民者变化或库存装备变化时触发装备分配

补充确认（AskUserQuestion）：

- 装备范围：仅护甲类（含衣物层），不含附件（腰带/背包）
- 重分配策略：全局重分配（可扒装）
- 文化处理：综合违反判定 + 偏好材质（后因 API 限制简化为违反扣分 + 符合要求加分）
- 奴隶参与：统一分配，与自由殖民者同池

## 迭代目标

1. 新建 `AutoEquipment` 模块（5 个源文件）实现事件驱动的全局装备重分配
2. 复用现有 `PawnRole`/`ArmorPreference`/`CombatTier` 评级体系
3. 13 个评分权重参数暴露到 Mod 设置，紧凑 UI
4. 同步规则文件、README、翻译键、迭代记录

## 改动文件清单

### 新增源文件

- `Source/AutoEverything/AutoEquipment/ApparelLayerFilter.cs` — 装备层过滤器（排除附件层）
- `Source/AutoEverything/AutoEquipment/CultureChecker.cs` — 文化/心情契合评分
- `Source/AutoEverything/AutoEquipment/GearInventoryService.cs` — 候选装备/Pawn 收集器
- `Source/AutoEverything/AutoEquipment/GearAllocator.cs` — 装备分配主入口（事件驱动 + 周期去抖）
- `Source/AutoEverything/AutoEquipment/GearScorer.cs` — 装备评分器（5 项加权求和）

### 修改文件

- `Source/AutoEverything/Core/AESettings.cs` — 新增 `autoEquipmentEnabled` 开关与 13 个 `ge*` 评分权重字段，含 Scribe 持久化
- `Languages/English/Keyed/AE_Keyed.xml` — 新增 `AE_AutoEquipment`、13 个 `AE_ge*`、`AE_AutoGear_AllocateResult`、`AE_TT_AutoEquipment` 翻译键
- `Languages/ChineseSimplified/Keyed/AE_Keyed.xml` — 对应中文翻译键
- `README.md` — 新增「自动装备分配（AutoEquipment）」章节、目录结构、模块职责、同步清单
- `.trae/rules/autoeverything-project.md` — 新增 AutoEquipment 模块条目、自动装备分配原则、同步清单 3 条、ITab 4 勾选框布局
- `.trae/rules/rule-06-自动装备模块新增.md` — 规则变动记录（新增）
- `c:\Users\zhou\.trae-cn\memory\projects\-e-SteamLibrary-steamapps-common-RimWorld-Mods-AutoEverything\project_memory.md` — 追加 Rule File Update 与 Feature Update 章节

## 关键决策与依据

### 1. 事件驱动设计（不引入 Tick 检查）

Harmony Postfix 仅设置 `GearAllocator.IsDirty=true`，由 `AutoExecutor` 周期评估时去抖执行（2500 tick 冷却 + 战斗过滤）。ITab 勾选切换走 `TriggerGearNow()` 立即执行，不受冷却限制。

事件清单：`Thing.SpawnSetup`/`Thing.Destroy`/`Pawn.SetFaction`/`Pawn.Kill`。

### 2. 全局重分配（可扒装）

候选装备池 = 地图上未穿戴 apparel + 玩家阵营 Pawn 已穿戴 apparel。按 `CombatTier` 降序排 Pawn，高评级先选优质装备。同层 apparel 选最高分，必要时从他人身上扒下。

### 3. 评分模型

5 项加权求和：

```
score = armorScore        // Sharp+Blunt+Heat 三项平均
      + layerMatchScore   // Heavy 线性加分 / Light 低护甲加分 / Flexible 无偏置
      + cultureScore      // ideo 违反扣分 / 符合要求加分
      + insulationScore   // 寒冷保暖 / 炎热隔热加分
      - movementPenalty   // Mass × 角色敏感度权重
```

13 个权重参数持久化到 `AESettings`，玩家可在 Mod 选项中通过紧凑滑块调整。

### 4. 紧凑 UI

- ITab 底部从 3 勾选框扩为 4 勾选框，2×2 双列布局
- Mod 选项用单行紧凑滑块（标签 + 数值 + 滑块），避免每个参数占用一行

### 5. 范围限定

- 装备范围：仅护甲类（含衣物层），通过 `ApparelLayerFilter` 排除 Belt/Backpack/Bag/Pack
- Pawn 范围：自由殖民者 + 玩家阵营奴隶，排除食尸鬼（无法穿戴 apparel）
- X 档（禁止暴力）：GearAllocator 直接跳过，不进入评分

### 6. Karpathy 四原则审视

- 简单优于复杂：贪心分配算法 + 层级独占，不用策略模式
- 删除优于扩展：因 API 限制移除 stuff 偏好功能（保留违反扣分 + 符合加分已覆盖主要场景）
- 理解优于记忆：每个 `ge*` 参数与公式项一一对应，命名自解释
- 原型优于规划：先修复 17 处 API 不匹配问题再优化

## 代码实现情况

### 评分器（GearScorer.cs）

5 个评分项各自独立方法，权重读取 `AESettings.ge*` 静态字段。`ComputeMovementPenalty` 用 `StatDefOf.Mass` 作为代理指标（RimWorld 1.6 中 apparel 无直接 `MoveSpeedFactor` stat）。

### 分配器（GearAllocator.cs）

- `TryAllocateFromTick()`：周期入口，受脏标 + 冷却 + 战斗过滤三重门控
- `TriggerGearNow()`：ITab 触发入口，立即执行并弹消息
- `MarkDirty()`：事件 Postfix 调用
- `ExecuteAllocation()`：核心逻辑，按 CombatTier 降序排 Pawn，对每个 Pawn 调用 `AllocateForPawn`
- `AllocateForPawn()`：按 ApparelLayer 分组选最高分，必要时卸旧换新
- `TrySafeRemove`/`TrySafeEquip`：try-catch + Log.ErrorOnce 错误隔离（salt 0xA800/0xA810/0xA820）

### 库存服务（GearInventoryService.cs）

- 复用静态缓冲区（`candidateApparelBuffer`/`candidatePawnBuffer`/`allocatedApparelIds`），Tick 路径不 new List
- `IsForbidden` 通过 `CompForbiddable` 组件查询（RimWorld 1.6 中 `Thing.Forbidden` 不存在）

### 文化检查器（CultureChecker.cs）

- `ViolatesIdeoRequirement`：apparel 覆盖 ideo 要求的 bodyPartGroup 但 def 不匹配 → 扣分
- `MatchesIdeoRequirement`：apparel.def 是 ideo 强制要求的 apparelDef → 加分
- 通过 `precept.def.roleApparelRequirements` + `req.requirement.bodyPartGroupsMatchAny`/`requiredDefs` 访问

### 层过滤器（ApparelLayerFilter.cs）

- `[StaticConstructorOnStartup]` 确保 DefDatabase 就绪
- `DefDatabase<ApparelLayerDef>.GetNamed("Backpack", false)` 动态查询（DefDatabase 是泛型类，GetNamed 是其静态方法）
- 用 `HashSet<Def>` O(1) 查询

## 整合优化情况

- 复用现有 `RoleDetector`/`CombatEvaluator`/`DLCCompat`/`PawnSuitabilityChecker`，无重复实现
- 复用 `AutoExecutor` 周期去抖机制，不引入新的 Tick 入口
- 复用 `AESettings` Scribe 模式（`ae_` 前缀 + LookCompat 双读兼容）

## 测试验证结果

### 编译验证

- `make check` 通过（零错误零警告，`-warnaserror`）
- `dotnet build -c Release` 成功生成 `Assemblies/AutoEverything.dll`

### API 兼容性（17 处修复）

通过 PowerShell 反射加载 `Assembly-CSharp.dll` 查询 RimWorld 1.6 实际类型成员，修正：

1. `Ideo.PreferredApparelStuffs` 不存在 → 移除 stuff 偏好功能
2. `ApparelLayerDefOf.Backpack/Bag/Pack` 不存在 → `DefDatabase<ApparelLayerDef>.GetNamed` 动态查询
3. `PreceptDef.apparelRequirement` 不存在 → `roleApparelRequirements`
4. `PreceptApparelRequirement.apparelDef/bodyPartGroupDef` 不存在 → `req.requirement.bodyPartGroupsMatchAny`/`requiredDefs`
5. `Apparel.Forbidden` 不存在 → `GetComp<CompForbiddable>().Forbidden`
6. `Apparel.WornByPawn` 不存在 → `Apparel.Wearer`
7. `PawnApparelGenerator.CanWearApparelDef` 不存在 → `ApparelUtility.HasPartsToWear`
8. `Pawn_ApparelTracker.TryRemove` 不存在 → `Remove(Apparel)`
9. `Pawn_ApparelTracker.TryEquip` 不存在 → `Wear(Apparel, true)`
10. `StatDefOf.MoveSpeedFactor` 不存在 → `StatDefOf.Mass`，公式从 `(1 - factor) × weight` 改为 `mass × weight`
11. `DefDatabase.GetNamed<T>` 调用语法错误 → `DefDatabase<T>.GetNamed`（DefDatabase 是泛型类）

### 游戏内验证（待用户执行）

- 无 DLC / 有 DLC 环境均启动无报错
- 旧存档加载不丢失数据
- 边界用例：空地图、单殖民者、全奴隶、全征召状态

## 遗留事项

- 游戏内验证待用户实际运行游戏确认（开发环境无法启动 RimWorld）
- `req-01-自动装备需求.md` 中 3 个 `[]` 项将在迭代记录归档时同步标记为 `[x]` 并移动到 `.trae/req/done/`

## 下一轮计划

本轮交付完成后无下一轮计划。如游戏内验证发现问题，开新一轮迭代修复。
