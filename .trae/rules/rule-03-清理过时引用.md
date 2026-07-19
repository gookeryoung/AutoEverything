# 清理过时引用

## 变动日期

2026-07-19

## 变动背景

三轮代码优化（commit 74c9eb6 / 78eabe8）已删除大量死代码与装备分配遗留模块，但规则文件中仍残留对已删除模块/类的引用，导致规则文件与代码状态不一致。

## 变动内容

### `.trae/rules/rimworld-mod-dev.md` L21

- 旧：`单一职责：一个类/文件只做一件事（参考 BeltAllocator 只管腰带）`
- 新：`单一职责：一个类/文件只做一件事（参考 PawnJobGuard 只管 Job 守卫）`
- 原因：`BeltAllocator`（腰带分配器）属于已移除的 `AutoEquipment`/`Allocation` 模块，引用已不存在的类误导后续开发。`PawnJobGuard` 是当前 Core 模块中职责单一的示例。

### `.trae/rules/autoeverything-project.md` L41

- 旧：`RoleEvaluation：角色与情境评价（PawnRole/RoleDetector、GearContext/ContextDetector、CombatEvaluator、PawnStateCleaner）`
- 新：`RoleEvaluation：角色与情境评价（PawnRole/RoleDetector、GearContext/ContextDetector、CombatEvaluator）`
- 原因：`PawnStateCleaner` 在第一轮优化（commit 74c9eb6）中已整文件删除，仅被两个死代码 `CleanupDeadPawns` 方法调用，删除后无人引用。

### `.trae/rules/autoeverything-project.md` L46-47

- 旧：
  ```
  > 历史模块 AutoEquipment/Allocation（装备评分系统、副武器/腰带分配等）、AutoFood/AutoDrug（食物/用药方案自动配置）已移除（与其他 MOD 冲突或代码精简）。
  > 原 CompGearManager（ThingComp Tick 入口）已替换为 AutoEverythingGameComponent（GameComponent），从源头杜绝 ThingDef.comps 注入冲突。
  ```
- 新：删除上述两行历史说明
- 原因：规则文件应只描述当前状态，历史变迁记录在 `project_memory.md` 的 Cleanup Decisions 章节中。规则文件中保留历史说明会增加新成员阅读负担，且容易在后续清理中被遗忘同步。

## 影响范围

- 仅规则文件（文档），不影响代码编译与运行
- `make check` 通过验证

## 同步更新

- `project_memory.md` 追加本次规则文件变动记录
