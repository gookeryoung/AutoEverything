# AutoWork v2 重构计划：多遍协调分配 + 工作计数跟踪

## 摘要

将当前 WorkAllocator 的「单遍独立分配」升级为「多遍协调分配 + 工作计数跟踪」，按用户 6 点需求落实分类规则：紧急 / 关键 / 狩猎 / 研究 / 普通技能 / 杂务 / 非技能。引入 `Dictionary<Pawn, int>` 跟踪每 Pawn 的 priority ≤ 2 专业工作数量，使「同等兴趣下优先安排其他工作少的」可执行。狩猎分配新增后排角色优先逻辑。

## 当前状态分析

**[WorkAllocator.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/AutoWork/WorkAllocator.cs)** 现状（212 行）：
- 单遍遍历所有 WorkTypeDef，每个独立分配
- 已正确处理：紧急工作→1、搬运/清洁按 CombatTier 分档、技能工作 passion→2/0、全无人有兴趣取前 2 人→3
- **缺陷**：
  1. 无工作计数跟踪，无法实现「同等兴趣下优先安排其他工作少的」
  2. 不区分关键工作（医疗/监管/保育）与普通技能工作
  3. 狩猎走通用技能工作分支，无法限制最多 2 人、无法优先后排
  4. 研究走通用分支，无法保证至少 1 人、无法 main→2/Others→4
  5. 关键工作无兜底「保证 2 人 priority ≥ 1」

**[PawnRole.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/RoleEvaluation/PawnRole.cs)** 现状：
- 已有 `GetArmorPreference(Role)`，返回 Heavy/Flexible/Light
- 缺 `IsBackRow(Role)` 辅助方法，需新增

**循环依赖注意**：`Role.Hunter` 依赖 `Hunting priority == 1`。本计划中 Hunting 始终设为 2 或 0（不设 1），所以不会污染角色检测。在 WorkAllocator 内调用 `RoleDetector.DetectRole(pawn)` 是安全的——读取的是玩家手动设置或上一次分配前的状态。

**关键工作 defName 确认**（通过 WorkTypes.xml 验证）：
- `Doctor`（Core，workTags: Caring/Commoner/AllWork）
- `Warden`（Core，workTags: Social/AllWork，disabledForSlaves=true）
- `Childcare`（Biotech DLC，workTags: Social/Caring/AllWork）

**钓鱼处理**：原版无 Fishing WorkTypeDef（VE 模组可能添加）。计划中按 defName=="Fishing" 归入狩猎类别，不存在则自动跳过。

## 已确认的设计决策（来自 AskUserQuestion）

1. **后排角色定义** = 仅 `ArmorPreference.Flexible`（Shooter/Hunter/Leader）
2. **医疗 Others → 4**（可备选）；**狩猎 Others → 0**（禁用）
3. **关键工作（监管/保育）规则** = 同医疗：passion→1，保证 2 人 priority ≥ 1，Others→4

## 完整优先级规则表（决策完毕）

| 顺序 | 工作类型 | 分类 | 分配规则 | Others 优先级 |
|------|---------|------|---------|--------------|
| 1 | Firefighter/Patient/PatientBedRest | 紧急 | 全部→1 | — |
| 2 | Doctor/Warden/Childcare | 关键 | passion→1；保证 ≥ 2 人 priority ≥ 1（不足时取技能最高的补到 1）；计入工作计数 | →4 |
| 3 | Hunting/Fishing | 特殊 | 候选过滤→后排优先→passion desc→workCount asc→skill desc；top 2→2；计入工作计数 | →0 |
| 4 | Research | 特殊 | guarantee 1：passion desc→workCount asc→skill desc；main 1→2；计入工作计数 | →4 |
| 5 | 其他技能工作（Cooking/Growing/Mining/Crafting/Smithing/Tailoring/Art/Construction/PlantCutting/Handling） | 专业 | guarantee 2：passion desc→workCount asc→skill desc；top 2→2；计入工作计数 | →4 |
| 6 | Hauling/Cleaning | 杂务 | S=4, A/B/C=3, D/X=1（不变） | — |
| 7 | BasicWorker 等无 relevantSkills | 非技能 | 全部→3 | — |

**工作计数定义**：`Dictionary<Pawn, int>`，记录每 Pawn 的 priority ≤ 2 的「专业工作」数量。
- 计入：关键工作 passion=1、关键工作兜底=1、狩猎 top2=2、研究 main=2、其他技能工作 top2=2
- 不计入：紧急工作 priority=1（所有人都有，无区分度）、搬运/清洁、非技能工作

## 提议变更

### 变更 1：[PawnRole.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/RoleEvaluation/PawnRole.cs) — 新增 `IsBackRow` 方法

**位置**：`RoleDetector` 类内，紧跟 `GetArmorPreference` 之后

**新增内容**：
```csharp
/// <summary>
/// 判定角色是否为后排（用于狩猎分配优先级）。
/// 后排 = Flexible 护甲偏好：Shooter/Hunter/Leader。
/// 设计意图：后排角色应优先承担狩猎以练习射击，前排近战角色不应被分配狩猎。
/// 注意：本方法不依赖 Hunting 工作优先级（避免循环依赖），仅看护甲偏好。
/// </summary>
public static bool IsBackRow(Role role)
{
    return GetArmorPreference(role) == ArmorPreference.Flexible;
}
```

**为什么**：WorkAllocator 需要判断「后排角色」用于狩猎优先分配。直接复用 `GetArmorPreference` 避免重复枚举，并保证语义统一。

### 变更 2：[WorkAllocator.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/AutoWork/WorkAllocator.cs) — 完全重构为多遍协调分配

**整体结构**：

```csharp
public static class WorkAllocator
{
    private static readonly List<Pawn> candidatePawns = new List<Pawn>();
    private static readonly List<Pawn> workCandidates = new List<Pawn>();      // 单工作候选复用
    private static readonly Dictionary<Pawn, int> workCount = new Dictionary<Pawn, int>();  // 工作计数
    private static List<WorkTypeDef> cachedWorkTypes;
    private static WorkTypeDef cachedHuntingDef;
    private static WorkTypeDef cachedResearchDef;
    private static bool keyWorkDefsCached;
    private static readonly List<WorkTypeDef> keyWorkDefs = new List<WorkTypeDef>();
    private static readonly List<WorkTypeDef> otherSkillWorkDefs = new List<WorkTypeDef>();
    private static WorkTypeDef cachedFishingDef;

    public static int ReallocateAll()
    {
        // 1. 启用自定义优先级开关
        if (!Find.PlaySettings.useWorkPriorities)
            Find.PlaySettings.useWorkPriorities = true;

        // 2. 收集候选殖民者（不变）
        candidatePawns.Clear();
        foreach (Map map in Find.Maps) { ... }
        if (candidatePawns.Count == 0) return 0;

        // 3. 懒加载并分类 WorkTypeDef
        CacheAndClassifyWorkTypes();

        // 4. 重置工作计数
        workCount.Clear();
        foreach (var p in candidatePawns) workCount[p] = 0;

        // 5. 多遍分配（顺序影响工作计数，必须按此顺序）
        AssignEmergencyPriorities();          // 第 1 遍：紧急
        AssignKeyWorkPriorities();            // 第 2 遍：关键（Doctor/Warden/Childcare）
        AssignHuntingPriorities();            // 第 3 遍：狩猎（含 Fishing）
        AssignResearchPriorities();           // 第 4 遍：研究
        AssignOtherSkillWorkPriorities();     // 第 5 遍：其他技能工作
        AssignHaulingCleaningPriorities();    // 第 6 遍：搬运/清洁
        AssignNonSkillWorkPriorities();       // 第 7 遍：非技能

        return candidatePawns.Count;
    }
}
```

**WorkTypeDef 分类逻辑**（`CacheAndClassifyWorkTypes`，只执行一次）：

```csharp
private static void CacheAndClassifyWorkTypes()
{
    if (cachedWorkTypes != null) return;
    cachedWorkTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading;

    foreach (var wt in cachedWorkTypes)
    {
        // 紧急：Firefighting tag 或 defName=Patient/PatientBedRest
        // → 第 1 遍处理，不进列表

        // 关键工作白名单（defName 判断，避免 WorkTags 漏掉 Warden）
        if (wt.defName == "Doctor" || wt.defName == "Warden" || wt.defName == "Childcare")
        {
            keyWorkDefs.Add(wt);
            continue;
        }

        // 狩猎：Hunting + Fishing
        if (wt.defName == "Hunting") { cachedHuntingDef = wt; continue; }
        if (wt.defName == "Fishing") { cachedFishingDef = wt; continue; }

        // 研究
        if (wt.defName == "Research") { cachedResearchDef = wt; continue; }

        // 搬运/清洁
        if (wt.defName == "Hauling" || wt.defName == "Cleaning") continue;

        // 其他技能工作：有 relevantSkills 且非上述类别
        if (wt.relevantSkills != null && wt.relevantSkills.Count > 0)
            otherSkillWorkDefs.Add(wt);
        // 非技能工作（如 BasicWorker/Firefighter）：第 7 遍统一处理
    }
}
```

**第 1 遍：紧急工作**（`AssignEmergencyPriorities`）
- 遍历 cachedWorkTypes，对 isEmergency 的 WorkTypeDef：全部 priority=1
- 不计入 workCount（所有人都有，无区分度）
- 实现与现有逻辑相同，仅抽出为独立方法

**第 2 遍：关键工作**（`AssignKeyWorkPriorities`，对每个 keyWorkDefs 调用 `AssignKeyWorkType`）

```csharp
private static void AssignKeyWorkType(WorkTypeDef workType)
{
    // 1. 收集候选：未禁用该工作 tag
    workCandidates.Clear();
    foreach (var pawn in candidatePawns)
    {
        if (pawn.WorkTagIsDisabled(workType.workTags)) continue;
        workCandidates.Add(pawn);
    }
    if (workCandidates.Count == 0) return;

    // 2. 分组：passionate（有兴趣） vs nonPassionate
    var passionate = new List<Pawn>();
    var nonPassionate = new List<Pawn>();
    foreach (var pawn in workCandidates)
    {
        if (HasPassionForAnySkill(pawn, workType.relevantSkills))
            passionate.Add(pawn);
        else
            nonPassionate.Add(pawn);
    }

    // 3. 有兴趣 → priority=1，计入 workCount
    foreach (var pawn in passionate)
    {
        pawn.workSettings.SetPriority(workType, 1);
        workCount[pawn]++;
    }

    // 4. 保证至少 2 人 priority >= 1
    int assigned = passionate.Count;
    if (assigned < 2 && nonPassionate.Count > 0)
    {
        // 按技能等级降序补足
        nonPassionate.Sort((a, b) => 
            ComputeSkillScore(b, workType.relevantSkills)
            .CompareTo(ComputeSkillScore(a, workType.relevantSkills)));
        int need = 2 - assigned;
        for (int i = 0; i < nonPassionate.Count; i++)
        {
            int priority = i < need ? 1 : 4;
            nonPassionate[i].workSettings.SetPriority(workType, priority);
            if (priority <= 2) workCount[nonPassionate[i]]++;
        }
    }
    else
    {
        // 已 >= 2 人有兴趣，无兴趣者 → 4
        foreach (var pawn in nonPassionate)
            pawn.workSettings.SetPriority(workType, 4);
    }
}
```

**第 3 遍：狩猎**（`AssignHuntingPriorities`，处理 Hunting + Fishing）

```csharp
private static void AssignHuntingPriorities()
{
    if (cachedHuntingDef != null)
        AssignHuntingType(cachedHuntingDef);
    if (cachedFishingDef != null)
        AssignHuntingType(cachedFishingDef);
}

private static void AssignHuntingType(WorkTypeDef workType)
{
    // 1. 收集候选
    workCandidates.Clear();
    foreach (var pawn in candidatePawns)
    {
        if (pawn.WorkTagIsDisabled(workType.workTags)) continue;
        workCandidates.Add(pawn);
    }
    if (workCandidates.Count == 0) return;

    // 2. 排序：后排优先 → passion desc → workCount asc → skill desc
    workCandidates.Sort((a, b) => ComparePawnsForHunting(a, b, workType.relevantSkills));

    // 3. top 2 → 2，其余 → 0
    for (int i = 0; i < workCandidates.Count; i++)
    {
        int priority = i < 2 ? 2 : 0;
        workCandidates[i].workSettings.SetPriority(workType, priority);
        if (priority <= 2) workCount[workCandidates[i]]++;
    }
}

private static int ComparePawnsForHunting(Pawn a, Pawn b, List<SkillDef> skills)
{
    // 后排优先（Flexible=Shooter/Hunter/Leader）
    bool backA = RoleDetector.IsBackRow(RoleDetector.DetectRole(a));
    bool backB = RoleDetector.IsBackRow(RoleDetector.DetectRole(b));
    if (backA != backB) return backB.CompareTo(backA);  // true 排前

    // 其余因子复用通用比较
    return ComparePawnsByPassionWorkCountSkill(a, b, skills);
}
```

**第 4 遍：研究**（`AssignResearchPriorities`）

```csharp
private static void AssignResearchPriorities()
{
    if (cachedResearchDef == null) return;
    WorkTypeDef workType = cachedResearchDef;

    workCandidates.Clear();
    foreach (var pawn in candidatePawns)
    {
        if (pawn.WorkTagIsDisabled(workType.workTags)) continue;
        workCandidates.Add(pawn);
    }
    if (workCandidates.Count == 0) return;

    // 排序：passion desc → workCount asc → skill desc
    workCandidates.Sort((a, b) => ComparePawnsByPassionWorkCountSkill(a, b, workType.relevantSkills));

    // guarantee 1：top 1 → 2，其余 → 4
    for (int i = 0; i < workCandidates.Count; i++)
    {
        int priority = i < 1 ? 2 : 4;
        workCandidates[i].workSettings.SetPriority(workType, priority);
        if (priority <= 2) workCount[workCandidates[i]]++;
    }
}
```

**第 5 遍：其他技能工作**（`AssignOtherSkillWorkPriorities`，对每个 otherSkillWorkDefs 调用 `AssignOtherSkillWorkType`）

```csharp
private static void AssignOtherSkillWorkType(WorkTypeDef workType)
{
    workCandidates.Clear();
    foreach (var pawn in candidatePawns)
    {
        if (pawn.WorkTagIsDisabled(workType.workTags)) continue;
        workCandidates.Add(pawn);
    }
    if (workCandidates.Count == 0) return;

    // 排序：passion desc → workCount asc → skill desc
    workCandidates.Sort((a, b) => ComparePawnsByPassionWorkCountSkill(a, b, workType.relevantSkills));

    // guarantee 2：top 2 → 2，其余 → 4
    for (int i = 0; i < workCandidates.Count; i++)
    {
        int priority = i < 2 ? 2 : 4;
        workCandidates[i].workSettings.SetPriority(workType, priority);
        if (priority <= 2) workCount[workCandidates[i]]++;
    }
}
```

**通用三因子比较器**（`ComparePawnsByPassionWorkCountSkill`）：

```csharp
/// <summary>
/// 三因子排序：Passion(desc) → WorkCount(asc) → SkillLevel(desc)
/// 设计意图：兴趣高者优先；同等兴趣下优先安排其他工作少的（均衡负载）；
/// 兴趣与负载都相同时按技能等级高低决断。
/// </summary>
private static int ComparePawnsByPassionWorkCountSkill(Pawn a, Pawn b, List<SkillDef> skills)
{
    // 1. Passion 降序（Major=2 > Minor=1 > None=0）
    int passionA = GetMaxPassionForSkills(a, skills);
    int passionB = GetMaxPassionForSkills(b, skills);
    if (passionA != passionB) return passionB.CompareTo(passionA);

    // 2. WorkCount 升序（工作少的优先）
    int countA = workCount[a];
    int countB = workCount[b];
    if (countA != countB) return countA.CompareTo(countB);

    // 3. Skill 降序
    float skillA = ComputeSkillScore(a, skills);
    float skillB = ComputeSkillScore(b, skills);
    return skillB.CompareTo(skillA);
}

/// <summary>返回该 Pawn 在指定技能集上的最高 Passion 量化值（None=0, Minor=1, Major=2）</summary>
private static int GetMaxPassionForSkills(Pawn pawn, List<SkillDef> skills)
{
    if (pawn?.skills == null) return 0;
    int max = 0;
    for (int i = 0; i < skills.Count; i++)
    {
        SkillRecord sr = pawn.skills.GetSkill(skills[i]);
        if (sr == null) continue;
        int v = (int)sr.passion;
        if (v > max) max = v;
    }
    return max;
}
```

**第 6 遍：搬运/清洁**（`AssignHaulingCleaningPriorities`）
- 保持现有逻辑：S=4, A/B/C=3, D/X=1
- 不计入 workCount

**第 7 遍：非技能工作**（`AssignNonSkillWorkPriorities`）
- 遍历 cachedWorkTypes，对未在前 6 遍处理的（无 relevantSkills 且非紧急、非搬运清洁）→ 全部 priority=3
- 不计入 workCount

### 变更 3：[README.md](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/README.md) — 更新 AutoWork 规则表

替换 `## 自动工作分配（AutoWork）` 章节的 `### 分配规则` 子章节为新的完整规则表：

```markdown
### 分配规则

工作类型按以下分类与顺序分配（顺序影响工作计数，前排分配结果影响后排候选）：

| 顺序 | 工作分类 | 包含类型 | 分配规则 | Others |
|------|---------|---------|---------|--------|
| 1 | 紧急 | Firefighter / Patient / PatientBedRest | 全部 → 1 | — |
| 2 | 关键 | Doctor / Warden / Childcare | 有兴趣 → 1；保证至少 2 人 priority ≥ 1（不足时按技能等级补足）；计入工作计数 | → 4 |
| 3 | 狩猎 | Hunting / Fishing | 候选排序：后排优先 → 兴趣降序 → 工作计数升序 → 技能降序；top 2 → 2；计入工作计数 | → 0 |
| 4 | 研究 | Research | 保证 1 人：排序同上（无后排优先）；top 1 → 2；计入工作计数 | → 4 |
| 5 | 普通技能 | Cooking / Growing / Mining / Crafting / Smithing / Tailoring / Art / Construction / PlantCutting / Handling | 保证 2 人：排序同上；top 2 → 2；计入工作计数 | → 4 |
| 6 | 杂务 | Hauling / Cleaning | S 档 = 4，A/B/C 档 = 3，D/X 档 = 1 | — |
| 7 | 非技能 | BasicWorker 等 | 全部 → 3 | — |

**工作计数**：跟踪每 Pawn 的 priority ≤ 2 的专业工作数量（紧急/搬运/清洁/非技能不计入）。
用于「同等兴趣下优先安排其他工作少的」实现均衡负载。

**三因子排序**：Passion 降序 → WorkCount 升序 → SkillLevel 降序。
Passion 量化：None=0, Minor=1, Major=2。

**后排角色优先**（仅狩猎）：通过 `RoleDetector.IsBackRow(role)` 判定，仅 `ArmorPreference.Flexible`（Shooter/Hunter/Leader）视为后排。
设计意图：后排角色应优先承担狩猎以练习射击能力。
```

### 变更 4：[AE_Keyed.xml (中文)](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Languages/ChineseSimplified/Keyed/AE_Keyed.xml) — 更新 tooltip

替换 `AE_TT_GlobalWorkReallocate` 内容为：

```xml
<AE_TT_GlobalWorkReallocate>按工作分类与多遍协调重新分配所有殖民者的工作优先级。
紧急工作=1；关键工作（医疗/监管/保育）有兴趣=1且保证≥2人；狩猎最多2人，后排优先；研究保证1人；其他技能工作保证2人，按兴趣→工作计数→技能排序；搬运清洁按档位=1/3/4。</AE_TT_GlobalWorkReallocate>
```

### 变更 5：[AE_Keyed.xml (英文)](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Languages/English/Keyed/AE_Keyed.xml) — 更新对应英文翻译

按相同结构提供英文版本（保持 Key 一致）。

## 假设与决策

1. **关键工作白名单用 defName 而非 WorkTags**：Warden 的 workTags 是 `Social/AllWork`，没有 `Caring` tag。若用 `(workTags & Caring) != 0` 检测会漏掉 Warden。改用 defName 白名单最稳妥。
2. **钓鱼（Fishing）归类狩猎**：原版无此 WorkTypeDef，但 VE 模组可能添加。若存在则按狩猎规则（max 2, Others→0）处理；不存在则 `cachedFishingDef == null` 自动跳过。
3. **工作计数仅计 priority ≤ 2 的专业工作**：紧急工作（priority=1）所有人都有，无区分度，不计入；搬运/清洁/非技能工作性质不同，不计入。
4. **循环依赖规避**：Hunting 始终设为 2 或 0，绝不设为 1，因此 `RoleDetector.DetectRole` 不会被本分配器污染。DetectRole 读取的是分配前的玩家手动状态，安全。
5. **候选复用 `workCandidates` 静态字段**：每遍分配前 Clear+重填，避免 GC。注意单遍内不应跨方法持有引用。
6. **`Dictionary<Pawn, int>` workCount**：每次 `ReallocateAll` 入口 Clear 并重置所有候选为 0，避免脏数据。
7. **Sort 比较器返回 0 的稳定性**：.NET 4.x Sort 不保证稳定，但本场景下三因子排序结果对同分 Pawn 顺序不敏感（取 top N 时同分都进或都不进，不影响公平性）。
8. **`RoleDetector.DetectRole` 调用成本**：单次调用涉及字典查询 + 技能读取，对 50 个 Pawn × 1 次调用 = 50 次，性能可接受（手动按钮触发，非 Tick 路径）。
9. **`workCandidates.Sort` 不用 LINQ**：符合 Tick 路径规范。虽然此为按钮触发路径，但保持代码风格一致。

## 验证步骤

1. **编译验证**：在项目根目录执行 `make check`，必须 0 警告 0 错误
2. **代码自检**：
   - 所有新增方法无 LINQ、无 `new List<>()` 在循环内
   - `workCount` 在每次 `ReallocateAll` 入口被 Clear+重置
   - `workCandidates` 在每个 `Assign*` 方法开头被 Clear
   - Hunting/Fishing 不设为 priority=1
3. **游戏内验证**（手动）：
   - 全殖民者有兴趣 Doctor → 全部 priority=1
   - 仅 1 人有兴趣 Doctor → 该人 priority=1，按技能补 1 人到 priority=1，其余 priority=4
   - 全殖民者无兴趣 Doctor → 取技能最高的 2 人 priority=1，其余 priority=4
   - 狩猎：5 个后排 + 5 个前排候选 → top 2 后排 priority=2，其余 priority=0
   - 研究：3 候选 → top 1 priority=2，其余 priority=4
   - 工作计数：观察某 Pawn 被分配多个 priority ≤ 2 工作后，下一遍分配时排序优先级下降
4. **文档同步检查**：
   - README.md 的 AutoWork 规则表与代码一致
   - AE_Keyed.xml tooltip 描述与规则一致
   - 项目规则文件无需修改（命名空间未变）

## 实施顺序

1. 修改 `PawnRole.cs`：新增 `IsBackRow` 方法
2. 重构 `WorkAllocator.cs`：完全替换为多遍分配实现
3. 运行 `make check` 验证编译
4. 更新 `README.md` 的 AutoWork 规则表
5. 更新 `AE_Keyed.xml`（中英文）的 tooltip
6. 再次 `make check` 验证（XML 不影响编译，但保持流程一致）
