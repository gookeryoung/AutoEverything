# 自动装备模块新增

## 变动日期

2026-07-19

## 变动背景

用户基于 `req-01-自动装备需求.md` 要求新增 `AutoEquipment` 模块：自动按角色/评级/文化/情境综合评分分配护甲类装备（不含武器与附件），支持全局重分配（含扒装）。13 个评分权重参数暴露到 Mod 选项供玩家调整，ITab 底部新增第 4 个"自动装备"勾选框（2×2 双列紧凑布局）。

规则文件中无 AutoEquipment 模块职责、自动装备分配原则、同步清单条目与 ITab 勾选框布局描述，需同步更新。

## 变动内容

### `.trae/rules/autoeverything-project.md` 模块职责

- 新增：`- **AutoEquipment**：自动装备分配（仅护甲类，事件驱动 + 全局重分配可扒装，按 CombatTier 降序贪心分配，13 个 `ge*` 评分权重可调，`ApparelLayerFilter`/`CultureChecker`/`GearInventoryService`/`GearScorer`/`GearAllocator`）`
- 原因：新增 AutoEquipment 模块，需声明其职责范围与组件清单，与现有 AutoMarkPawn/AutoWork 等模块并列。

### `.trae/rules/autoeverything-project.md` 计算规则章节

- 新增：`### 自动装备分配原则` 子章节，覆盖范围、参与对象、触发方式、分配顺序、分配策略、替换阈值、扒装流程、评分公式、默认开关与错误隔离 salt。
- 原因：评分模型与分配规则是面向玩家的契约，必须与代码同步。13 个 `ge*` 字段、`geReplaceThreshold` 阈值、`AllocateErrorSalt = 0xA800` 均来自 `GearAllocator.cs` / `GearScorer.cs` / `AESettings.cs` 实际实现。

### `.trae/rules/autoeverything-project.md` 同步计算规则清单

- 新增 3 条同步条目（编号 18/19/20）：
  - 18. 自动装备分配规则（`GearAllocator.cs` / `GearScorer.cs` / `AutoEquipment` 模块）→ 同步 `## 自动装备分配（AutoEquipment）` 全部章节
  - 19. 装备评分权重（`AESettings.cs` 中的 `ge*` 字段）→ 同步 `### 评分公式` 表格
  - 20. 装备事件 Postfix（`HarmonyPatches.cs` 的 4 个事件 Postfix）→ 同步 `### 事件驱动` 表格
- 原因：规则文件原本只覆盖到 17 项同步条目，新增模块需追加同步清单，避免后续修改 `ge*` 字段或事件 Postfix 时遗漏同步 README。

### `.trae/rules/autoeverything-project.md` ITab 面板布局

- 旧：`面板尺寸 360f × 420f（高度容纳底部 3 勾选框）` + `底部 3 勾选框` 列出 3 个勾选框
- 新：`面板尺寸 360f × 420f（高度容纳底部 4 勾选框 2×2 双列紧凑布局）` + `底部 4 勾选框 2×2 双列紧凑布局（buttonGap=6f、checkboxHeight=22f）` 列出 4 个勾选框（评级/工作/星标/装备），并标注"自动装备默认关闭避免误扒装"
- 原因：ITab 底部从 3 勾选框单列（104f 高）改为 4 勾选框 2×2 双列（62f 高），布局参数与勾选框行为需与代码一致。装备勾选框行为独立说明：取消勾选仅停止自动（无法撤销已分配装备）。

## 影响范围

- 仅规则文件（文档），不影响代码编译与运行
- `make check` 通过验证

## 同步更新

- `project_memory.md` 追加本次规则文件变动记录（Rule File Update 章节）
- `README.md` 对应章节已在前序改动中同步更新（功能概览、`## 自动装备分配（AutoEquipment）` 全部章节、目录结构、模块职责、评估周期表、文档同步检查清单）
