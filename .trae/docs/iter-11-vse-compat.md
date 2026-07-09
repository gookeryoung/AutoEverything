# iter-11：Vanilla Skills Expanded（VSE）兼容

## 本轮目标

兼容 Vanilla Skills Expanded（VSE）mod 引入的扩展 passion 类型（不止原版 None/Minor/Major 三种），让评分、排序、角色判定、评级判定、装备评分在 VSE 加载时正确处理 6 种 passion。

## 改动文件清单

### 新建
- `Source/AutoEverything/Core/PassionHelper.cs`：VSE 兼容层核心，反射检测 VSE + 构建 passion→tier/name 映射 + 提供 GetPassionTier/GetPassionName API

### 修改（7 个 passion 使用点）
- `Source/AutoEverything/RoleEvaluation/CombatEvaluator.cs`：
  - `GetPassionMult`：基于 tier 计算战斗价值乘数（Apathy=none×0.5, Natural=Major, Critical=Major×1.5）
  - `AddSkillScore`：兴趣分按 tier 累加（Apathy/None 不加分, Minor=1, Major=2, Critical=3）
  - `CollectTierInput` + `CountPassions`/`IsPassion` 拆分为 `CountPassionsAtLeast`/`CountPassionsExactly`/`IsPassionAtLeast`/`IsPassionExactly`：Major 计数用 `tier >= Major`（含 Natural/Critical），Minor 计数用 `tier == Minor`（严格匹配避免双计数）
  - `CountWorkMajors`：同样改用 `IsPassionAtLeast(Major)`
- `Source/AutoEverything/AutoWork/WorkAllocator.Comparer.cs`：`GetMaxPassionForSkills` 返回 `(int)PassionHelper.GetPassionTier(sr.passion)`
- `Source/AutoEverything/AutoWork/WorkAllocator.Assignment.cs`：所有 `passionLevel >= (int)Passion.Major/Minor` 替换为 `PassionHelper.PassionTier.Major/Minor`（replace_all 批量）
- `Source/AutoEverything/RoleEvaluation/PawnRole.cs`：`meleePassion != Passion.None` 改为 `tier > None`，`meleePassion == Passion.Major` 改为通过 `PassionHelper.GetPassionName` 显示
- `Source/AutoEverything/AutoEquipment/Scoring/Weapon/WeaponSkillScorer.cs`：`GetPassionMultiplier` 改用 tier 查 weights；`GetPassionName` 改用 `PassionHelper.GetPassionName`
- `Source/AutoEverything/AutoEquipment/Scoring/Apparels/ApparelLabCoatScorer.cs`：`passion != Passion.None` 改为 `tier > None`，让 Apathy 正确视为无火及以下

### 文档
- `README.md`：兼容性章节新增 VSE 兼容说明（6 种 passion 处理规则表）
- `c:\Users\zhou\.trae-cn\memory\projects\...\project_memory.md`：追加 VSE 兼容约束

## 关键决策与依据

### 1. PassionTier 枚举不设 Natural 值
- **决策**：`PassionTier` 枚举只保留 5 个不同值（Apathy=-1, None=0, Minor=1, Major=2, Critical=3），VSE_Natural 在 `DefNameToTier` 时映射为 Major
- **依据**：C# switch 不允许两个相同常量值的 case 标签（CS0152 错误）。若枚举中 `Natural=2, Major=2`，switch 中 `case Major: case Natural:` 会编译失败。VSE_Natural 语义等同双火，归入 Major 不影响行为；名称显示通过独立 `nameMap` 查表保留"自然"区分

### 2. 评级判定拆分 AtLeast/Exactly 两种语义
- **决策**：新增 `IsPassionAtLeast(tier)` 与 `IsPassionExactly(tier)`，Major 计数用前者（含 Natural/Critical），Minor 计数用后者（严格匹配）
- **依据**：原 `IsPassion(pawn, skill, Passion.Major)` 用 `s.passion == passion` 等值比较，VSE_Natural(index=3+) 不会被识别为 Major。改为 `tier >= Major` 后 Natural/Critical 正确归入 Major 统计。Minor 若也用 `>=`，则 Major 会被双计数，违反评级规则

### 3. Apathy 视为"无火及以下"
- **决策**：Apathy tier=-1，在所有判定中：
  - 战斗价值乘数 = 无火 × 0.5（比无火还低）
  - 评级兴趣分不加分
  - 研究型判定（ApparelLabCoatScorer）满足"无火及以下"条件（`tier ≤ None`）
  - 工作分配走无火分支（passionLevel=-1 < Minor=1）
- **依据**：VSE 的 Apathy 是负面兴趣（learnRateFactor=0.25 < None=0.35），玩家不希望这类 Pawn 被当作"有火"

### 4. 反射兼容模式
- **决策**：启动时遍历 AppDomain 查找 `VSE.Passions.PassionManager` 类型，反射读 `Passions` 数组的 `defName` 字段构建映射；运行时 O(1) 查询，无 Tick 路径反射开销
- **依据**：参考 DLCCompat 模式（ModsConfig 检测 + try-catch + Log.ErrorOnce）；反射在静态构造中执行安全（不查 DefDatabase）；失败时降级为原版 3 档，不阻断主功能

### 5. VSE_Natural/Critical 装备评分乘数
- **决策**：
  - Natural → `w_passionMajor`（等同双火）
  - Critical → `w_passionMajor × 1.5`（高于双火）
  - Apathy → `1 / w_passionMinor`（比无火 1.0 更低）
- **依据**：与战斗价值乘数保持一致的相对关系

## 验证结果

- `make check`：0 警告 0 错误 ✓
- `make test`：62/62 测试通过（30 ApplySkillFloorCore + 32 EvaluateAutoTierCore）✓
- 工作树干净（除新增/修改的文件外无其他改动）
- 无 VSE 环境下行为与原版完全一致（PassionHelper 静态构造填充原版 3 档兜底）

## 遗留事项

- 未在 VSE 实际加载环境做游戏内验证（需玩家测试）
- 未给 PassionHelper 添加单元测试（需 mock VSE 反射环境，复杂度高，暂不实施）
- VSE 的 `PassionDef` 可能还有其他字段（如 `isBad`、`learnRateFactorOther`）未利用，当前仅用 defName 判断类型已足够
