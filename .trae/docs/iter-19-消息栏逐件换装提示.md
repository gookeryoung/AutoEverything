# 迭代 19：消息栏逐件换装提示（req-08 需求 4）

## 需求清单

- [x] req-08 需求 1：按殖民者优先级排序换装（已满足，无需改动）
- [x] req-08 需求 2：判定更优装备并锁定殖民者（当前 MarkAllocated + 顺序处理已等价满足，无需改动）
- [x] req-08 需求 3：无更优装备不操作（已满足，无需改动）
- [x] req-08 需求 4：换装时消息栏提示判断依据（本次实现）

来源：用户提示 `req-08-完善自动装备.md` 4 项需求未完成。

## 迭代目标

实现 req-08 需求 4：ITab 手动触发装备重分配时，每件换装成功后在消息栏弹出"旧装备 → 新装备 + 得分变化 + 偏好"提示，便于玩家调试。周期触发不弹避免刷屏。

需求 1/2/3 经分析已满足，本轮不动。

## 改动文件清单

| 文件 | 改动 |
|------|------|
| `Source/AutoEverything/AutoEquipment/GearAllocator.cs` | `AllocateForPawn` 增加 `showMessage` 参数；换装成功后若 `showMessage=true` 调用 `Messages.Message` 弹出 `AE_AutoGear_SwapDetail`；`ExecuteAllocation` 调用处传入 `showMessage` |
| `Languages/ChineseSimplified/Keyed/AE_Keyed.xml` | 新增 `AE_AutoGear_SwapDetail` Key |
| `Languages/English/Keyed/AE_Keyed.xml` | 新增 `AE_AutoGear_SwapDetail` Key |
| `README.md` | AutoEquipment 章节新增"消息栏逐件换装提示"小节 |

## 关键决策与依据

### 1. 仅实现需求 4，需求 1/2/3 视为已满足

**决策**：用户确认仅实现需求 4（消息栏提示），需求 1/2/3 不动。

**依据**：
- **需求 1（按优先级排序）**：`candidatePawns.Sort(PawnTierComparer.Instance)` 已按 CombatTier 降序 + CombatValue 降序，高评级先选优质装备，已满足
- **需求 2（判定更优 + 锁定）**：`bestScore - currentScore > geReplaceThreshold` 判定更优已实现；"锁定"语义经用户确认为当前 `MarkAllocated` + 顺序处理已等价满足（同一时刻只有一个 Pawn 在选装备，装备成功后立即 MarkAllocated，下个 Pawn 看不到已锁定的装备）
- **需求 3（无更优不操作）**：`bestScore - currentScore <= geReplaceThreshold` 时 `continue` 跳过该层，已满足

### 2. 仅 ITab 手动触发弹逐件消息，周期触发不弹

**决策**：`showMessage` 参数仅由 `TriggerGearNow`（ITab 勾选触发）传 true，`TryAllocateFromTick`（周期触发）传 false。

**依据**：
- 用户决策"仅 ITab 手动触发时弹逐件消息"
- 周期触发频率高（2500 tick 冷却），每次可能换装多件，弹消息会刷屏
- ITab 手动触发是玩家主动行为，弹逐件消息便于玩家调试评分公式与扒装逻辑
- 周期触发的换装信息仍写入 `AEDebug.Log`，需要时可通过 `debugLogging` 开关查看

### 3. 用 string.Format 替代多参 Translate

**决策**：消息构造用 `string.Format("AE_AutoGear_SwapDetail".Translate(), ...)` 而非 `"AE_AutoGear_SwapDetail".Translate(...)`。

**依据**：
- RimWorld 1.6 中 `Translator.Translate(string, params object[])` 标记为过时（CS0618，推荐用 `TranslatorFormattedStringExtensions`）
- 项目用 `-warnaserror` 编译，过时警告升级为错误
- 现有代码 `Translate(int, int)` 与 `Translate(int)` 走的是特定参数数量的未过时重载（编译通过），但 7 参调用无对应重载，走 params 形式触发警告
- RimWorld 翻译占位符 `{0}` `{1}` 与 `string.Format` 兼容，用 `string.Format` + 无参 `Translate()` 是最简方案，无需引入新 API

### 4. 消息类型用 MessageTypeDefOf.TaskCompletion

**决策**：复用现有 `AE_AutoGear_AllocateResult` 的 `MessageTypeDefOf.TaskCompletion` 消息类型（白色中性消息）。

**依据**：
- 与现有"已为 N/M 个殖民者分配装备"总数消息一致，玩家视觉体验统一
- 换装是中性事件，不适合 PositiveEvent/NegativeEvent

## 代码实现情况

### GearAllocator.cs 签名扩展

```csharp
// 原：无 showMessage 参数
private static bool AllocateForPawn(Pawn pawn, Role role, ArmorPreference armorPref, List<Apparel> candidateApparel)

// 新：增加 showMessage 参数
private static bool AllocateForPawn(Pawn pawn, Role role, ArmorPreference armorPref, List<Apparel> candidateApparel, bool showMessage)
```

### ExecuteAllocation 调用处传参

```csharp
if (AllocateForPawn(pawn, effectiveRole, effectivePref, candidateApparel, showMessage))
{
    allocatedCount++;
}
```

### 换装成功后弹消息

```csharp
if (showMessage)
{
    string oldName = currentWorn?.def?.defName ?? "无";
    string newName = best.def?.defName ?? "?";
    string message = string.Format("AE_AutoGear_SwapDetail".Translate(),
        pawn.LabelShort, layerKey.defName, oldName, newName,
        currentScore.ToString("F1"), bestScore.ToString("F1"), armorPref);
    Messages.Message(message, MessageTypeDefOf.TaskCompletion);
}
```

### 翻译 Key

```xml
<!-- ChineseSimplified -->
<AE_AutoGear_SwapDetail>{0} 换装[{1}]: {2} → {3} (得分 {4} → {5}, 偏好 {6})</AE_AutoGear_SwapDetail>

<!-- English -->
<AE_AutoGear_SwapDetail>{0} swap[{1}]: {2} → {3} (score {4} → {5}, pref {6})</AE_AutoGear_SwapDetail>
```

## 测试验证结果

### 编译验证

```
$ make check
AutoEverything net472 已成功 (0.3 秒) → Assemblies\AutoEverything.dll
0 个警告
0 个错误
[check] PASS: No errors
```

### 单元测试

```
$ make test-check
=== AutoEverything.Tests ===
[ApplySkillFloorCoreTests] 30/30 passed
[EvaluateAutoTierCoreTests] 43/43 passed
[PawnMarkerTests/Category] 14/14 passed
[PawnMarkerTests/FormatMessage] 23/23 passed
[PawnMarkerTests/ComputeNewlyMarked] 32/32 passed
[GearAllocatorTests/*] 119/119 passed
[GearScorerTests/*] 109/109 passed
All tests passed. (370 个测试全部通过)
```

## 遗留事项

- 用户需进行游戏内验证：
  - ITab 勾选"自动装备"时，消息栏显示逐件换装提示（含旧装备→新装备、得分变化、偏好）
  - 周期触发（装备生成/销毁事件）不弹消息栏，仅 AEDebug.Log
  - 中英文本地化正确显示
- req-08 需求 1/2/3 视为已满足，如未来用户认为需要进一步强化（如需求 2 的"锁定"语义需更严格），可再开新迭代

## 下一轮计划

无。本次迭代完成 req-08 需求 4，4 项需求全部完结。
