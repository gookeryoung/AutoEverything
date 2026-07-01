# 文化武器偏好评分（Request G）

## 背景与意图

用户反馈：武器装备的选择需要考虑**文化偏好**，包括近战/远程、利器/钝器、极致科技/石器时代科技等维度。

RimWorld Ideology DLC 原生提供 `Precept_Weapon` 机制，通过 `WeaponClassDef`（如 Melee/Ranged/Neolithic/Ultratech/MeleePiercer/MeleeBlunt 等）描述武器分类，MemeDef 的 `preferredWeaponClasses` 在创建文化时自动注入到 `Precept_Weapon.noble` / `despised` 列表。文化对武器有三档态度：Noble（尊崇）/ Despised（鄙夷）/ None（无态度）。

### 用户决策

- **利器/钝器维度**：跳过（RimWorld 原生不支持该维度偏好，武器分类虽有 `MeleePiercer`/`MeleeBlunt` 但文化不会主动选择）
- **鄙夷武器处理**：大额负分惩罚（-300），**不用 Veto**（避免完全禁止玩家选择，且与"近战武器 Veto"等硬约束语义区分）

### 已验证 API（反射）

- `Precept_Weapon` 类：`GetDispositionForWeapon(ThingDef weaponDef)` → `IdeoWeaponDisposition`
- `IdeoWeaponDisposition` 枚举：None=0, Noble=1, Despised=2
- `Ideo.GetAllPreceptsOfType<Precept_Weapon>()` 遍历文化下所有武器戒律
- 原生 `WeaponClassDef`：Melee/Ranged/Neolithic/Ultratech/MeleePiercer/MeleeBlunt 等

## 当前状态分析

### WeaponIdeologyScorer（已损坏）

文件：`Source/AutoEverything/AutoEquipment/Scoring/Weapon/WeaponIdeologyScorer.cs`

当前实现存在严重缺陷：

1. **错误识别戒律**：用 `precept.def.defName.Contains("Weapon"/"Melee"/"Ranged")` 判断武器戒律，但实际 `Precept_Weapon` 的 defName 为 `NobleDespisedWeapons`（单一 Def，不含 "Melee"/"Ranged"）
2. **未用原生 API**：未调用 `Precept_Weapon.GetDispositionForWeapon`，无法识别具体武器是 Noble 还是 Despised
3. **硬编码分值**：`±30f` 不读 `GearWeights`，无法随预设方案调整
4. **无 DLC 守卫**：未检查 `ModsConfig.IdeologyActive`，未加载 DLC 时 `pawn.Ideo` 为 null 虽不报错但语义不清晰
5. **无 try-catch**：违反 Tick 路径异常隔离规则

### DLCCompat 缺失 Ideology 守卫

文件：`Source/AutoEverything/Core/DLCCompat.cs`

现有模式：`Anomaly`/`Biotech` 静态 bool 字段 + `IsGhoul`/`IsSlave`/`IsChild` 方法（均含 try-catch + `Log.ErrorOnce`）。但**缺失 Ideology 守卫**与武器戒律查询方法。

### GearWeights 缺失意识形态权重

文件：`Source/AutoEverything/AutoEquipment/Scoring/GearWeights.cs`

现有 4 个预设（Standard/Aggressive/Economic/Hunting）均无意识形态相关字段。需要新增 `w_ideology_noble` / `w_ideology_despised` 两个字段并同步 4 个预设。

### README 文档

文件：`README.md`

需同步章节：
- `## 评分模型 → 权重预设方案` 表格（13 → 15 字段）
- `## 评分模型 → 武器评分管线` 表格（第 9 项 WeaponIdeologyScorer 说明）
- `## 总分公式` 说明意识形态惩罚

## 提议变更

### 变更 1：DLCCompat 新增 Ideology 守卫与武器戒律查询

**文件**：`Source/AutoEverything/Core/DLCCompat.cs`

**Why**：集中包装 Ideology DLC API，避免未加载 DLC 时直接调用 `Precept_Weapon` 导致 `TypeLoadException`；遵循现有 `IsGhoul`/`IsSlave`/`IsChild` 的 try-catch + `Log.ErrorOnce` 模式。

**How**：在 `DLCCompat` 类中新增：
- `private static readonly bool Ideology = ModsConfig.IdeologyActive;` 字段
- `private const int IdeologyErrorIdBase = 0xA400;` 错误去重 ID
- `GetWeaponDisposition(Pawn pawn, ThingDef weaponDef)` 方法，返回 `int`（0=None, 1=Noble, 2=Despised）

**代码**：

```csharp
private static readonly bool Ideology = ModsConfig.IdeologyActive;
private const int IdeologyErrorIdBase = 0xA400;

/// <summary>
/// 查询文化对武器的态度（需 Ideology DLC）。
/// 返回值：0=None, 1=Noble（尊崇）, 2=Despised（鄙夷）。
/// 未加载 DLC / Pawn 无 Ideo / 查询失败时返回 0。
/// </summary>
public static int GetWeaponDisposition(Pawn pawn, ThingDef weaponDef)
{
    if (!Ideology || pawn == null || weaponDef == null) return 0;
    if (pawn.Ideo == null) return 0;
    try
    {
        // 遍历所有武器戒律，取首个非 None 态度
        // 多个 Precept_Weapon 通常不会对同一武器给出冲突态度，取首个即可
        var precepts = pawn.Ideo.GetAllPreceptsOfType<Precept_Weapon>();
        for (int i = 0; i < precepts.Count; i++)
        {
            var disposition = precepts[i].GetDispositionForWeapon(weaponDef);
            if (disposition != IdeoWeaponDisposition.None)
                return (int)disposition;
        }
        return 0;
    }
    catch (Exception ex)
    {
        Log.ErrorOnce("[AutoEverything] DLCCompat.GetWeaponDisposition 异常: " + ex.Message,
            (pawn.thingIDNumber ^ weaponDef.shortHash ^ IdeologyErrorIdBase));
        return 0;
    }
}
```

**说明**：用 `int` 而非 `IdeoWeaponDisposition` 作为返回类型，避免在 DLCCompat 公开类型泄漏 DLC 依赖（调用方用 `0/1/2` 比较，无需 `using RimWorld` 中的 `IdeoWeaponDisposition`）。

### 变更 2：WeaponIdeologyScorer 完全重写

**文件**：`Source/AutoEverything/AutoEquipment/Scoring/Weapon/WeaponIdeologyScorer.cs`

**Why**：当前实现因错误识别戒律完全失效。重写后使用原生 `GetDispositionForWeapon` API，正确识别 Noble/Despised 态度。

**How**：
1. 调用 `DLCCompat.GetWeaponDisposition(pawn, gear.def)` 获取态度（0/1/2）
2. `Noble (1)` → `breakdown.AddScore(Name, ..., +weights.w_ideology_noble)`
3. `Despised (2)` → `breakdown.AddScore(Name, ..., -weights.w_ideology_despised)`
4. `None (0)` → 直接返回，不加分

**代码**：

```csharp
using RimWorld;
using Verse;
using AutoEverything.Core;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.AutoEquipment.Scoring.Weapon
{
    /// <summary>
    /// 武器意识形态评分：检查文化戒律对武器的偏好（Noble）或鄙夷（Despised）。
    /// Noble 加 w_ideology_noble 分；Despised 减 w_ideology_despised 分（大额负分，不用 Veto）。
    /// 需 Ideology DLC；未加载或 Pawn 无 Ideo 时跳过。
    /// </summary>
    public class WeaponIdeologyScorer : IScorer<Thing>
    {
        public string Name => "意识形态";

        public void Score(Pawn pawn, Thing gear, Role role, GearContext context,
                          GearWeights weights, ScoreBreakdown breakdown)
        {
            // DLCCompat 内部已守卫 ModsConfig.IdeologyActive 与 pawn.Ideo==null
            int disposition = DLCCompat.GetWeaponDisposition(pawn, gear.def);
            if (disposition == 0) return;  // None：无态度，跳过

            if (disposition == 1)  // Noble：尊崇
            {
                breakdown.AddScore(Name,
                    breakdown.CollectItems ? "文化尊崇" : null,
                    weights.w_ideology_noble);
            }
            else  // disposition == 2, Despised：鄙夷
            {
                breakdown.AddScore(Name,
                    breakdown.CollectItems ? "文化鄙夷" : null,
                    -weights.w_ideology_despised);
            }
        }
    }
}
```

**关键决策**：
- **Despised 用大额负分而非 Veto**：与"非 Brawler 角色 Veto 近战武器"等硬约束区分；文化鄙夷只是强烈负面偏好，玩家应保留选择权（如战利品中只有该武器时仍可拾取过渡）
- **`-w_ideology_despised` 默认 -300**：足以让鄙夷武器在评分中显著落后，但不超过 Veto 分（-9000）

### 变更 3：GearWeights 新增意识形态权重字段

**文件**：`Source/AutoEverything/AutoEquipment/Scoring/GearWeights.cs`

**Why**：让意识形态评分可随预设方案调整（如 Aggressive 方案对文化鄙夷更严格），符合现有权重数据化模式。

**How**：
1. 在 `// ===================== 武器权重 =====================` 区域末尾新增两个字段
2. 在 4 个预设中同步设置

**字段新增**（在 `w_quality` 后）：

```csharp
/// <summary>文化尊崇武器加分</summary>
public float w_ideology_noble;

/// <summary>文化鄙夷武器惩罚（取负值）</summary>
public float w_ideology_despised;
```

**4 个预设取值**：

| 预设 | w_ideology_noble | w_ideology_despised | 设计意图 |
|------|------------------|---------------------|----------|
| Standard | 200 | 300 | 标准偏好，Noble 与技能双火同量级（200 ≈ 技能 12 × 4.0 × 2.0 = 96 的 2 倍），Despised 惩罚足以让鄙夷武器显著落后 |
| Aggressive | 250 | 400 | 激战方案更看重文化契合（战斗增益），鄙夷惩罚更严 |
| Economic | 150 | 200 | 经济方案弱化文化偏好，避免因文化放弃高耐久装备 |
| Hunting | 200 | 300 | 狩猎方案与标准一致 |

**Standard 预设示例**（仅展示新增字段，其余不变）：

```csharp
public static GearWeights Standard => new GearWeights
{
    // ... 既有字段 ...
    w_quality = 10.0f,
    w_ideology_noble = 200.0f,
    w_ideology_despised = 300.0f,
    // ... 防具字段 ...
    upgradeThreshold = 0.15f
};
```

### 变更 4：README 同步

**文件**：`README.md`

**4a. 权重预设方案表**：在权重字段说明中补充意识形态字段

将原文：
> `GearWeights` 结构包含 13 个权重字段。提供 4 个预设方案：

改为：
> `GearWeights` 结构包含 15 个权重字段（含意识形态尊崇/鄙夷）。提供 4 个预设方案：

在权重预设方案表格下方新增意识形态字段说明段落：

```markdown
**意识形态权重**：
- `w_ideology_noble`：文化尊崇武器加分（Standard=200, Aggressive=250, Economic=150, Hunting=200）
- `w_ideology_despised`：文化鄙夷武器惩罚（取负值，Standard=300, Aggressive=400, Economic=200, Hunting=300）

鄙夷武器使用大额负分（-300）而非 Veto，与硬约束（如非 Brawler Veto 近战）语义区分：文化鄙夷是强烈负面偏好，玩家应保留选择权（如战利品中只有该武器时仍可拾取过渡）。
```

**4b. 武器评分管线表**：更新第 9 项说明

将原文：
```
| 9 | `WeaponIdeologyScorer` | 意识形态偏好 |
```

改为：
```
| 9 | `WeaponIdeologyScorer` | 意识形态武器偏好（Noble +w_ideology_noble / Despised -w_ideology_despised，需 Ideology DLC） |
```

**4c. 总分公式说明**：补充意识形态惩罚说明

在 `### 总分公式` 章节末尾追加：

```markdown
**意识形态惩罚**：文化鄙夷（Despised）武器施加 `-w_ideology_despised`（默认 -300）大额负分，但不触发 Veto。与硬约束 Veto（-9000）区分：文化鄙夷是强烈负面偏好，玩家应保留选择权（如战利品中只有该武器时仍可拾取过渡）。文化尊崇（Noble）武器施加 `+w_ideology_noble`（默认 +200）加分。
```

## 假设与决策

1. **跳过利器/钝器维度**：RimWorld 原生 `WeaponClassDef` 虽有 `MeleePiercer`/`MeleeBlunt`，但文化 MemeDef 不会主动选择这些类别，玩家无法在文化编辑器中配置利器/钝器偏好。该维度无原生支持，跳过。

2. **Despised 用负分而非 Veto**：硬约束 Veto（-9000）保留给"非 Brawler 拿近战""护盾腰带拿远程"等机制性冲突；文化鄙夷是偏好问题，用大额负分（-300）让鄙夷武器在评分中显著落后但非完全禁止。

3. **返回 int 而非枚举**：DLCCompat.GetWeaponDisposition 返回 `int`（0/1/2）而非 `IdeoWeaponDisposition`，避免调用方（WeaponIdeologyScorer）依赖 DLC 类型，保持 DLC 兼容层封装性。

4. **取首个非 None 态度**：多个 `Precept_Weapon` 对同一武器通常不会给出冲突态度（Noble+Despised），取首个非 None 即可。极端情况下若冲突，取首个 Noble/Despised 不影响游戏体验。

5. **不修改 ScoringPipelineFactory**：WeaponIdeologyScorer 已在管线第 9 位，顺序无需调整。

## 验证步骤

1. `make check` 编译通过（0 警告 0 错误）
2. 检查 `WeaponIdeologyScorer.cs` using 包含 `AutoEverything.Core`（新增依赖）
3. 检查 `GearWeights.cs` 4 个预设均包含 `w_ideology_noble` / `w_ideology_despised`
4. 检查 `DLCCompat.cs` 新增 `Ideology` 字段、`IdeologyErrorIdBase`、`GetWeaponDisposition` 方法
5. 检查 `README.md` 三处更新：权重预设方案、武器评分管线表、总分公式说明
6. 项目规则同步检查清单：
   - [x] 改了 `GearWeights.cs`？→ README `权重预设方案` 表格已更新
   - [x] 改了任一 Scorer 的加分公式？→ README 对应行说明已更新
   - [x] `make check` 通过
