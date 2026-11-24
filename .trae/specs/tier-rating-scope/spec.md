# 需求卡片：人员评级范围配置

- 主题目录：`.trae/specs/tier-rating-scope/`
- 来源 / 提出人：用户（2026-11-24）
- 关联 ADR：无（S1）
- 定级：**S1**（新增设置键 + UI + 人员收集口径变化，跨 `Core` / `UI` / `Languages` / `Test`）
- 状态：**已确认（2026-11-24）**，已实现并通过 `make check` / `make test`

## 需求原句（逐字）

> 人员评级增加范围配置，默认包括殖民者、访问者和奴隶囚犯

## 可实施约束（C1..Cn）

| # | 需求句（原文片段） | 可实施约束 | 涉及模块 | 验收方式 |
|---|---|---|---|---|
| C1 | 增加范围配置 | 人员评级页新增「评级范围」配置块，按人群分组逐项勾选 | `UI/MainTabWindow_AutoEverything.cs`、`Languages/{ChineseSimplified,English}/Keyed/AE_Keyed.xml` | UI 人工验收：中文不换行、滚动不裁剪 |
| C2 | 默认包括殖民者、访问者和奴隶囚犯 | 三组范围开关默认全部开启；新增设置键 `ae_tierScopeColonists` / `ae_tierScopeVisitors` / `ae_tierScopeSlavesPrisoners`，默认 `true` | `Core/AESettings.Judgement.cs` | 读档保留；新建存档默认三组全开 |
| C3 | 人员评级（既有行为） | 评级前缀的应用与「立即评级」按范围收集 Pawn；关闭某组后该组不再新增/刷新前缀 | `Core/PawnCollector.cs`、`Core/AESettings.TierTag.cs` | 纯逻辑真值表单测 + 游戏内实测 |
| C4 | 范围收窄后的清理 | 「清除评级名称」不受范围限制，始终清理所有曾被本 MOD 打过前缀的 Pawn，避免关掉范围后残留前缀无法清除 | `Core/PawnCollector.cs`、`Core/AESettings.TierTag.cs` | 纯逻辑 + 游戏内实测（先评级 → 关闭范围 → 清除） |

约束编号在实现与测试中引用；无 ADR。

## 现状影响面（已核实）

| # | 受影响能力 | 位置 | 备注 |
|---|---|---|---|
| 1 | 评级 Pawn 收集 | [PawnCollector.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/Core/PawnCollector.cs) | 当前仅 `PawnsFinder.AllMaps_FreeColonists` + 玩家食尸鬼；奴隶/囚犯/访客均不参与 |
| 2 | 评级前缀应用/清除 | [AESettings.TierTag.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/Core/AESettings.TierTag.cs) | Apply 与 Clear 共用 `AllManagedPawns` |
| 3 | 评级设置字段与持久化 | [AESettings.Judgement.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/Core/AESettings.Judgement.cs) | 人员评级判定参数集中在此 partial |
| 4 | 评级页 UI | [MainTabWindow_AutoEverything.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/UI/MainTabWindow_AutoEverything.cs#L540-L597) | 顶部为评级开关 + 立即评级/清除按钮 |
| 5 | 名字评级着色 | [HarmonyPatches.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/Core/HarmonyPatches.cs#L708-L746) | 仅玩家阵营自由殖民者；明确排除囚犯/奴隶/精神状态，非玩家阵营不着色 |
| 6 | 玩家可见口径 | `README.md`「全局人物评级标签」、`CHANGELOG.md` `[Unreleased]` | 需同步 |

## 方案要点

- 人群分类纯逻辑 `TierScopeRules.Classify(isPrisoner, isSlave, isVisitor)`：优先级 囚犯 > 奴隶 > 访问者 > 殖民者；`IsIncluded(colonists, visitors, slavesPrisoners, kind)` 判定是否参与评级。
- `PawnCollector` 提供两个入口：`AllManagedPawns()`（按范围，供 Apply）与 `AllTaggablePawns()`（全范围，供 Clear）；食尸鬼跟随「殖民者」开关。
- 访问者判定用 `pawn.GuestStatus == GuestStatus.Guest`（RimWorld 的 GuestStatus 无 Visitor 值，访客即 Guest 状态）且派系非玩家；奴隶/囚犯用 PawnsFinder 的 `AllMapsCaravansAndTravellingTransporters_Alive_SlavesOfColony` / `..._PrisonersOfColony`。
- 奴隶/囚犯范围覆盖远行队/运输中的成员；访问者限已生成（地图上）的访客；食尸鬼沿用地图 Pawn 过滤。
- 名字着色不跟随本范围（保持现有保守口径），如需一并扩展另立需求。

## 已决问题（2026-11-24 用户确认）

- [x] 范围选项划分：三组（殖民者含食尸鬼 / 访问者 / 奴隶与囚犯）
- [x] 关闭某范围时：保留该组已有前缀，由「清除评级名称」统一清理
- [x] 名字评级着色：不跟随范围，维持现有保守口径

## 卡点

- [x] 每条需求句都有 C 编号约束（无遗漏、无「待定」）
- [x] 需求方确认方案
