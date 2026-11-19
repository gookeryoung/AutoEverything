# 需求卡片：自动装备排除任务临时成员（难民等）

- 主题目录：`.trae/specs/equip-quest-lodger-toggle/`
- 来源 / 提出人：用户（2026-11-19）
- 日期：2026-11-19
- 关联 ADR：无（S1）
- 定级：**S1**（新增设置键 + UI + 人员过滤口径变化，跨 `Core` / `AutoEquip` / `UI` / `Languages` 四处）
- 状态：**已确认并落地**（2026-11-19，`make test-check` 全绿）

## 需求原句（逐字）

> 提供选项，设置是否允许非玩家殖民者自动装备，避免难民等带装备跑路

## 可实施约束（C1..Cn）

| # | 需求句（原文片段） | 可实施约束 | 涉及模块 | 验收方式 |
|---|---|---|---|---|
| C1 | 提供选项，设置是否允许 | 新增布尔设置 `equipAllowQuestLodgers`（默认 `false`），存档键 `ae_equipAllowQuestLodgers`；【自动】→ 自动装备页新增勾选框与悬停说明；中英文 Key 同步 | `Core/AESettings.Equip.cs`、`UI/MainTabWindow_AutoEverything.cs`、`Languages/{ChineseSimplified,English}/Keyed/AE_Keyed.xml` | 读档保留；默认值为 `false`；UI 人工验收（中文不换行、滚动不裁剪） |
| C2 | 非玩家殖民者自动装备 | 判定口径 = `QuestUtility.IsQuestLodger(pawn)`（反编译确认实现为 `HasExtraHomeFaction(null) \|\| HasExtraMiniFaction(null)`），覆盖难民、逃兵等任务临时成员；为 `false` 时在人员收集阶段跳过，为 `true` 时沿用现状 | `AutoEquip/EquipAllocator.cs`、`AutoEquip/EquipPolicy.cs` | 纯逻辑真值表单测 + 游戏内任务实测 |
| C3 | 避免难民等带装备跑路 | 默认不允许：主武器、服装/护甲、副武器、投掷物全部不为任务临时成员派发 Job；不追溯收回已穿装备（不做卸装 Job） | `AutoEquip/EquipAllocator.cs` | 任务临时成员在场时「立即装备」不产生其 Job |

约束编号在 ADR 中原样引用；本需求无 ADR。

## 现状影响面（已核实）

| # | 受影响能力 | 位置 | 备注 |
|---|---|---|---|
| 1 | 人员收集与过滤 | [EquipAllocator.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/AutoEquip/EquipAllocator.cs#L128-L163) | 使用 `PawnsFinder.AllMaps_FreeColonistsSpawned` + `pawn.IsColonist`；任务临时成员属玩家阵营、`IsFreeColonist == true`，因此目前会被纳入 |
| 2 | 设置字段与持久化 | [AESettings.Equip.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/Core/AESettings.Equip.cs#L15-L53) | 现有装备设置均在此 partial 维护 |
| 3 | 装备页 UI | [MainTabWindow_AutoEverything.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/UI/MainTabWindow_AutoEverything.cs#L1008-L1069) | 顶部为总开关/周期/按钮，下方三个分组 |
| 4 | 本地化 | `Languages/ChineseSimplified/Keyed/AE_Keyed.xml:192-232`、`Languages/English/Keyed/AE_Keyed.xml:191-231` | 已有一整套 `AE_CFG_Equip*` / `AE_TT_Equip*` |
| 5 | 玩家可见口径 | `README.md`「自动装备」章节、`CHANGELOG.md` `[Unreleased]` | 规则要求行为变化必须同步 |

### 反编译证据

`RimWorld.QuestUtility::IsQuestLodger(Pawn p)` 的 IL 为：

```
ldarg.0; ldnull; call HasExtraHomeFaction
brtrue.s L
ldarg.0; ldnull; call HasExtraMiniFaction
ret
L: ldc.i4.1; ret
```

即 `p.HasExtraHomeFaction(null) || p.HasExtraMiniFaction(null)`，是原版判定「任务临时成员」的标准入口（普通殖民者 `homeFaction == Faction.OfPlayer`，不命中）。

## 方案要点

- 过滤点放在 `EquipAllocator.ShouldSkipPawn` 的殖民者判定之后，对所有入口（周期、脏标、立即装备）一致生效。
- 纯逻辑抽 `EquipPolicy.ShouldSkipQuestLodger(isQuestLodger, allowQuestLodgers)`，同轮补真值表单测。
- 临时成员结束任务转为正式殖民者后，`IsQuestLodger` 自然变为 `false`，下一轮恢复自动装备，无需额外状态。

## 未决问题

- [x] 默认值：默认不允许（`false`），即默认不为难民等临时成员装备；玩家可显式勾选恢复现有行为。
- [x] 已穿装备处置：关闭/默认状态下不为他们派发卸装 Job，只阻止后续自动装备（避免额外 Job 与中途脱甲风险）。
- [x] 口径范围：仅「任务临时成员」（`QuestUtility.IsQuestLodger`）；奴隶/囚犯/食尸鬼等维持既有跳过逻辑。

## 卡点

- [x] 每条需求句都有 C 编号约束（无遗漏、无「待定」）
- [x] 需求方确认方案（默认不允许；已穿装备不处理）
