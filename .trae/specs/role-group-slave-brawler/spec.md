# Spec：修复「有格斗者特质且已勾选，却分到"其他"」

## 1. 问题陈述

玩家反馈：殖民者栏角色分组中，某殖民者明明有**格斗者（Brawler）特质**，且【自动】→ 分组标记页的「前排 / 格斗者特质」已勾选，但该殖民者仍被归入**其他**。

## 2. 已完成的排查结论（只读证据）

已排除的方向：

| 假设 | 结论 |
| --- | --- |
| 纯逻辑 `EvaluateRoleGroupCore` 对 Brawler 判定顺序错误 | 排除。Brawler 判定位于前排分支，先于射手/工人/贸易 |
| 设置项 `groupBrawlerTraitFront` 默认值/存档读写出错 | 排除。默认 true；`Mod_AutoEverything_AutoEverythingMod.xml` 中该键未写出即等于默认 true |
| `RoleGroupRules.FromSettings()` 字段错位 | 排除。逐字段映射正确 |
| UI 复选框数组与写回索引错位 | 排除。`front[1] ↔ groupBrawlerTraitFront` 一致 |
| `TraitDefOf.Brawler` 不存在或多 Degree 变体 | 排除。单 Degree，`HasTrait(TraitDefOf.Brawler)` 正确 |
| 分组框标签/`groupRoles` 映射/`AssignGroupIds` 切分 | 排除。映射与切分逻辑正常 |
| 存档中确实存在带 Brawler 的殖民者 | 已确认。`Autosave-3.rws` 中 4 个 pawn 满足 `story/traits/allTraits/li/def = Brawler` |

**定位到的真实代码缺陷：**

[PawnRole.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/RoleEvaluation/PawnRole.cs) 中 `RoleDetector.DetectRoleGroup` 的候选门槛为：

```csharp
bool isBarColonist = pawn.IsColonist || pawn.IsColonySubhumanPlayerControlled;
```

而反编译 `Assembly-CSharp.dll` 确认：

```csharp
public bool IsColonist =>
    Faction != null && Faction.IsPlayer && RaceProps.Humanlike
    && (!IsSlave || Guest?.SlaveIsSecure == true)   // 奴隶必须"已稳固"才算 IsColonist
    && !IsSubhuman;
```

其中 `SlaveIsSecure` 要求 `guest.hostFaction == Faction.OfPlayer` 且 `slaveFaction == null`。

存档 `Autosave-3.rws` 中这些 Brawler 殖民者的 `guest` 节点为：

```
<guest><hostFaction>null</hostFaction><slaveFaction>null</slaveFaction>
       <joinStatus>JoinAsSlave</joinStatus>...</guest>
```

`hostFaction = null` ⇒ `SlaveIsSecure = false` ⇒ `IsColonist = false` ⇒ 候选门槛直接返回 `RoleGroup.Other`，**根本走不到 Brawler 判定分支**。这与「有格斗者特质仍被分到其他」的现象完全吻合。

附带缺陷（同源）：`IsColonySubhumanPlayerControlled` 要求 `Spawned`，远行队中/未生成的玩家侧食尸鬼也会落到 `Other`；而 `IsColonist` 明确排除 `IsSubhuman`。

同时，现有代码注释与 README 均声称分组「含奴隶」，但实现并不支持——文档与代码不一致。

## 3. 修复方案

### 3.1 代码改动（唯一文件：`Source/AutoEverything/RoleEvaluation/PawnRole.cs`）

**(a) 扩展纯函数签名**（`RoleGroupRules`）：

```csharp
/// <summary>
/// 参与角色分组的资格判断（纯函数，便于单元测试）。
/// 只要属于玩家侧、会出现在殖民者栏的人形即参与分组：
/// 自由殖民者 / 玩家奴隶 / 玩家控制的亚人（食尸鬼）。
/// 不能用 Pawn.IsColonist：它对"未稳固"的奴隶返回 false，
/// 会让带格斗者特质的奴隶直接落到"其他"。
/// </summary>
public static bool IsRoleGroupCandidate(
    bool isColonist, bool isSlaveOfColony, bool isSubhumanControlled,
    bool hasSkills, bool hasStory)
{
    return (isColonist || isSlaveOfColony || isSubhumanControlled)
        && hasSkills && hasStory;
}
```

**(b) 修改调用点** `RoleDetector.DetectRoleGroup`：

```csharp
bool isColonist = pawn.IsColonist;
bool isSlave = pawn.IsSlaveOfColony;
bool isSubhuman = pawn.IsColonySubhumanPlayerControlled;
if (!RoleGroupRules.IsRoleGroupCandidate(isColonist, isSlave, isSubhuman,
        pawn.skills != null, pawn.story != null))
    return RoleGroup.Other;
```

保留原有的「食尸鬼优先」前置分支（`rules.GhoulToFront && DLCCompat.IsGhoul(pawn)`）不变，使远行队/未生成的食尸鬼仍能进前排。

**(c) 增加一次性诊断日志（便于玩家自查，遵守日志规范）**

在 `DetectRoleGroup` 内、仅在 `AESettings.debugLog` 为真时输出一行（`AEDebug.Log(() => ...)` 延迟构造 + `Log.ErrorOnce` 不适用，此处为普通调试日志）：

```
[AutoEverything][RoleGroup] {name}: colonist={0} slave={1} subhuman={2} brawler={3} -> {group}
```

用途：玩家/我们可凭日志确认某 pawn 为何归组，避免再次盲猜。

### 3.2 测试（`Test/AutoEverything.Tests/RoleGroupTests.cs`）

新增用例：

1. `IsRoleGroupCandidate` 真值表：
   - 自由殖民者（true/false/false, true, true）→ true
   - **玩家奴隶（false/true/false, true, true）→ true**（当前 bug 的直接回归测试）
   - 玩家亚人（false/false/true, true, true）→ true
   - 三者皆 false → false
   - 缺 skills 或缺 story → false
2. 「奴隶 + 格斗者特质」端到端：`EvaluateRoleGroupCore` 输入 `HasBrawlerTrait = true`、规则 `BrawlerTraitToFront = true` → `RoleGroup.Front`。
3. 回归：`BrawlerTraitToFront = false` 时，带 Brawler 特质者不得进前排（落入其他/后续条件）。

## 4. 验证步骤

1. `make check`（`-warnaserror`，零错误零警告）。
2. `make test`（全部用例通过，含新增回归用例）。
3. 文档一致性：检查 README 中「角色分组含奴隶」的表述；若代码修改后已与文档一致，则**不改文档**；若文档描述的是别处行为，则同步修正。
4. 游戏内验证（人工）：载入该存档，确认带 Brawler 特质的奴隶/殖民者进入「前排」分组框；开启调试日志可看到 `colonist= False slave= True ... -> Front`。

## 5. 明确不做的事

- 不修改 `Role`（ITab 徽章）与狩猎/护甲判定所用的 `DetectRole`，本次只修殖民者栏分组入口。
- 不新增设置项、不改存档格式（`RoleGroupRules` 字段集不变）。
- 不扩展「坚韧（Tough）图标 → 前排」等新白名单语义（用户未要求）。
