# 迭代 18：图标统一深红 + 远程判定扩展 + 神秘学者 DarkStudy 优先级 1

## 需求清单

- [x] 图标颜色统一用深红色，避免看不清
- [x] 分析"乱开枪+射击单火"高价值角色未标记为远程的原因，并给出解决方案
- [x] 工作安排规则增加神秘学者判定，神秘学者强制 DarkStudy（暗黑调查）优先级 1

来源：用户反馈「1.图标统一用深红色，避免看不清。2.乱开枪射击带单火的高价值角色为何不标记为远程？请分析原因并给出解决方案。3.工作安排规则增加神秘学者的判定，如果是就安排暗黑调查工作优先级1」。

## 迭代目标

1. 4 种角色定位图标颜色统一深红色，形状区分角色类型（避免多色看不清）
2. 扩展 Ranged 判定到"乱开枪+射击单火"（S 档高价值），原仅"乱开枪+射击双火"（SS/SSS 档）
3. 神秘学者（Occultist 特质，Anomaly DLC）强制 DarkStudy priority=1，覆盖 ResearchConfig 默认优先级，绕过硬上限

## 改动文件清单

| 文件 | 改动 |
|------|------|
| `Source/AutoEverything/AutoMarkPawn/RoleIconDef.cs` | 重写颜色常量与 GetColor 方法；扩展 Ranged 判定含 Minor；新增 IsMinorPassion 辅助方法 |
| `Source/AutoEverything/AutoWork/WorkAllocator.Assignment.cs` | 主循环+非候选清理循环添加神秘学者 DarkStudy priority=1 覆盖；新增 IsDarkStudyWork/IsOccultist 辅助方法 |
| `README.md` | 更新角色定位图标表（统一深红+扩展 Ranged 判定）；工作分配表备注神秘学者覆盖；新增"神秘学者 DarkStudy 优先级覆盖"段落 |

## 关键决策与依据

### 1. 图标颜色统一深红色 RGB(0.6, 0.0, 0.0)

**决策**：移除原 CombatColor（橙）/WorkColor（绿）/TradeColor（粉）三色分组，所有图标统一返回 IconColor 深红色。

**依据**：
- 用户反馈多色在殖民者栏 16×16 小尺寸下看不清
- 4 种图标形状已足够区分（盾/弓箭/锤子铁砧/钱袋），颜色不再做分类
- 深红色 RGB(0.6, 0.0, 0.0) 对比度强，在浅色/深色 UI 背景下均清晰
- 简化 GetColor 方法为单行 `return IconColor;`，消除颜色分支

### 2. Ranged 判定扩展到含射击 Minor

**原因分析**（用户问"乱开枪射击带单火的高价值角色为何不标记为远程"）：

原判定逻辑（RoleIconDef.cs 第 98-99 行）：
```csharp
if (isTriggerHappy && shootingMajor)
    buffer.Add(RoleIconType.Ranged);
```

仅识别"乱开枪+射击 Major（双火）"对应 CombatEvaluator 评级的 SS/SSS 档。

但 CombatEvaluator 评级规则中：
- 乱开枪 + shootingMajor → SS
- 乱开枪 + shootingMinor → S（也是高价值角色）

原 Ranged 判定漏掉了"乱开枪+射击 Minor"S 档高价值角色，导致这类 Pawn 未被标记为远程。

**解决方案**：扩展判定为 `isTriggerHappy && (shootingMajor || shootingMinor)`，覆盖所有"乱开枪+射击有火"的 S+/SS+/SSS+ 高价值远程单位。

**依据**：
- 用户期望"乱开枪+单火"的 S 档高价值角色也标记为远程
- 与 CombatEvaluator 评级规则对齐：乱开枪系列维度中 Minor 对应 S 档
- 新增 IsMinorPassion 辅助方法（与 IsMajorPassion 对称）

### 3. 神秘学者 DarkStudy 优先级覆盖

**决策**：在 AssignWork 主循环与非候选清理循环中，对神秘学者 + DarkStudy 工作类型强制 priority=1，覆盖 ResearchConfig 默认优先级（双火 2/单火 3/无火 0），且绕过硬上限（即使满载 workCount>=3 也承担 DarkStudy）。

**依据**：
- 用户需求"如果是就安排暗黑调查工作优先级1"——神秘学者必须优先发展暗黑调查方向
- 神秘学者天然契合暗黑调查（Occultist 特质设计意图），应优先承担
- 绕过硬上限是用户意图的必然结果：神秘学者即使满载也要做 DarkStudy
- DarkStudy 是最后分配阶段，priority=1 累加 workCount 不影响后续工作分配
- 无 Anomaly DLC 时 TraitDefCache.Occultist 为 null，IsOccultist 安全返回 false

**实现位置**：
- 主循环（candidatePawns 内的候选）：在 ApplySkillFloor 后、SetPriority 前添加覆盖
- 非候选清理循环（被硬上限拦截或 WorkTagIsDisabled 之外的非候选）：在 ApplySkillFloor 后、SetPriority 前添加覆盖
- WorkTagIsDisabled 的 Pawn 仍然跳过（不能做 DarkStudy 的神秘学者不强制）

### 4. 保留 GetColor(RoleIconType) 签名

**决策**：GetColor 方法保留 type 参数（虽然不再使用），仅返回 IconColor。

**依据**：
- 避免破坏调用方 HarmonyPatches.cs 第 205 行 `GUI.color = RoleIconDef.GetColor(type)`
- 未来如需恢复颜色分组，只需修改 GetColor 方法体
- 符合 KISS 原则：最小改动达成目标

## 代码实现情况

### RoleIconDef.cs 颜色统一

```csharp
// 原 3 个颜色常量
public static readonly Color CombatColor = new Color(1.0f, 0.55f, 0.06f);
public static readonly Color WorkColor = new Color(0.2f, 0.8f, 0.2f);
public static readonly Color TradeColor = new Color(1.0f, 0.4f, 0.7f);

// 替换为 1 个统一颜色
public static readonly Color IconColor = new Color(0.6f, 0.0f, 0.0f);

// GetColor 简化
public static Color GetColor(RoleIconType type)
{
    return IconColor;
}
```

### RoleIconDef.cs Ranged 判定扩展

```csharp
// 原：仅 Major
bool shootingMajor = IsMajorPassion(pawn, SkillDefOf.Shooting);
if (isTriggerHappy && shootingMajor)
    buffer.Add(RoleIconType.Ranged);

// 新：含 Minor
bool shootingMajor = IsMajorPassion(pawn, SkillDefOf.Shooting);
bool shootingMinor = IsMinorPassion(pawn, SkillDefOf.Shooting);
if (isTriggerHappy && (shootingMajor || shootingMinor))
    buffer.Add(RoleIconType.Ranged);
```

### WorkAllocator.Assignment.cs 神秘学者覆盖

```csharp
// 主循环与非候选清理循环均添加：
if (IsDarkStudyWork(workTypes) && IsOccultist(pawn))
{
    priority = 1;
}

// 新增辅助方法
private static bool IsDarkStudyWork(WorkTypeDef[] workTypes)
{
    if (cachedDarkStudyDef == null) return false;
    for (int i = 0; i < workTypes.Length; i++)
    {
        if (workTypes[i] == cachedDarkStudyDef) return true;
    }
    return false;
}

private static bool IsOccultist(Pawn pawn)
{
    if (TraitDefCache.Occultist == null) return false;
    if (pawn.story?.traits == null) return false;
    return pawn.story.traits.HasTrait(TraitDefCache.Occultist);
}
```

## 测试验证结果

### 编译验证

```
$ make check
AutoEverything -> Assemblies\AutoEverything.dll
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
  - 4 种图标在殖民者栏右上角显示为统一深红色，形状区分清晰
  - "乱开枪+射击单火"的 S 档殖民者正确显示远程图标
  - 神秘学者殖民者在 DarkStudy 工作上显示 priority=1（绕过硬上限）
- 无 Anomaly DLC 环境下神秘学者覆盖逻辑应自动跳过（TraitDefCache.Occultist 为 null）

## 下一轮计划

无。本次迭代完成 3 个用户需求，等待用户游戏内验证反馈。
