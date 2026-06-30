# 自动装备重配 + 重甲优先 + 奴隶工作 + 狩猎限制

> 用户需求（4 项）：
> 1. 除手动全局重配外，间隔 3000 ticks 自动寻找替换装备。武器和防具按高评级优先分配
> 2. 防具按重甲单位优先原则分配
> 3. 奴隶没有自动分配工作，需解决
> 4. 无兴趣低技能的人被安排狩猎（priority=2），需从配备远程武器的人中选择，优先级顺序不变（兴趣>等级）

## 决策摘要（来自 AskUserQuestion）

- **自动重配机制**：轻量升级检查（每 Pawn 调用 `ForceEvaluate`，不放下当前装备，不打断战斗）
- **重甲优先策略**：加大匹配奖励（保持现有逐件分配算法，将 Heavy+heavy / Light+light 匹配奖励从 +50 提升到 +500）

## 现状分析

### 已确认的关键代码位置

| 关注点 | 文件 | 行号 | 说明 |
|---|---|---|---|
| 自动触发模式 | `Source/AutoEverything/Core/AutoExecutor.cs` | L23-87 | 静态门控：`CheckInterval=60`、`ExecuteInterval=3000`，已有 `lastWorkTick`/`lastTierTick`/`TriggerWorkNow`/`TriggerTierNow`/`ExecuteWork`/`ExecuteTier` 模式 |
| 轻量换装入 | `Source/AutoEverything/AutoEquipment/CompGearManager.cs` | L295-332 | `ReloadAllColonists()` 静态方法：遍历所有 `mapPawns.AllPawnsSpawned`，调用 `comp.ForceEvaluate(ReloadTarget.All)`。但该方法**未过滤奴隶**，自动路径不能直接复用 |
| 设置开关模式 | `Source/AutoEverything/Core/AESettings.cs` | L20-21 | `autoWorkEnabled = true` / `autoTierTag = true`，L455-456 用 `LookCompat` 持久化 |
| ITab 底部布局 | `Source/AutoEverything/UI/ITab_GearManager.cs` | L139-147、L313-379 | 2 勾选框 + 1 按钮布局；`contentRect` 高度公式 `checkboxHeight * 2 + buttonHeight + buttonGap * 3` |
| 护甲匹配奖励 | `Source/AutoEverything/Allocation/GlobalAllocator.cs` | L377-383 | `score += 50f;` 硬编码，需要改为读取 `AESettings.heavyArmorMatchBonus` |
| 奴隶工作过滤 | `Source/AutoEverything/AutoWork/WorkAllocator.cs` | L57-66 | `FreeColonistsSpawned` 不含奴隶；L62 `if (DLCCompat.IsSlave(pawn)) continue;` 需删除并补充奴隶收集 |
| 狩猎分配 | `Source/AutoEverything/AutoWork/WorkAllocator.cs` | L255-296 | `AssignHuntingType` 不过滤远程武器，需在 L257-264 候选过滤中增加远程武器检查 |

### 关键约束（来自项目规则与历史记忆）

- **食尸鬼排除**：所有装备管理逻辑必须排除食尸鬼
- **奴隶装备排除（未征召）**：CompGearManager.CompTick 已对未征召奴隶做 `return`，但 `ForceEvaluate` 是外部调用不走 CompTick，需要单独过滤
- **`FreeColonistsSpawned` vs `SlavesOfColonySpawned`**：前者不含奴隶，后者专门收集奴隶
- **远程武器检查标准写法**：`pawn.equipment?.Primary?.def.IsRangedWeapon == true`（GearContext.cs:188）
- **Tick 路径禁忌**：禁止 LINQ、禁止 `new List<>()`，但 AutoExecutor 非高频 Tick 路径（60 tick 一次门控），可适度放宽
- **错误去重 salt**：每个错误点独立，避免跨方法冲突。现有 `WorkErrorSalt = 0xA200`、`TierErrorSalt = 0xA300`，新增 `GearErrorSalt = 0xA400`

## 提案变更

### 变更 1：自动装备重配（轻量升级检查，3000 ticks 周期）

#### 1.1 AESettings.cs 新增字段

在 L20-21 附近新增：
```csharp
public static bool autoGearReallocate = true;  // 自动装备重配主开关（轻量升级检查，周期触发）
```

在 L455-456 附近新增持久化：
```csharp
LookCompat(ref autoGearReallocate, "autoGearReallocate", true);
```

#### 1.2 AutoExecutor.cs 新增 Gear 触发

**新增字段（L33-42 附近）：**
```csharp
private static int lastGearTick = -9999;
private const int GearErrorSalt = 0xA400;
```

**修改 `TryTick()`（L49-87）：**
- 首次初始化守卫中增加 `lastGearTick = tick;`（与 `lastWorkTick`/`lastTierTick` 并列）
- 新增殖民者检测中增加 `ExecuteGear(tick, showMessage: false);`（与 `ExecuteWork`/`ExecuteTier` 并列）
- 周期触发块中增加：
  ```csharp
  if (tick - lastGearTick >= ExecuteInterval)
  {
      ExecuteGear(tick, showMessage: false);
  }
  ```

**新增公共方法：**
```csharp
public static void TriggerGearNow()
{
    ExecuteGear(Find.TickManager.TicksGame, showMessage: true);
}
```

**新增私有方法 `ExecuteGear`：**
- 不直接复用 `ReloadAllColonists()`（该方法未过滤奴隶）
- 自行遍历 `map.mapPawns.FreeColonistsSpawned`，应用过滤链：食尸鬼/不适用/Dead/Downed/奴隶/锁定
- 调用 `comp.ForceEvaluate(CompGearManager.ReloadTarget.All)`
- try-catch 隔离 + `Log.ErrorOnce` 防重复
- `showMessage: true` 时通过 `Messages.Message` 反馈数量
- 关键代码骨架：
  ```csharp
  private static void ExecuteGear(int tick, bool showMessage)
  {
      lastGearTick = tick;
      if (!AESettings.autoGearReallocate) return;
      try
      {
          int n = 0;
          foreach (Map map in Find.Maps)
          {
              foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
              {
                  if (DLCCompat.IsGhoul(pawn)) continue;
                  if (!PawnSuitabilityChecker.CanManageGear(pawn)) continue;
                  if (pawn.Dead || pawn.Downed) continue;
                  if (DLCCompat.IsSlave(pawn)) continue;   // 奴隶未征召不参与自动装备
                  if (pawn.Drafted) continue;              // 不打断征召战斗
                  var comp = pawn.GetComp<CompGearManager>();
                  if (comp == null) continue;
                  if (comp.locked) continue;               // 尊重玩家锁定
                  comp.ForceEvaluate(CompGearManager.ReloadTarget.All);
                  n++;
              }
          }
          AEDebug.Log(() => $"[AutoExecutor] 自动装备重配: {n} 个殖民者 (tick={tick})");
          if (showMessage)
          {
              Messages.Message("AE_AutoGearReallocateResult".Translate(n),
                  MessageTypeDefOf.TaskCompletion);
          }
      }
      catch (Exception ex)
      {
          Log.ErrorOnce("[AutoEverything] 自动装备重配失败: " + ex.Message, GearErrorSalt);
      }
  }
  ```

#### 1.3 ITab_GearManager.cs 新增第三个勾选框

**修改 L139-147 的 `buttonHeight`/`checkboxHeight` 计算：**
- 现有：`checkboxHeight * 2 + buttonHeight + buttonGap * 3`（2 勾选框 + 1 按钮 + 3 间隔）
- 改为：`checkboxHeight * 3 + buttonHeight + buttonGap * 4`（3 勾选框 + 1 按钮 + 4 间隔）

**在 L361（work 勾选框结束）与 L362（按钮开始）之间插入 gear 勾选框：**
```csharp
// 3. 装备自动重配勾选框：勾选立即执行 + 启用周期自动；取消勾选仅停止自动（保留当前装备）
Rect gearCheckRect = new Rect(
    rect.x,
    workCheckRect.yMax + buttonGap,
    rect.width,
    checkboxHeight);

bool prevWrap3 = Text.WordWrap;
Text.WordWrap = false;
bool prevGear = AESettings.autoGearReallocate;
Widgets.CheckboxLabeled(gearCheckRect, "AE_AutoGearReallocate".Translate(), ref AESettings.autoGearReallocate);
Text.WordWrap = prevWrap3;
TooltipHandler.TipRegion(gearCheckRect, "AE_TT_AutoGearReallocate".Translate());
if (AESettings.autoGearReallocate && AESettings.autoGearReallocate != prevGear)
{
    AutoExecutor.TriggerGearNow();
}
```

**修改 L364-368 的按钮位置：**
- `workCheckRect.yMax + buttonGap` 改为 `gearCheckRect.yMax + buttonGap`

#### 1.4 翻译 XML 新增键

中英文 `AE_Keyed.xml` 各新增 3 个键：
- `AE_AutoGearReallocate` — 勾选框标签
  - 中：`装备自动重配`
  - 英：`Auto Gear Reallocate`
- `AE_TT_AutoGearReallocate` — Tooltip
  - 中：`开启后每 3000 tick（约 50 秒）自动评估装备升级；不打断征召状态；奴隶与锁定者除外`
  - 英：`When enabled, automatically evaluate gear upgrades every 3000 ticks (~50s); does not interrupt drafted pawns; slaves and locked pawns are excluded`
- `AE_AutoGearReallocateResult` — 触发反馈消息
  - 中：`已自动重配 {0} 个殖民者的装备`
  - 英：`Auto gear evaluation triggered for {0} colonists`

### 变更 2：防具重甲优先（加大匹配奖励）

#### 2.1 AESettings.cs 新增字段

在已有的 `heavyArmorPenaltyForLight`/`lightArmorPenaltyForHeavy` 附近新增：
```csharp
public static float heavyArmorMatchBonus = 500f;  // 角色偏好与护甲类型匹配时的奖励分（Heavy+heavy / Light+light）
```

在 `LookCompat` 区域新增持久化：
```csharp
LookCompat(ref heavyArmorMatchBonus, "heavyArmorMatchBonus", 500f);
```

#### 2.2 GlobalAllocator.cs 修改匹配奖励

**L377-383 修改：**
```csharp
// 旧：
else if ((pref == ArmorPreference.Heavy && isHeavy)
         || (pref == ArmorPreference.Light && !isHeavy))
    score += 50f;  // 匹配奖励

// 新：
else if ((pref == ArmorPreference.Heavy && isHeavy)
         || (pref == ArmorPreference.Light && !isHeavy))
    score += AESettings.heavyArmorMatchBonus;  // 匹配奖励（默认 500，让匹配偏好显著胜过 Flexible）
```

**同步更新 L372-376 的注释**，把"+50"改为"+AESettings.heavyArmorMatchBonus（默认 500）"。

**同步更新 L385-388 的评级权重注释**，说明 0.5/档 × 7 档 = 3.5，远小于 500 匹配奖励。

### 变更 3：奴隶参与工作分配

#### 3.1 WorkAllocator.cs ReallocateAll 修改候选收集

**L53-66 修改：**
```csharp
// 旧：
candidatePawns.Clear();
foreach (Map map in Find.Maps)
{
    foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
    {
        if (DLCCompat.IsGhoul(pawn)) continue;
        if (!PawnSuitabilityChecker.CanManageGear(pawn)) continue;
        if (pawn.Dead || pawn.Downed) continue;
        if (DLCCompat.IsSlave(pawn)) continue;  // ← 删除此行
        candidatePawns.Add(pawn);
    }
}

// 新：
candidatePawns.Clear();
foreach (Map map in Find.Maps)
{
    // 殖民者（FreeColonistsSpawned 不含奴隶）
    foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
    {
        if (DLCCompat.IsGhoul(pawn)) continue;
        if (!PawnSuitabilityChecker.CanManageGear(pawn)) continue;
        if (pawn.Dead || pawn.Downed) continue;
        candidatePawns.Add(pawn);
    }
    // 奴隶（Biotech DLC 才有，无 DLC 时 SlavesOfColonySpawned 返回空列表）
    foreach (Pawn pawn in map.mapPawns.SlavesOfColonySpawned)
    {
        if (DLCCompat.IsGhoul(pawn)) continue;
        if (!PawnSuitabilityChecker.CanManageGear(pawn)) continue;
        if (pawn.Dead || pawn.Downed) continue;
        candidatePawns.Add(pawn);
    }
}
```

**注**：`mapPawns.SlavesOfColonySpawned` 在无 Biotech DLC 时返回空列表，无需 `ModsConfig.BiotechActive` 检查。`PawnSuitabilityChecker.CanManageGear` 已能过滤动物/机械族等。

### 变更 4：狩猎需远程武器

#### 4.1 WorkAllocator.cs AssignHuntingType 修改候选过滤

**L255-264 修改：**
```csharp
// 旧：
private static void AssignHuntingType(WorkTypeDef workType)
{
    workCandidates.Clear();
    for (int i = 0; i < candidatePawns.Count; i++)
    {
        Pawn pawn = candidatePawns[i];
        if (pawn.WorkTagIsDisabled(workType.workTags)) continue;
        workCandidates.Add(pawn);
    }
    if (workCandidates.Count == 0) return;

// 新：
private static void AssignHuntingType(WorkTypeDef workType)
{
    workCandidates.Clear();
    for (int i = 0; i < candidatePawns.Count; i++)
    {
        Pawn pawn = candidatePawns[i];
        if (pawn.WorkTagIsDisabled(workType.workTags)) continue;
        // 狩猎需远程武器：避免无兴趣低技能者被分配（priority=2）
        // 优先级顺序不变（兴趣>等级仍由 ComparePawnsForHunting 保证）
        if (pawn.equipment?.Primary?.def.IsRangedWeapon != true) continue;
        workCandidates.Add(pawn);
    }
    if (workCandidates.Count == 0) return;
```

**注释说明**：
- `pawn.equipment?.Primary?.def.IsRangedWeapon` 标准远程武器判定（参考 GearContext.cs:188）
- 未装备武器 / 装备近战武器 / 装备非武器 → 都被排除
- 修复后：无兴趣低技能的殖民者不会被分配狩猎工作（除非他们装备了远程武器，此时仍按 兴趣>等级 排序）

### 变更 5：README.md + 翻译同步

#### 5.1 README.md 需要更新的章节

1. **自动执行章节**：新增"装备自动重配"条目，说明 3000 ticks 周期 + 轻量升级 + 奴隶/锁定/征召排除
2. **评估周期表**：新增"自动装备重配"行（3000 ticks）
3. **护甲分配章节**：说明 `heavyArmorMatchBonus` 默认 500 取代旧 +50 硬编码
4. **奴隶工作章节**：新增"奴隶参与工作分配"说明（含 `SlavesOfColonySpawned` 收集逻辑）
5. **狩猎限制章节**：新增"狩猎需远程武器"说明（修复无兴趣低技能者被分配）

#### 5.2 autoeverything-project.md 同步检查清单

在同步检查清单中新增：
- 改了 `AutoExecutor.cs` → README `自动执行` 章节 + `评估周期` 表格已更新（已存在，但需补充装备重配条目）

### 变更 6：编译验证

执行 `make check` 验证 0 警告 0 错误。如失败则修复后再次执行。

## 假设与决策

### 决策

1. **自动重配用轻量升级检查而非完整 ReallocateAll**：避免每 50 秒引发殖民者大规模跑动；调用 `comp.ForceEvaluate(ReloadTarget.All)` 让每个 Pawn 独立评估升级机会，不放下当前装备
2. **不直接复用 `ReloadAllColonists()`**：该方法未过滤奴隶，且会扫描 `AllPawnsSpawned`（包含动物/机械族）。在 `ExecuteGear` 内部手动遍历，应用与 `CompGearManager.CompTick` 一致的过滤链
3. **重甲匹配奖励从 +50 提升到 +500**：让 Heavy 偏好者显著胜过 Flexible，但仍小于 -1000 硬否决惩罚；500 远大于评级权重最大值 3.5（7 档 × 0.5），保证匹配偏好优先于评级
4. **`heavyArmorMatchBonus` 设为 AESettings 字段**：与现有 `heavyArmorPenaltyForLight`/`lightArmorPenaltyForHeavy` 模式一致，玩家可在设置面板调整
5. **奴隶收集用 `SlavesOfColonySpawned`**：RimWorld 原生 API，无 DLC 时返回空列表，无需 `ModsConfig.BiotechActive` 检查
6. **狩猎远程武器过滤加在候选收集阶段**：不修改 `ComparePawnsForHunting` 比较器，保持"兴趣>等级"排序逻辑不变
7. **第三个勾选框插在 work 与按钮之间**：保持视觉顺序（评级→工作→装备→按钮），符合"从人到装备"的逻辑递进

### 假设

- `mapPawns.SlavesOfColonySpawned` 在无 Biotech DLC 时返回空列表（RimWorld API 保证）
- `ForceEvaluate(ReloadTarget.All)` 在已装备齐全的 Pawn 上是 no-op 或低开销操作（依赖 GearScorer 的评分逻辑判断是否需要换装）
- 现有 `comp.locked` 字段已正确持久化（前序工作已实现）
- 现有 `LookCompat` 已能处理 bool/float 类型（前序工作已多次使用）

## 不在范围内

- 不修改 `ReloadAllColonists()`（保留手动按钮现有行为）
- 不修改 `GlobalAllocator.ReallocateAll()` 的武器排序（武器排序已按 `ComputeCombatValue` 降序，已隐含高评级优先——因为高评级通常对应高战斗价值）
- 不修改 `CompGearManager.CompTick` 的奴隶过滤（已正确）
- 不修改 `BeltAllocator`/`SidearmAllocator` 的奴隶过滤（已正确）
- 不新增第四个勾选框到 ITab（保持 3 勾选框 + 1 按钮布局）

## 验证步骤

### 编译验证
1. `make check` 必须 0 警告 0 错误

### 游戏内验证清单
1. 启动游戏，ITab 底部应显示 3 勾选框 + 1 按钮（顺序：评级/工作/装备/按钮）
2. 勾选"装备自动重配"，应立即弹消息框"已自动重配 N 个殖民者的装备"
3. 等待 3000 ticks（约 50 秒），观察 `[AutoEverything] 自动装备重配` 日志输出
4. 殖民者不应频繁跑动换装（轻量升级检查，仅在有更优装备时才换）
5. 征召中的殖民者不应被自动换装（`if (pawn.Drafted) continue;`）
6. 锁定的殖民者不应被自动换装（`if (comp.locked) continue;`）
7. 奴隶应出现在工作分配中（用开发者模式查看 `Work` 标签，奴隶应有非零优先级）
8. 无远程武器的殖民者不应被分配狩猎工作（检查 `Hunting` 优先级为 0）
9. 装备远程武器但无兴趣的殖民者可被分配狩猎 priority=4（备选），不应是 priority=2
10. 重甲偏好殖民者应优先获得重甲（即使评级低于 Flexible 殖民者）

### 边界用例
- 无 Biotech DLC：奴隶收集返回空列表，工作分配逻辑正常
- 全奴隶殖民地：工作分配应正常（奴隶成为主要劳动力）
- 全殖民者已征召：`ExecuteGear` 应跳过所有人，不报错
- 全殖民者锁定：`ExecuteGear` 应跳过所有人，不报错
- 无武器殖民者：狩猎优先级应为 0（被远程武器过滤排除）

## 文件清单

| 文件 | 修改类型 | 行数预估 |
|---|---|---|
| `Source/AutoEverything/Core/AESettings.cs` | 新增 2 字段 + 2 LookCompat | +6 |
| `Source/AutoEverything/Core/AutoExecutor.cs` | 新增 lastGearTick + GearErrorSalt + TryTick 集成 + TriggerGearNow + ExecuteGear | +40 |
| `Source/AutoEverything/UI/ITab_GearManager.cs` | 修改 contentRect 高度公式 + 插入第 3 勾选框 + 修改按钮位置 | +18 |
| `Source/AutoEverything/Allocation/GlobalAllocator.cs` | 修改 L377-383 匹配奖励 + 更新注释 | +3 |
| `Source/AutoEverything/AutoWork/WorkAllocator.cs` | 修改 L53-66 奴隶收集 + 删除 L62 + 修改 L255-264 狩猎过滤 | +12 |
| `Languages/ChineseSimplified/Keyed/AE_Keyed.xml` | 新增 3 翻译键 | +3 |
| `Languages/English/Keyed/AE_Keyed.xml` | 新增 3 翻译键 | +3 |
| `README.md` | 更新 5 章节 | +30 |
| `.trae/rules/autoeverything-project.md` | 同步检查清单已含 AutoExecutor 项，无需修改 | 0 |

**总计**：约 +115 行变更

## 实施顺序

1. AESettings.cs（新增字段，后续依赖）
2. AutoExecutor.cs（核心逻辑）
3. WorkAllocator.cs（奴隶 + 狩猎修复）
4. GlobalAllocator.cs（重甲匹配奖励）
5. ITab_GearManager.cs（UI 集成）
6. AE_Keyed.xml 中英文（翻译）
7. README.md（文档同步）
8. `make check` 验证
9. 如失败则修复后再次执行 `make check`
