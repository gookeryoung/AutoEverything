# 评级规则扩展：新增 SS / SSS 档次

## Summary

在现有 S 档之上新增 **SS** 与 **SSS** 两个更高档次，按三大维度（乱开枪系列 / 坚韧格斗系列 / 工作狂神经质系列）细分判定，并把"有负面特质直接判 D"改为"降一档"。同步更新前缀解析、UI 颜色/FloatMenu、搬运优先级、翻译、README。

## Current State Analysis

已完成（前序会话）：
- [CombatTier.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/Core/CombatTier.cs) 枚举已扩展为 8 档：`X=0, D=1, C=2, B=3, A=4, S=5, SS=6, SSS=7`
- [CombatEvaluator.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/RoleEvaluation/CombatEvaluator.cs) `tierRepresentativeScore` 数组已扩展到 8 项（SS=95f, SSS=110f）

待修复的硬编码陷阱（已识别）：
1. `GetAutoCombatTier`（L234-331）仍是旧逻辑：直接 `return CombatTier.D` 给负面特质，三大维度未实现
2. `StripTierTagPrefixFromLabel`（L409-419）只识别单字母前缀，SS/SSS 会让 Nick 永久错乱
3. [AESettings.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/Core/AESettings.cs) `HasTierTagPrefix`（L390-396）+ `StripTierTagPrefix`（L401-408）硬编码 `Substring(2)`，同上问题
4. [ITab_GearManager.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/UI/ITab_GearManager.cs) `GetTierColor` switch 缺 SS/SSS case（落入 default=X 红色）；FloatMenu 循环上限 `(int)CombatTier.S`（L286）漏 SS/SSS
5. [WorkAllocator.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/AutoWork/WorkAllocator.cs) 搬运 switch（L430-436）缺 SS/SSS case（落入 default=3，应为 4）
6. 翻译缺 `AE_Tier_SS` / `AE_Tier_SSS`（中英文）
7. README 评级表、代表分、FloatMenu 说明、排序说明需更新

关键既有字段（CombatEvaluator.cs 已存在，复用即可）：
- `shootingAccuracyDef`（乱开枪，degree=-1）
- `toughDef`（坚韧）
- `nimbleDef`（敏捷，已查询）
- `bloodlustDef`（嗜血，已查询，本规则未用但保留）
- `industriousnessDef`（勤奋 degree=2 / 怠惰 degree=-1 / 工作懒惰 degree=-2）
- `neuroticDef`（严重神经质 degree=2）
- `beautyDef`（沉鱼落雁 degree=2）
- 负面特质查询方法 `HasNegativeTrait`（纵火狂/脑子慢/脆弱/工作懒惰/工作怠惰）

待新增字段：`brawlerDef`（格斗者 = `TraitDefOf.Brawler`，原生 DefOf 无需 null 检查，参考 [PawnRole.cs:111](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/RoleEvaluation/PawnRole.cs#L111) 与 [WeaponTraitScorer.cs:47](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/AutoEquipment/Scoring/Weapon/WeaponTraitScorer.cs#L47)）

## Assumptions & Decisions

**术语对齐（已与用户确认）：**
- 单火 = `Passion.Minor`（单火焰图标）
- 双火 = `Passion.Major`（双火焰图标）
- 有火 = Minor 或 Major（任一兴趣）
- 工作狂 = 勤奋 `Industriousness degree=2`
- 工作狂+严重神经质 = **AND**（同时拥有两者，缺一不可）
- 格斗者 = `TraitDefOf.Brawler`（原生特质）
- 敏捷 = `Nimble` 特质（已有 `nimbleDef`）

**降档规则替代 D 档直接判定：**
- 旧规则：有负面特质 → 直接 `return D`
- 新规则：有负面特质 且 `tier > D` → `tier - 1`（最后统一降档）
- 降档不低于 D（D 不再降）
- X 档（禁暴力）先于一切判定，不受降档影响
- 负面特质范围（4 类）：脑子慢 SlowLearner / 纵火狂 Pyromaniac / 脆弱 Wimp / 工作懒惰或怠惰 Industriousness degree=-1/-2

**三大维度取最高档（MaxTier），不互斥：**
- 维度1（乱开枪系列）：triggerHappy + shooting
- 维度2（坚韧格斗系列）：tough + melee
- 维度3（工作狂神经质系列）：industrious2 AND neurotic2 + workMajors

**原 S 条件 4（特殊天赋）/5（沉鱼落雁+社交双火）保留为 S 档**（不升级）。

**A/B 判定仅在三大维度+原S条件均未触达（tier==C）时进行。**

**纹理兜底：** `LoadTierBadgeTextures` 已用 `Enum.GetValues` 自动遍历，但 `Tier_SS.png` / `Tier_SSS.png` 不存在 → `TryGetValue` 失败 → 回退纯色块（按规则可接受，不强制创建 PNG）。

## Proposed Changes

### 1. [CombatEvaluator.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/RoleEvaluation/CombatEvaluator.cs)

#### 1.1 新增 `brawlerDef` 字段（L42 后，与 `bloodlustDef` 并列）
```csharp
// 格斗者 Brawler：原生 DefOf，始终存在（与 PawnRole/WeaponTraitScorer 一致直接引用）
// 用于 SSS 条件：坚韧+格斗双火+敏捷或格斗者
private static readonly TraitDef brawlerDef = TraitDefOf.Brawler;
```

#### 1.2 新增 `MaxTier` 辅助方法（放在 `IsPassion` 后）
```csharp
/// <summary>
/// 返回两档中较高的一档（枚举值大的更高：SSS>SS>S>A>B>C>D>X）。
/// 用于三大维度取最高档，避免互斥 if-else。
/// </summary>
private static CombatTier MaxTier(CombatTier a, CombatTier b)
{
    return (int)a >= (int)b ? a : b;
}
```

#### 1.3 新增 `CountWorkMajors` 辅助方法（放在 `CountPassions` 后）
```csharp
/// <summary>
/// 统计 6 大专业工作技能的双火（Major）数量。
/// 用于工作狂+神经质系列的 S/SS/SSS 判定。
/// </summary>
private static int CountWorkMajors(Pawn pawn)
{
    int count = 0;
    if (IsPassion(pawn, SkillDefOf.Crafting, Passion.Major)) count++;
    if (IsPassion(pawn, SkillDefOf.Construction, Passion.Major)) count++;
    if (IsPassion(pawn, SkillDefOf.Artistic, Passion.Major)) count++;
    if (IsPassion(pawn, SkillDefOf.Cooking, Passion.Major)) count++;
    if (IsPassion(pawn, SkillDefOf.Plants, Passion.Major)) count++;
    if (IsPassion(pawn, SkillDefOf.Mining, Passion.Major)) count++;
    return count;
}
```

#### 1.4 重写 `GetAutoCombatTier`（替换 L234-331 整段）

执行顺序：
1. X 检查（禁暴力）→ return X
2. skills==null → return X
3. 统计 majors/minors、技能兴趣布尔、特质布尔
4. `tier = C`（默认）
5. 三大维度取 MaxTier：
   - **维度1（乱开枪系列）**：
     - `triggerHappy && shootingMajor && tough` → SSS
     - else `triggerHappy && shootingMajor` → SS
     - else `triggerHappy && shootingMinor` → S
   - **维度2（坚韧格斗系列）**：
     - `tough && meleeMajor && (nimble || brawler)` → SSS
     - else `tough && meleeMajor` → SS
     - else `tough && meleeAny(Minor||Major)` → S
   - **维度3（工作狂神经质系列）**：
     - `industrious2 && neurotic2 && workMajors >= 3` → SSS
     - else `industrious2 && neurotic2 && workMajors >= 2` → SS
     - else `industrious2 && neurotic2 && workMajors >= 1` → S
6. 原 S 条件 4：`HasSpecialTalentTrait` → tier = MaxTier(tier, S)
7. 原 S 条件 5：`beauty2 && socialMajor` → tier = MaxTier(tier, S)
8. 若 `tier == C`：判 A/B（majorCount>=2 && sum>=3 → A；majorCount>=1 && sum>=3 → B）
9. **降档**：`HasNegativeTrait(pawn) && tier > D` → `tier - 1`
10. return tier

注意：维度 1/2/3 的 tier 用 `MaxTier` 累积到结果变量，避免互斥 return 提前退出。原 S 条件 4/5 也用 `MaxTier(tier, S)`。

#### 1.5 重写 `StripTierTagPrefixFromLabel`（替换 L409-419）

支持多字母前缀（SS#/SSS#），与 AESettings 同语义：
```csharp
private static string StripTierTagPrefixFromLabel(string label)
{
    if (string.IsNullOrEmpty(label)) return label;
    int hashIdx = label.IndexOf('#');
    if (hashIdx <= 0 || hashIdx > 3) return label;
    string prefix = label.Substring(0, hashIdx);
    return System.Enum.TryParse(prefix, out CombatTier _) 
        ? label.Substring(hashIdx + 1) 
        : label;
}
```

`hashIdx > 3` 防止误把超长前缀当评级（最长前缀 SSS=3 字符）。

### 2. [AESettings.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/Core/AESettings.cs)

#### 2.1 重写 `HasTierTagPrefix`（L390-396）

```csharp
private static bool HasTierTagPrefix(string nick)
{
    if (string.IsNullOrEmpty(nick)) return false;
    int hashIdx = nick.IndexOf('#');
    if (hashIdx <= 0 || hashIdx > 3) return false;
    string prefix = nick.Substring(0, hashIdx);
    return System.Enum.TryParse(prefix, out CombatTier _);
}
```

#### 2.2 重写 `StripTierTagPrefix`（L401-408）

```csharp
private static string StripTierTagPrefix(string nick)
{
    if (string.IsNullOrEmpty(nick)) return string.Empty;
    if (!HasTierTagPrefix(nick)) return nick;
    int hashIdx = nick.IndexOf('#');
    return nick.Substring(hashIdx + 1);
}
```

### 3. [ITab_GearManager.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/UI/ITab_GearManager.cs)

#### 3.1 `GetTierColor` 新增 SS/SSS case（L879-890）

```csharp
private Color GetTierColor(CombatTier tier)
{
    switch (tier)
    {
        case CombatTier.SSS: return new Color(1.0f, 0.93f, 0.55f); // 钻金（最高档，比金更亮）
        case CombatTier.SS:  return new Color(1.0f, 0.75f, 0.20f); // 亮金橙（次高档）
        case CombatTier.S:   return new Color(1.0f, 0.84f, 0.0f);  // 金
        case CombatTier.A:   return new Color(0.61f, 0.35f, 0.71f); // 紫
        case CombatTier.B:   return new Color(0.2f, 0.6f, 0.85f);   // 蓝
        case CombatTier.C:   return new Color(0.18f, 0.8f, 0.44f);  // 绿
        case CombatTier.D:   return new Color(0.58f, 0.65f, 0.65f); // 灰
        default:             return new Color(0.85f, 0.2f, 0.2f);   // 红（X）
    }
}
```

#### 3.2 FloatMenu 循环上限改 SSS（L286）

```csharp
for (int t = (int)CombatTier.SSS; t >= (int)CombatTier.X; t--)
```

注释同步改为"倒序展示：SSS 在最上"。

### 4. [WorkAllocator.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/AutoWork/WorkAllocator.cs)

#### 4.1 搬运 switch 新增 SS/SSS case（L430-436）

```csharp
switch (tier)
{
    case CombatTier.SSS:
    case CombatTier.SS:
    case CombatTier.S: priority = 4; break;
    case CombatTier.D:
    case CombatTier.X: priority = 1; break;
    default: priority = 3; break; // A/B/C
}
```

### 5. 翻译 XML（中英文同步）

#### 5.1 [ChineseSimplified/Keyed/AE_Keyed.xml](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Languages/ChineseSimplified/Keyed/AE_Keyed.xml)

在 `<AE_Tier_S>` 行后新增：
```xml
<AE_Tier_SS>SS：乱开枪+射击双火 / 坚韧+格斗双火 / 工作狂+神经质+2专业双火</AE_Tier_SS>
<AE_Tier_SSS>SSS：乱开枪+坚韧+射击双火 / 坚韧+格斗双火+敏捷或格斗者 / 工作狂+神经质+3专业双火</AE_Tier_SSS>
```

同步更新 `<AE_Tier_S>` 文案为：`S：乱开枪+射击单火 / 坚韧+格斗有火 / 工作狂+神经质+1专业双火 / 特殊天赋 / 沉鱼落雁+社交双火`

#### 5.2 [English/Keyed/AE_Keyed.xml](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Languages/English/Keyed/AE_Keyed.xml)

在 `<AE_Tier_S>` 行后新增：
```xml
<AE_Tier_SS>SS: TriggerHappy+Shooting Major / Tough+Melee Major / Industrious+Neurotic+2 work Majors</AE_Tier_SS>
<AE_Tier_SSS>SSS: TriggerHappy+Tough+Shooting Major / Tough+Melee Major+Nimble/Brawler / Industrious+Neurotic+3 work Majors</AE_Tier_SSS>
```

同步更新 `<AE_Tier_S>` 文案为：`S: TriggerHappy+Shooting Minor / Tough+Melee passion / Industrious+Neurotic+1 work Major / Special talent / Beauty+Social Major`

### 6. [README.md](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/README.md)

#### 6.1 L207 "离散化为 6 档" → "离散化为 8 档"

#### 6.2 评级表（L211-218）替换为新表

| 档次 | 判定条件（任一满足即归此档） | 说明 |
|------|------------------------------|------|
| **SSS** | 1. 乱开枪（ShootingAccuracy degree=-1）+ 坚韧（Tough）+ 射击双火<br>2. 坚韧（Tough）+ 格斗双火 + 敏捷（Nimble）或格斗者（Brawler）<br>3. 勤奋（Industriousness degree=2）且严重神经质（Neurotic degree=2）+ 3 个专业工作双火 | 顶级组合 |
| **SS** | 1. 乱开枪 + 射击双火<br>2. 坚韧 + 格斗双火<br>3. 勤奋且严重神经质 + 2 个专业工作双火 | 强化组合 |
| **S** | 1. 乱开枪 + 射击单火<br>2. 坚韧 + 格斗有火（Minor 或 Major）<br>3. 勤奋且严重神经质 + 1 个专业工作双火<br>4. 拥有任一特殊天赋特质：博闻强识（TooSmart）/开心果（Joyous）/极致体能（BodyMastery）/痴迷虚空（VoidFascination）/神秘学者（Occultist）/怪诞不经（Disturbing）<br>5. 沉鱼落雁（Beauty degree=2）+ 社交双火 | 全局高价值 |
| **A** | 不满足以上，但所有 9 大兴趣技能中至少 2 个双 Major + 1 个单 Minor 以上 | 多面手高价值 |
| **B** | 不满足以上，但所有 9 大兴趣技能中至少 1 个双 Major + 2 个单 Minor 以上 | 中等价值 |
| **C** | 其他情况（无特殊组合、未触达负面特质降档） | 普通价值 |
| **D** | 拥有任一负面特质且原档 > D 时降一档：纵火狂（Pyromaniac）/脑子慢（SlowLearner）/脆弱（Wimp）/工作懒惰（Industriousness degree=-1）/工作怠惰（Industriousness degree=-2） | 低价值（降档） |
| **X** | `WorkTagIsDisabled(WorkTags.Violent)` | 无法从事暴力活动（医疗/未成年等） |

新增"专业工作技能"说明行：
> **专业工作技能（用于工作狂神经质系列判定）：** 手工、建造、艺术、烹饪、种植、采矿（共 6 项，统计 Major 数量）。
> **降档规则：** 三大维度与原 S 条件 4/5 计算出 tier 后，若拥有任一负面特质且 `tier > D`，则 `tier` 降一档（D 不再降，X 先于一切判定不受影响）。

#### 6.3 L249 代表分更新

`D=5, C=15, B=25, A=50, S=80` → `D=5, C=15, B=25, A=50, S=80, SS=95, SSS=110`

#### 6.4 L259 FloatMenu 说明

`弹出 S/A/B/C/D/X 选项 FloatMenu` → `弹出 SSS/SS/S/A/B/C/D/X 选项 FloatMenu`

#### 6.5 L266 排序规则代表分

`D=5, C=15, B=25, A=50, S=80, X=-1` → `D=5, C=15, B=25, A=50, S=80, SS=95, SSS=110, X=-1`

#### 6.6 L290 排序模式说明

`S→A→B→C→D→X` → `SSS→SS→S→A→B→C→D→X`

#### 6.7 L363 搬运优先级

`搬运：S 档 = 4，A/B/C 档 = 3，D/X 档 = 1` → `搬运：SSS/SS/S 档 = 4，A/B/C 档 = 3，D/X 档 = 1`

### 7. 编译验证

执行 `make check`，必须 0 警告 0 错误。

## Implementation Steps

1. CombatEvaluator.cs：新增 `brawlerDef` 字段 + `MaxTier` + `CountWorkMajors` 辅助方法
2. CombatEvaluator.cs：重写 `GetAutoCombatTier`（三大维度 MaxTier + 原S条件4/5 + A/B + 降档）
3. CombatEvaluator.cs：重写 `StripTierTagPrefixFromLabel`（多字母前缀）
4. AESettings.cs：重写 `HasTierTagPrefix` + `StripTierTagPrefix`（多字母前缀）
5. ITab_GearManager.cs：`GetTierColor` 新增 SS/SSS case + FloatMenu 循环上限改 SSS
6. WorkAllocator.cs：搬运 switch 新增 SS/SSS case
7. 翻译 XML：中英文新增 `AE_Tier_SS` / `AE_Tier_SSS`，更新 `AE_Tier_S` 文案
8. README.md：评级表 + 代表分 + FloatMenu 说明 + 排序说明 + 搬运优先级
9. `make check` 验证

## Verification

- `make check` 通过（0 警告 0 错误）
- 代码扫描确认所有 `case CombatTier.S:` 是否需要 SS/SSS（已覆盖 ITab/WorkAllocator；CombatEvaluator 评级逻辑不依赖 switch）
- 翻译完整性：中英文均含 `AE_Tier_SS` / `AE_Tier_SSS`
- 前缀解析：`S#王五` / `SS#王五` / `SSS#王五` 均能正确剥离与识别（不会把 `SS#王五` 误判为 S + `#王五` 残留）
- 降档规则：X 不降、D 不降、C/B/A/S/SS/SSS 在有负面特质时各降一档
