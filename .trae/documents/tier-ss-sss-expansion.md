# 评级规则扩展：新增 SS 与 SSS 档次

## Context

当前 `CombatTier` 枚举仅 6 档（X/D/C/B/A/S），S 为最高档。用户希望进一步细分顶级价值，在 S 之上新增 SS、SSS 两档，并按"乱开枪系列 / 坚韧格斗系列 / 工作狂神经质系列"三大维度细分判定。同时引入"负面特质降一档"机制替代原"有负面特质直接判 D"。

用户已确认（AskUserQuestion）：
1. 「工作狂」= Industriousness degree=2（勤奋）
2. 「工作狂+严重神经质」= AND（同时拥有两者）
3. 降档规则**替代**原「有负面特质直接判 D」，改为降一档
4. 降档负面特质包括全部 4 类：脑子慢/纵火狂/脆弱/工作懒惰怠惰

## 新评级规则（GetAutoCombatTier）

三大维度任一满足取最高档，互不排斥；最后统一降档。

### 规则 1：乱开枪系列（射击维度）
| 条件 | 档次 |
|------|------|
| 乱开枪(ShootingAccuracy degree=-1) + 射击单火(Minor) | S |
| 乱开枪 + 射击双火(Major) | SS |
| 乱开枪 + 坚韧(Tough) + 射击双火(Major) | SSS |

### 规则 2：坚韧+格斗系列（近战维度）
| 条件 | 档次 |
|------|------|
| 坚韧(Tough) + 格斗单火(Melee Minor) | S |
| 坚韧 + 格斗双火(Melee Major) | SS |
| 坚韧 + 格斗双火 + 敏捷(Nimble) 或 格斗者(Brawler 特质) | SSS |

### 规则 3：工作狂+神经质系列（工作维度）
| 条件 | 档次 |
|------|------|
| 勤奋(Industriousness=2) + 严重神经质(Neurotic=2) + 1 个专业工作双火 | S |
| 同上 + 2 个专业工作双火 | SS |
| 同上 + 3 个专业工作双火 | SSS |

专业工作双火 = Crafting/Construction/Artistic/Cooking/Plants/Mining 任一 Major

### 保留的原 S 条件（仍为 S 档）
- 条件 4：拥有特殊天赋特质（TooSmart/Joyous/BodyMastery/VoidFascination/Occultist/Disturbing）→ S
- 条件 5：沉鱼落雁(Beauty degree=2) + 社交双火(Social Major) → S

### A/B/C 档（仅当未命中任何 S/SS/SSS 时）
- A：≥2 双 Major + ≥1 Minor
- B：≥1 双 Major + ≥2 Minor
- C：其他

### 降档规则（最后统一执行）
- 有负面特质（脑子慢/纵火狂/脆弱/工作懒惰怠惰）→ 基础档降一档
- SSS→SS, SS→S, S→A, A→B, B→C, C→D
- D 不再降（已是最低）
- X 档（禁暴力）不受降档影响（禁暴力直接 X，先于一切）

### 评级逻辑顺序
```
1. 禁暴力 → X（直接返回）
2. skills==null → X
3. 统计技能/特质
4. 三大维度取最高档（SSS/SS/S）+ 原S条件4/5
5. 若仍为 C → 判 A/B
6. 降档：有负面特质 && tier>D → tier-1
7. 返回
```

## Proposed Changes

### 1. `Source/AutoEverything/Core/CombatTier.cs` — 扩展枚举

新增 `SS=6, SSS=7`，保持原值不变（兼容旧存档）。
```csharp
public enum CombatTier : byte
{
    X = 0, D = 1, C = 2, B = 3, A = 4, S = 5, SS = 6, SSS = 7
}
```

### 2. `Source/AutoEverything/RoleEvaluation/CombatEvaluator.cs` — 重写评级逻辑

**A. `tierRepresentativeScore` 数组扩展**（L24-32，否则索引越界崩溃）：
```csharp
private static readonly float[] tierRepresentativeScore = new float[]
{
    -1f,   // X
    5f,    // D
    15f,   // C
    25f,   // B
    50f,   // A
    80f,   // S
    95f,   // SS
    110f   // SSS
};
```

**B. 新增 `brawlerDef` 字段**（近 L40 附近，与 nimbleDef 并列）：
```csharp
// Brawler 在原生 TraitDefOf 中，但为保持查询风格统一用 DefDatabase
private static readonly TraitDef brawlerDef = TraitDefOf.Brawler;
```
注：项目规则"原生 DefOf 始终存在，无需 null 检查"，可直接用 `TraitDefOf.Brawler`。但为与现有 toughDef/nimbleDef 风格一致，用字段缓存。实际用 `TraitDefOf.Brawler` 更安全简洁，无需 null 检查。

**C. 新增辅助方法**：
```csharp
private static CombatTier MaxTier(CombatTier a, CombatTier b)
    => (int)a >= (int)b ? a : b;

// 统计专业工作双火数量（Crafting/Construction/Artistic/Cooking/Plants/Mining）
private static int CountWorkMajors(Pawn pawn)
{
    int n = 0;
    if (IsPassion(pawn, SkillDefOf.Crafting, Passion.Major)) n++;
    if (IsPassion(pawn, SkillDefOf.Construction, Passion.Major)) n++;
    if (IsPassion(pawn, SkillDefOf.Artistic, Passion.Major)) n++;
    if (IsPassion(pawn, SkillDefOf.Cooking, Passion.Major)) n++;
    if (IsPassion(pawn, SkillDefOf.Plants, Passion.Major)) n++;
    if (IsPassion(pawn, SkillDefOf.Mining, Passion.Major)) n++;
    return n;
}
```

**D. 重写 `GetAutoCombatTier`**（L232-329）：
- 新增 `shootingMinor`、`meleeMinor` 局部变量
- 规则 1/2/3 用 `MaxTier` 取最高
- 原 S 条件 4/5 保留（仍为 S）
- A/B 判定仅当 tier==C 时执行
- 最后统一降档：`if (HasNegativeTrait(pawn) && tier > CombatTier.D) tier = (CombatTier)((int)tier - 1);`
- 移除原"有负面特质直接 return D"分支（被降档规则替代）

**E. 重写 `StripTierTagPrefixFromLabel`**（L407-417）支持多字母前缀：
```csharp
private static string StripTierTagPrefixFromLabel(string label)
{
    if (string.IsNullOrEmpty(label)) return label;
    int hashIdx = label.IndexOf('#');
    if (hashIdx <= 0 || hashIdx > 3) return label;
    // 验证前缀是有效 CombatTier 名（SS/SSS/S/A/B/C/D/X）
    string prefix = label.Substring(0, hashIdx);
    if (!System.Enum.TryParse(prefix, out CombatTier _)) return label;
    return label.Substring(hashIdx + 1);
}
```

### 3. `Source/AutoEverything/Core/AESettings.cs` — 重写前缀解析

**A. `HasTierTagPrefix`**（L390-396）支持多字母前缀：
```csharp
private static bool HasTierTagPrefix(string nick)
{
    if (string.IsNullOrEmpty(nick)) return false;
    int hashIdx = nick.IndexOf('#');
    if (hashIdx <= 0 || hashIdx > 3) return false;
    // 验证前缀是有效 CombatTier 名，避免误判 "ABC#名字"
    string prefix = nick.Substring(0, hashIdx);
    return System.Enum.TryParse(prefix, out CombatTier _);
}
```

**B. `StripTierTagPrefix`**（L401-408）按 # 位置动态剥离：
```csharp
private static string StripTierTagPrefix(string nick)
{
    if (!HasTierTagPrefix(nick)) return nick;
    int hashIdx = nick.IndexOf('#');
    return nick.Substring(hashIdx + 1);
}
```

注：这两处是**最危险的硬编码陷阱**——原逻辑假设单字母前缀，SS/SSS 会导致剥离错误、Nick 永久错乱。

### 4. `Source/AutoEverything/UI/ITab_GearManager.cs` — UI 适配

**A. `GetTierColor`**（L879-890）新增 SS/SSS case（金色变体）：
```csharp
case CombatTier.SSS: return new Color(1.0f, 0.95f, 0.6f);   // 白金（最亮）
case CombatTier.SS:  return new Color(1.0f, 0.90f, 0.3f);   // 亮金
case CombatTier.S:   return new Color(1.0f, 0.84f, 0.0f);    // 金（原）
```

**B. 自定义评级 FloatMenu 循环上限**（L286）：
```csharp
// 原: for (int t = (int)CombatTier.S; t >= (int)CombatTier.X; t--)
// 改: for (int t = (int)CombatTier.SSS; t >= (int)CombatTier.X; t--)
```

### 5. `Source/AutoEverything/AutoWork/WorkAllocator.cs` — 搬运优先级

**L432 switch** 新增 SS/SSS case（与 S 同 priority=4）：
```csharp
case CombatTier.SSS:
case CombatTier.SS:
case CombatTier.S: priority = 4; break;
case CombatTier.D:
case CombatTier.X: priority = 1; break;
default: priority = 3; break; // A/B/C
```

### 6. 翻译文件（中英文同步）

**ChineseSimplified/Keyed/AE_Keyed.xml**（L168 后新增）：
```xml
<AE_Tier_SS>SS：乱开枪+坚韧+射击双火 / 坚韧+格斗双火+敏捷或格斗者 / 工作狂+神经质+3专业双火</AE_Tier_SS>
<AE_Tier_SSS>SSS：顶级组合（乱开枪+坚韧+射击双火 / 坚韧+格斗双火+敏捷或格斗者 / 工作狂+神经质+3专业双火）</AE_Tier_SSS>
```

**English/Keyed/AE_Keyed.xml** 对应新增英文翻译。

### 7. 纹理资源（提醒用户添加，代码自动回退纯色块）

需新增 `Textures/UI/Icons/Tier/Tier_SS.png` 与 `Tier_SSS.png`（64×64）。
`LoadTierBadgeTextures` 用 `Enum.GetValues` 自动遍历新枚举，无图时回退纯色块（`DrawBadge` 用 `GetTierColor` 颜色），不崩溃。

### 8. `README.md` — 同步评级章节

- `### 全局价值评级档次（CombatTier）` 表格新增 SS/SSS 行
- 殖民者栏排序说明 `S→A→B→C→D→X` 更新为 `SSS→SS→S→A→B→C→D→X`
- 评级规则描述同步新三大维度 + 降档规则

## Assumptions & Decisions

1. 「工作狂」= Industriousness degree=2（勤奋）—— 用户确认
2. 「工作狂+严重神经质」= AND（同时拥有）—— 用户确认
3. 降档规则替代原「有负面特质直接判 D」—— 用户确认
4. 降档负面特质 = 脑子慢/纵火狂/脆弱/工作懒惰怠惰（原 HasNegativeTrait 全部）—— 用户确认
5. 「敏捷」= Nimble 特质（CombatEvaluator 已有 nimbleDef）
6. 「格斗者」= Brawler 特质（用 TraitDefOf.Brawler，原生 DefOf）
7. 三大维度取最高档（MaxTier），不互斥
8. 原 S 条件 4（特殊天赋）/5（沉鱼落雁+社交双火）保留为 S 档
9. 降档不低于 D（D 不再降）
10. X 档（禁暴力）不受降档影响，先于一切判定
11. tierRepresentativeScore 新值 SS=95, SSS=110（递增，让 SSS 排最前）

## Verification Steps

1. **编译验证**：`make check` 通过（0 警告 0 错误）
2. **前缀解析验证**（关键）：
   - 存档保存 "SS#王五" 后加载，能正确剥离为 "王五"
   - "SSS#李四" 同理
   - 旧存档 "S#王五" 仍能正确剥离（向后兼容）
3. **评级逻辑验证**（游戏内或日志）：
   - 乱开枪+射击双火+坚韧 → SSS
   - 乱开枪+射击双火（无坚韧）→ SS
   - 乱开枪+射击单火 → S
   - 坚韧+格斗双火+Nimble → SSS
   - 坚韧+格斗双火（无 Nimble/Brawler）→ SS
   - 坚韧+格斗单火 → S
   - 勤奋+严重神经质+3专业双火 → SSS
   - 勤奋+严重神经质+2专业双火 → SS
   - 勤奋+严重神经质+1专业双火 → S
   - 仅勤奋（无神经质）→ 不命中规则3
   - 有负面特质 → 降一档（如 SS→S）
   - 有负面特质 + C 档 → D
4. **UI 验证**：自定义评级 FloatMenu 显示 SSS/SS 选项；徽章颜色正确（白金/亮金）
5. **排序验证**：SSS 殖民者排在 SS 之前，SS 排在 S 之前

## 实现步骤顺序

1. 改 `CombatTier.cs` 新增 SS=6, SSS=7
2. 改 `CombatEvaluator.cs`：扩展数组 + 新增 brawlerDef + MaxTier/CountWorkMajors + 重写 GetAutoCombatTier + 重写 StripTierTagPrefixFromLabel
3. 改 `AESettings.cs`：重写 HasTierTagPrefix + StripTierTagPrefix
4. 改 `ITab_GearManager.cs`：GetTierColor 新增 case + FloatMenu 循环上限改 SSS
5. 改 `WorkAllocator.cs`：搬运 switch 新增 SS/SSS case
6. 改翻译 XML（中英文同步）
7. 改 README.md（评级表格 + 排序说明）
8. `make check` 验证
