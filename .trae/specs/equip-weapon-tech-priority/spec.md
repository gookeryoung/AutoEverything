# 需求卡片：自动装备武器选择修复（科技优先 + 防重复派发）

- 主题目录：`.trae/specs/equip-weapon-tech-priority/`
- 来源 / 提出人：用户（2026-11-22）
- 日期：2026-11-22
- 关联 ADR：无（沿用仓库既有 spec 约定）
- 定级：**S1**（跨 `AutoEquip` 选择口径与 Job 派发，含玩家可见规则变化，无存档字段变化）
- 状态：已落地（`make test-check` 全绿）

## 需求原句（逐字）

> 自动装备，自动装备武器，殖民者会拿去重复装备或者匕首和弓等低端武器，请解决。

## 可实施约束（C1..Cn）

| # | 需求句（原文片段） | 可实施约束 | 涉及模块 | 验收方式 |
|---|---|---|---|---|
| C1 | 重复装备 | 派发装备 Job 前跳过已有装备/穿戴/取物 Job（当前 Job 或队列）的殖民者，避免反复打断同一 Job；候选增加可达性检查，不可达武器不再每轮重试 | `AutoEquip/EquipAllocator.cs` | 逻辑走查 + 游戏内观察不再反复跑动；现有单测全绿 |
| C2 | 匕首和弓等低端武器 | 一般武器排序改为「科技等级 > 战斗评分 > 品质 > defName」；更高科技的可用武器存在时不再选新石器匕首/弓 | `AutoEquip/EquipPolicy.cs`、`AutoEquip/WeaponScorer.cs` | 纯逻辑单测（科技优先、同科技比战斗分、品质兜底） |
| C3 | 换装判断与排序一致 | 换装阈值改为按同一分层口径判断：特化 > 一般；科技更高直接换；同科技才要求战斗评分达到「换装最小提升」；不因品质加成压过枪械 | `AutoEquip/EquipPolicy.cs` | 单测覆盖无武器/升科技/降科技/同科技/特化互斥 |
| C4 | 文档同步 | README「选择优先级」与 CHANGELOG `[Unreleased]` 同步新口径 | `README.md`、`CHANGELOG.md` | 自查链接与文案 |

## 现状影响面（已核实）

| # | 受影响能力 | 位置 | 备注 |
|---|---|---|---|
| 1 | 武器排序 | [EquipPolicy.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/AutoEquip/EquipPolicy.cs#L32-L53) | 原普通武器「品质优先」，传奇弓可压过普通枪械 |
| 2 | 换装阈值 | [EquipPolicy.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/AutoEquip/EquipPolicy.cs#L59-L76) | `ComparableScore` 叠加 `品质*10`，且特化 1e6 基数使特化之间永远达不到 10% 阈值 |
| 3 | 主武器派发 | [EquipAllocator.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/AutoEquip/EquipAllocator.cs#L275-L350) | 未检查殖民者是否已在装备途中 |
| 4 | 人员过滤 | [EquipAllocator.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/AutoEquip/EquipAllocator.cs#L149-L166) | 只过滤玩家强制 Job，不过滤装备类 Job |
| 5 | 候选可用性 | [EquipAllocator.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/AutoEquip/EquipAllocator.cs#L631-L649) | 无 `CanReach`，不可达目标也派发 |

## 方案要点

- `WeaponScoreInput` 增加 `TechRank`（`ThingDef.techLevel` 枚举值）。
- 一般武器：科技等级降序 → 战斗评分降序 → 品质降序 → `defName`；特化仍为特性价值优先。
- `ShouldSwapWeapon(current, candidate, pct)` 替换 `ComparableScore`：特化与一般互斥按特化优先；同类特化比特性价值，价值相同比战斗分；一般武器科技更高直接换、科技更低不换、同科技按战斗分过阈值。
- `EquipAllocator.ShouldSkipPawn` 增加装备类 Job 守卫；`IsCandidateAvailable` 增加 `pawn.CanReach(..., PathEndMode.Touch, Danger.Deadly)`。
- 不新增设置项、不改存档键、中英文翻译不变。

## 非目标

- 不改变特化武器 > 一般武器的既有规则。
- 不引入市价、材质或武器类别权重。
- 不新增“最低武器科技等级”设置项（如后续需要再单独立项）。
