# workCount 硬上限 + 烹饪升格为关键工作

## Context

用户反馈工作分配两个问题：
1. **没有厨子**：烹饪属"普通技能工作"，无火 top2 仅给 priority=3，殖民者总被 priority 1/2 的工作占用，实际无人优先做饭
2. **手工专家被分配研究 priority=1**：workCount 只是三因子排序的**第三兜底因子**（passion→skill→workCount），在 passion/skill 决出胜负后才生效，无法阻止已满载的专家被选为研究 top1

**根因**：workCount 无法在前置门槛阻止过载，让少数专家被榨干；烹饪优先级太低（priority=3），被高优先级工作抢占。

**用户决策**：
- workCount 硬上限 = 2 项（每人最多 2 项 priority≤2 的专业工作，候选不足时回退放宽）
- 烹饪升格为关键工作（保证至少 1 人 priority≤2）

## 当前状态分析

### AssignWorkType 候选收集（[WorkAllocator.cs:471-482](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/AutoWork/WorkAllocator.cs#L471-L482)）

当前仅过滤 `WorkTagIsDisabled` 和 `RequireRangedWeapon`，**不检查 workCount**。满载者仍进入候选池参与排序，仅靠第三因子兜底。

### 三因子排序（[WorkAllocator.cs:558-574](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/AutoWork/WorkAllocator.cs#L558-L574)）

`ComparePawnsByPassionWorkCountSkill`：Passion 降序 → Skill 降序 → WorkCount 升序。workCount 仅在 passion 与 skill 都相同时才起作用。

### 烹饪分类（[WorkAllocator.cs:162-165](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/AutoWork/WorkAllocator.cs#L162-L165)）

烹饪（defName=`Cooking`）因有 relevantSkills 被归入 `otherSkillWorkDefs`，与 Mining/Crafting/Art 等共用普通技能配置（无火 top2→3）。

### 关键工作配置（[WorkAllocator.cs:206-223](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/AutoWork/WorkAllocator.cs#L206-L223)）

`AssignKeyWorkPriorities` 遍历 `keyWorkDefs`（Doctor/Warden/Childcare），统一配置 GuaranteeCount=2, top2 有火→1, 无火→3。烹饪需要不同配置（GuaranteeCount=1），不能直接复用。

## 提议变更

### 变更 1：AssignWorkType 新增 workCount 硬上限

**文件**：`Source/AutoEverything/AutoWork/WorkAllocator.cs`

**Why**：把 workCount 从"第三排序兜底因子"提升为"前置候选门槛"，彻底阻止已满载者进入分配池。候选不足时回退放宽，保证小殖民地工作有人做。

**How**：
1. 新增常量 `private const int MaxCoreWorkCount = 2;`
2. 在 `AssignWorkType` 候选收集阶段（L473-481）跳过 `workCount[pawn] >= MaxCoreWorkCount` 的
3. 回退放宽：若严格收集后候选数 `< config.GuaranteeCount`，重新收集全部候选（含满载者）

**代码**（替换 L473-482 的候选收集块）：

```csharp
// 候选收集：跳过已满载者（workCount 硬上限，强制均衡负载）
workCandidates.Clear();
for (int i = 0; i < candidatePawns.Count; i++)
{
    Pawn pawn = candidatePawns[i];
    if (pawn.WorkTagIsDisabled(workType.workTags)) continue;
    if (config.RequireRangedWeapon && pawn.equipment?.Primary?.def.IsRangedWeapon != true) continue;
    if (workCount[pawn] >= MaxCoreWorkCount) continue;  // 硬上限：跳过已满载者
    workCandidates.Add(pawn);
}
// 回退放宽：严格候选不足保证人数时，重新收集全部候选（含满载者）
// 场景：小殖民地人手不足，必须让已满载者承担更多工作
if (workCandidates.Count < config.GuaranteeCount)
{
    workCandidates.Clear();
    for (int i = 0; i < candidatePawns.Count; i++)
    {
        Pawn pawn = candidatePawns[i];
        if (pawn.WorkTagIsDisabled(workType.workTags)) continue;
        if (config.RequireRangedWeapon && pawn.equipment?.Primary?.def.IsRangedWeapon != true) continue;
        workCandidates.Add(pawn);
    }
}
if (workCandidates.Count == 0) return;
```

**关键决策**：
- **回退条件用 `< GuaranteeCount` 而非 `< 候选总数`**：只要严格候选够保证人数就不放宽，让满载者休息；不足时才放宽
- **回退时清空重收集**：避免 `Contains` 去重的 O(n) 开销，候选列表通常 < 50 人，两次遍历可接受

### 变更 2：烹饪升格为关键工作

**文件**：`Source/AutoEverything/AutoWork/WorkAllocator.cs`

**Why**：烹饪是生存关键（没人做饭会饿死），应与 Doctor 同级保证 priority≤2，而非与 Art 等可选技能同级给 priority=3。

**How**：
1. 新增字段 `private static WorkTypeDef cachedCookingDef;`
2. `CacheAndClassifyWorkTypes` 中缓存 Cooking（defName=`Cooking`），并从 `otherSkillWorkDefs` 排除
3. `AssignKeyWorkPriorities` 末尾新增烹饪单独分配，用独立配置（GuaranteeCount=1）

**2a. 新增字段**（在 `cachedGrowingDef` 后）：

```csharp
private static WorkTypeDef cachedCookingDef;
```

**2b. CacheAndClassifyWorkTypes 缓存烹饪**（在 Growing 缓存后、Research 缓存前）：

```csharp
if (wt.defName == "Growing") { cachedGrowingDef = wt; continue; }
if (wt.defName == "Cooking") { cachedCookingDef = wt; continue; }
```

**2c. AssignKeyWorkPriorities 末尾新增烹饪分配**：

```csharp
// 烹饪：生存关键，保证 1 人 priority≤2
// top1 有火→1（优先让有火者做饭），无火→2（保底有人做）
// 超出 top1 的有火者→3（保留生产能力），无火者→0（不强制无兴趣者做饭）
WorkAllocationConfig cookingConfig = new WorkAllocationConfig
{
    GuaranteeCount = 1,
    GuaranteePassionatePriority = 1,
    GuaranteeNonPassionatePriority = 2,
    FloorPassionatePriority = 3,
    UseSkillFloorForNonPassionate = false,
    FloorNonPassionatePriority = 0,
    RequireRangedWeapon = false,
    UseBackRowSort = false
};
if (cachedCookingDef != null)
    AssignWorkType(cachedCookingDef, cookingConfig);
```

**关键决策**：
- **GuaranteeCount=1 而非 2**：烹饪不像医疗需要 2 人轮班，1 人 priority≤2 足够保证食物供应
- **无火不兜底（UseSkillFloorForNonPassionate=false）**：避免高技能无火者被迫做饭，烹饪应让有火者承担
- **烹饪在关键工作遍内分配**：workCount 在烹饪分配后更新，影响后续狩猎/研究/普通技能的候选门槛

### 变更 3：README 同步

**文件**：`README.md`

**3a. 统一四大原则 → 补充 workCount 硬上限说明**

在第 462 行"三因子排序"后追加：

```markdown
**workCount 硬上限**：每人最多承担 `MaxCoreWorkCount=2` 项 priority≤2 的专业工作。候选收集阶段跳过已满载者，强制均衡负载。若严格收集后候选不足保证人数，回退放宽（含满载者），保证小殖民地工作有人做。
```

**3b. 分配规则表 → 新增烹饪行，普通技能移除 Cooking**

将第 475 行关键工作表格扩展为：

```markdown
| 2 | 关键 | Doctor / Warden / Childcare | 2 | 1 | 3 | 3 | 技能兜底 | — |
| 2 | 烹饪 | Cooking | 1 | 1 | 2 | 3 | 0 | 生存关键，保证 1 人 priority≤2 |
```

将第 481 行普通技能行的"包含类型"移除 Cooking：

```markdown
| 5 | 普通技能 | Mining / Crafting / Smithing / Tailoring / Art / Construction / Handling 等 | 2 | 2 | 3 | 3 | 技能兜底 | — |
```

**3c. 工作计数说明 → 补充硬上限语义**

将第 492-493 行扩展为：

```markdown
**工作计数**：跟踪每 Pawn 的 priority ≤ 2 的专业工作数量（紧急/服务类不计入）。
用于「同等兴趣下优先安排其他工作少的」实现均衡负载。
**硬上限**：每人最多 2 项 priority≤2 的专业工作，候选收集阶段跳过已满载者，候选不足时回退放宽。
```

## 假设与决策

1. **MaxCoreWorkCount=2**：用户确认。每人最多 2 项核心工作（如 Doctor+Cooking，或 Hunting+Research），强制均衡负载。小殖民地回退放宽保证工作有人做。

2. **回退条件用 `< GuaranteeCount`**：只要严格候选够保证人数就不放宽。例如 10 人殖民地，3 人已满载（workCount=2），研究 GuaranteeCount=1，严格候选有 7 人 >> 1，不放宽，满载者休息。若 3 人殖民地，2 人已满载，研究严格候选仅 1 人 = GuaranteeCount=1，不放宽；若 0 人未满载，严格候选 0 < 1，回退放宽。

3. **烹饪 GuaranteeCount=1**：烹饪不像医疗需 2 人轮班，1 人 priority≤2 足够。有火者优先（top1→1），无火者保底（top1→2），超出的有火者保留生产能力（→3），无火者不强制（→0）。

4. **烹饪无火不兜底**：`UseSkillFloorForNonPassionate=false`，避免高技能无火者被迫做饭。烹饪应让有火者承担（兴趣驱动效率更高）。

5. **烹饪在关键工作遍内分配**：顺序上烹饪与 Doctor/Warden/Childcare 同遍，但配置不同（GuaranteeCount=1 vs 2）。烹饪分配后 workCount 更新，影响后续狩猎/研究/普通技能候选门槛。

6. **不修改 ScoringPipelineFactory 或其他模块**：仅 WorkAllocator.cs 与 README.md 受影响。

## 验证步骤

1. `make check` 编译通过（0 警告 0 错误）
2. 检查 `WorkAllocator.cs`：
   - 新增 `MaxCoreWorkCount = 2` 常量
   - 新增 `cachedCookingDef` 字段
   - `CacheAndClassifyWorkTypes` 缓存 Cooking 并从 otherSkillWorkDefs 排除
   - `AssignKeyWorkPriorities` 末尾新增烹饪单独分配
   - `AssignWorkType` 候选收集阶段含硬上限检查 + 回退放宽
3. 检查 `README.md`：
   - 统一四大原则章节补充 workCount 硬上限说明
   - 分配规则表新增烹饪行（关键工作），普通技能移除 Cooking
   - 工作计数说明补充硬上限语义
4. 项目规则同步检查清单：
   - [x] 改了 `WorkAllocator.cs`？→ README 分配规则表格已更新
   - [x] `make check` 通过
