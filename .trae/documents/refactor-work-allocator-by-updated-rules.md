# 按更新后的自动工作分配原则重构 WorkAllocator

## Context

规则文件 `.trae/rules/autoeverything-project.md` 第74-84行的"自动工作分配原则"已更新，明确要求区分双火/单火给不同优先级（如关键专业双火1、单火2、无火0）。但当前 `WorkAllocator.cs` 的 `WorkAllocationConfig` 结构只有 `GuaranteePassionatePriority`/`FloorPassionatePriority` 两个"有火"字段，对所有有火统一处理，无法表达双火/单火的差异。

主要不一致：
1. **未区分双火/单火**：规则要求双火/单火分别给不同优先级，代码统一处理
2. **次级专业保底人数**：规则要求保底2人，代码 `SecondaryConfig.GuaranteeCount=1`
3. **研究工作优先级**：规则要求双火2、单火3、无火0，代码是 GuaranteePassionate=1、FloorPassionate=2
4. **技能兜底与规则冲突**：代码 `UseSkillFloorForNonPassionate=true` 让无火者按技能给2/3，但规则说"无火优先级0"

用户已确认：去掉技能兜底（严格按规则），Fishing 保持次级专业分类。

## 重构方案

### 1. 扩展 WorkAllocationConfig 结构（WorkAllocator.cs 第147-157行）

把"有火"字段拆成 Major/Minor 两个版本，删除技能兜底字段：

```csharp
private struct WorkAllocationConfig
{
    public int GuaranteeCount;
    public int GuaranteeMajorPriority;          // top N 内双火优先级
    public int GuaranteeMinorPriority;          // top N 内单火优先级
    public int GuaranteeNonPassionatePriority;  // top N 内无火优先级（保底3）
    public int FloorMajorPriority;              // 超出 top N 的双火保底
    public int FloorMinorPriority;              // 超出 top N 的单火保底
    public int FloorNonPassionatePriority;      // 超出 top N 的无火优先级（0）
    public bool RequireRangedWeapon;
    public bool UseBackRowSort;
}
```

删除字段：`UseSkillFloorForNonPassionate`（技能兜底已去掉）。

### 2. 配置值（按规则文件，WorkAllocator.cs 第49-112行）

| 配置 | Guarantee | G_Major | G_Minor | G_NP | F_Major | F_Minor | F_NP | RequireRanged | BackRowSort |
|------|-----------|---------|---------|------|---------|---------|------|---------------|-------------|
| KeyWorkConfig | 2 | 1 | 2 | 3 | 1 | 2 | 0 | false | false |
| OtherSkillConfig | 2 | 2 | 3 | 3 | 2 | 3 | 0 | false | false |
| SecondaryConfig | 2 | 2 | 4 | 3 | 2 | 4 | 0 | false | false |
| SecondaryRangedConfig | 2 | 2 | 4 | 3 | 2 | 4 | 0 | true | true |
| ResearchConfig | 1 | 2 | 3 | 0 | 2 | 3 | 0 | false | false |

说明：
- 关键/普通/次级专业的 `G_NP=3` 实现"保底2人即使无火也3"
- `F_NP=0` 实现"超出保底的无火者给0"（即"新增更适合者原保底者降至0"）
- 研究工作 `G_NP=0`：top 1 内无火也给0（规则要求无火0），若无人有火则研究无人承担

### 3. 修改优先级选择逻辑

**AssignWorkType（第570-613行）和 AssignWorkGroup（第711-754行）** 的优先级选择分支改为按 Passion 三分支：

```csharp
int passionLevel = GetMaxPassionForSkills(pawn, workType.relevantSkills);
int priority;
if (i < config.GuaranteeCount && !isOverloaded)
{
    // top N 内（满载者回退放宽时不抢占 Guarantee）
    if (passionLevel >= (int)Passion.Major)
        priority = config.GuaranteeMajorPriority;
    else if (passionLevel >= (int)Passion.Minor)
        priority = config.GuaranteeMinorPriority;
    else
        priority = config.GuaranteeNonPassionatePriority;
}
else
{
    // 超出 top N 或满载者降级
    if (passionLevel >= (int)Passion.Major)
        priority = config.FloorMajorPriority;
    else if (passionLevel >= (int)Passion.Minor)
        priority = config.FloorMinorPriority;
    else
        priority = config.FloorNonPassionatePriority;
}
```

保留 `isOverloaded` 回退放宽分支（满载者不抢占 Guarantee，只给 Floor）。

### 4. 删除冗余方法

- 删除 `GetSkillFloorPriority`（第853-862行）：技能兜底已去掉
- 删除 `HasPassionForAnySkill`（第867-878行）：改为用 `GetMaxPassionForSkills` 返回值判断
- 保留 `GetMaxSkillLevelForSkills`：调试日志仍需显示技能等级

### 5. 调试日志 bucket 标识扩展

`AssignWorkType`/`AssignWorkGroup` 的调试日志中 bucket 标识从 G/F/N 扩展为 GM/Gi/FM/Fi/N：
- GM = Guarantee Major（top N 内双火）
- Gi = Guarantee Minor（top N 内单火）
- FM = Floor Major（超出 top N 双火）
- Fi = Floor Minor（超出 top N 单火）
- N = NonPassionate（无火）

### 6. 同步更新注释

- `WorkAllocationConfig` 结构 XML 注释（第139-157行）：更新四大原则描述
- 5个配置常量的行内注释（第49-112行）：更新优先级数值说明
- `AssignWorkType`/`AssignWorkGroup` 方法注释：更新原则描述

## 同步更新 README.md

### 分配规则表格（第476-483行）

列结构调整为：`顺序 | 工作分类 | 包含类型 | 保底人数 | 双火 | 单火 | 无火(top N) | 无火(超出) | 特殊约束`

| 顺序 | 工作分类 | 包含类型 | 保底 | 双火 | 单火 | 无火(top N) | 无火(超出) | 特殊约束 |
|------|---------|---------|------|------|------|-------------|------------|---------|
| 1 | 紧急 | Firefighter/Patient/PatientBedRest | — | 1 | 1 | 1 | — | 不计入 workCount |
| 2 | 关键专业 | Doctor/Warden/Childcare/Cooking/PlantCutting | 2 | 1 | 2 | 3 | 0 | — |
| 3 | 普通专业 | Construction/Mining/Growing/Smithing/Tailoring/Crafting/Art | 2 | 2 | 3 | 3 | 0 | Crafting 组分配共享 1 workCount |
| 4 | 次级专业 | Handling/Fishing/Hunting | 2 | 2 | 4 | 3 | 0 | Hunting 需远程武器+后排排序 |
| 5 | 研究 | Research/DarkStudy | 1 | 2 | 3 | 0 | 0 | 最后分配 |
| 6 | 辅助 | Hauling/Cleaning/BasicWorker 等 | — | 见辅助规则 | — | — | — | 不计入 workCount，按评级分档 |

### 统一四大原则（第457-464行）

更新原则4：删除"技能兜底"描述，改为"超出 guarantee 的无火者直接给 `FloorNonPassionatePriority`（通常为0）"。

## 关键文件

- `Source/AutoEverything/AutoWork/WorkAllocator.cs` — 主要修改
- `README.md` — 同步分配规则表格与四大原则

## 验证

1. `make check` — 编译零错误零警告（`-warnaserror`）
2. `make rebuild-check` — 大改动后完整重建验证
3. 检查调试日志：开启 `AESettings.debugLogging`，触发工作重配，确认日志中 `[WorkAllocator]` 行的 bucket 标识为 GM/Gi/FM/Fi/N，优先级数值符合规则
4. 边界场景验证：
   - 全无火殖民者：关键/普通/次级专业应保底2人给 prio3，研究无人承担
   - 单一有火者：该有火者进 top N 给对应 prio，无火者超出 top N 给 prio0
   - 满载者（workCount≥3）回退放宽时降级至 Floor 保底
