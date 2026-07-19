# Auto Everything

> 智能自动万物管理 MOD，适用于 RimWorld 1.6+
>
> packageId: `gookeryoung.autoeverything`

为殖民者自动执行**人员评级**、**工作优先级分配**与**高价值目标标记**，让玩家从繁琐的微调中解放出来。

零配置开箱即用，每个殖民者根据技能与特质自动识别角色与评级。

## 功能概览

| 模块 | 功能 | 触发方式 |
|------|------|----------|
| **AutoTier**（人员自动评级） | 按 SSS/SS/S/A/B/C/D/X 档次评级，可选应用评级前缀到 Nick 并重排殖民者栏 | 周期 3000 tick + 新增殖民者 + ITab 勾选 |
| **AutoWork**（工作自动配置） | 按工作类别与兴趣/技能多遍协调分配工作优先级 | 事件驱动（殖民者增减）+ 冷却 2500 tick + ITab 勾选 |
| **AutoMarkPawn**（高价值标记） | 为 S+ 档次非殖民者人类头顶实时绘制红色星标 ★ | 实时绘制（Harmony 补丁）+ ITab 勾选 |

## 设计思路

1. **角色驱动**：殖民者不是无差别的劳动力，而是有专长的个体。评级系统综合射击、近战、工作、社交、特质等维度评判全局价值，让玩家一眼分辨主力与辅助。
2. **情境感知**：同一殖民者在不同情境下角色不同。情境检测器识别战斗/工作/狩猎/寒冷/炎热等状态，供面板展示与工作分配参考。
3. **工作均衡**：工作分配按兴趣 → 技能 → 工作计数三因子排序，保证关键工作有人做、专业工作兴趣者优先、辅助工作按评级分档，避免高价值殖民者被杂务拖累。
4. **逻辑杜绝而非事后清理**：食尸鬼、动物等不适用类别在入口（ITab 可见性、工作分配前过滤）就被排除，绝不进入自动管理流程。
5. **安全可逆**：评级标签与星标均为纯前端展示，不修改 Pawn 核心数据；工作优先级可随时手动调整。

## 角色检测规则

`PawnRole.cs` 中的 `RoleDetector.DetectRole(pawn)` 按以下优先级判定角色：

| 优先级 | 角色 | 判定条件 |
|--------|------|----------|
| 1 | `Pacifist`（和平主义者）| `WorkTags.Violent` 被禁用 |
| 2 | `Leader` / 二次分类 | 拥有意识形态角色，按其技能倾向二次分类为 `Doctor`/`Shooter`/`Brawler`/`Leader` |
| 3 | `Brawler`（格斗者）| 拥有 `Brawler` 特质 |
| 4 | `Hunter`（猎人）| 狩猎工作优先级为 1 |
| 5 | `Doctor`（医生）| 医疗 ≥ 8 且为最高战斗相关技能 |
| 6 | `Shooter`（射手）| 射击 ≥ 8 且射击 > 近战 |
| 7 | `Brawler`（格斗者）| 近战 ≥ 8 且近战 > 射击 |
| 8 | `Worker`（工人）| 射击与近战均 < 5 |
| 9 | `Shooter`/`Brawler` | 中等技能按高低判定 |

**护甲偏好（`ArmorPreference`）**：角色检测同时输出护甲偏好（`Heavy`/`Flexible`/`Light`），仅用于 ITab 面板徽章展示与工作分配中的"后排角色"判定（`IsBackRow` = Shooter/Hunter/Leader）。

## 情境检测规则

`GearContext.cs` 中的 `ContextDetector.GetContext(pawn)` 判定以下情境，仅用于 ITab 面板徽章展示：

| 情境 | 触发条件 |
|------|----------|
| `Combat` | 已征召，或当前 Job 为战斗类（`AttackStatic`/`AttackMelee`/`Wait_Combat` 等非狩猎的 `alwaysShowWeapon` Job） |
| `Hunting` | 当前工作为 `Hunt` 或 `PredatorHunt` |
| `Cold` | 环境温度低于舒适下限 + 5℃，持续 2500 tick（约 42 秒） |
| `Hot` | 环境温度高于舒适上限 + 5℃，持续 2500 tick |
| `Work` | 正在执行非战斗工作 |
| `Normal` | 默认 |

温度情境需持续暴露，避免频繁切换。

## 全局价值评级档次（CombatTier）

殖民者**全局价值**按 `CombatTier` 枚举离散化为 8 档，DEBUG 模式下在面板角色行与日志中以 `S#王五` 格式显示（自定义评级则显示 `S(A)#王五`，括号内为玩家指定档）。

**评级规则（不再局限于战斗维度，覆盖生产、社交、特质等全局价值）：**

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

**三大维度取最高档（MaxTier，不互斥）：** 乱开枪系列 / 坚韧格斗系列 / 工作狂神经质系列。

**专业工作技能（用于工作狂神经质系列判定）：** 手工、建造、艺术、烹饪、种植、采矿（共 6 项，统计 Major 数量）。

**降档规则：** 三大维度与原 S 条件 4/5 计算出 tier 后，若拥有任一负面特质且 `tier > D`，则 `tier` 降一档（D 不再降，X 先于一切判定不受影响）。

**配偶评级豁免：** 与 S 级以上（S/SS/SSS）人员结婚的殖民者，评级至少为 S（不降级 SS/SSS）。配偶评级用 `GetAutoCombatTier` 计算，避免递归。自定义评级优先于配偶豁免。

**统计范围（9 大可兴趣技能）：** 射击、近战、社交、手工、建造、艺术、烹饪、种植、采矿。

**特殊天赋特质来源：**
- 原生（Core）：`TooSmart`（博闻强识）
- 异象（Anomaly DLC）：`Joyous`（开心果）、`BodyMastery`（极致体能）、`VoidFascination`（痴迷虚空）、`Occultist`（神秘学者）、`Disturbing`（怪诞不经）
- 未加载 Anomaly DLC 时这些特质查询返回 null 自动跳过，不影响判定。

### 评级方法分层

`CombatEvaluator` 提供三个评级查询入口，按用途区分：

| 方法 | 自定义评级 | 配偶豁免 | 用途 |
|------|-----------|---------|------|
| `GetCombatTier` | 优先 | 含 | 全局重配排序、工作分配评级分档（自定义优先，与面板标签一致） |
| `GetSystemTier` | 不含 | 含 | 评级标签 Nick 前缀、ITab 面板"当前档次"显示、AEDebug.Label、腰带/副武器排序 |
| `GetAutoCombatTier` | 不含 | 不含 | 配偶豁免内部递归调用，避免无限递归 |

### 自定义评级识别码（玩家可调）

玩家可在殖民者装备面板（ITab）"自定义评级识别码"区为指定殖民者手动指定档次，**跳过自动公式计算**。

| 操作 | 入口 | 说明 |
|------|------|------|
| 设置自定义档次 | 面板内"设置自定义档次"按钮 | 弹出 SSS/SS/S/A/B/C/D/X 选项 FloatMenu，选定后写入存档 |
| 清除自定义档次 | 面板内"清除自定义"按钮 | 移除自定义条目，恢复自动判定 |

- **存档格式**：`List<string>`，元素格式 `档次#Pawn名字`，如 `S#王五`
- **运行时**：解析为 `Dictionary<名字, CombatTier>` 供快速查询
- **DEBUG 显示**：命中自定义评级的 Pawn 在面板与日志中显示 `S(A)#王五`（系统档 S 在前，括号内 A 为玩家指定档）；自动档仅显示 `S#王五`
- **面板对比**：面板"当前档次"行直接显示完整识别码，括号区分自定义与系统档
- **排序规则**：自定义评级档次映射为代表分（D=5, C=15, B=25, A=50, S=80, SS=95, SSS=110, X=-1）+0.5 微量偏向，让同档自定义略优先于同档自动

### 全局人物评级标签（Nick 改名 + 殖民者栏重排）

面板底部"人员自动评级"勾选框控制评级标签的自动应用：

- **勾选时**：立即执行一次评级应用，并启用自动执行（每 3000 tick + 新增殖民者时立即触发）
- **取消勾选时**：清除所有殖民者（含食尸鬼）Nick 上的评级前缀，恢复原名；保留殖民者栏当前顺序不重置
- **默认勾选**

| 操作 | 效果 |
|------|------|
| 勾选 → 自动执行 | 所有殖民者 Nick 变为 `S#王五` `A#李四` 格式，并按 Mod 选项配置的默认排序重排殖民者栏 |
| 取消勾选 | 恢复原 Nick，从字典取原名或按前缀解析剥离；保留殖民者栏当前顺序不重置 |

**覆盖范围**：殖民者 + 食尸鬼（Anomaly DLC）。食尸鬼也按相同规则评级，玩家可一眼分辨其价值。排序仅作用于 `PawnsFinder.AllMaps_FreeColonists`（不含食尸鬼），通过 `pawn.playerSettings.displayOrder` 写入并 `Find.ColonistBar.MarkColonistsDirty()` 刷新。

### 殖民者栏默认排序（Mod 选项）

在 Mod 选项 → "默认排序" 里配置，`AESettings.defaultSortMode` 字段，存档键 `ae_defaultSortMode`，默认 `ByTierThenValue`。

| 排序模式 | 比较器 | 规则 |
|---------|--------|------|
| 不排序 | — | 仅应用前缀，保留殖民者栏原顺序 |
| 按评级+价值（推荐） | `ComparePawnByTierThenValueDesc` | 先按 `CombatTier` 降序 SSS→SS→S→A→B→C→D→X，同档内按 `ComputeCombatValue` 降序 |
| 按角色+评级 | `ComparePawnByRoleThenValueDesc` | 按角色分组，同角色内按评级降序 |
| 按战斗价值 | `ComparePawnByCombatValueOnlyDesc` | 纯按 `ComputeCombatValue` 降序，不区分评级（高技能和平主义者可能挤占前列） |

**按评级+价值的设计意图**：和平主义者（X 档）即使技能高也排在最右，避免挤占 S/A 档位置。

**角色排序优先级**（`GetRoleOrder`，用于"按角色+评级"模式）：

| 顺序 | 角色 | 说明 |
|------|------|------|
| 0 | Brawler | 前排格斗者 |
| 1 | Shooter | 后排射手 |
| 2 | Doctor | 医生 |
| 3 | Worker | 工人 |
| 4 | Pacifist | 和平主义者 |
| 5 | Hunter | 狩猎者 |
| 6 | Leader | 意识形态领袖 |
| 99 | Default | 未分类 |

**防双重前缀**：
- `CombatEvaluator.GetPawnLookupName` 会自动剥离 Nick 上的评级前缀返回纯净名，确保自定义评级查询仍能命中（玩家设置时用的是原名），面板"当前档次"行拼接出 `S#王五` 而非 `S#S#王五`
- `AEDebug.Label` 在 Nick 已带前缀时直接返回 LabelShort，不再拼接

**持久化**：原名字典 `tierTagOriginals` 通过 `ae_tierTagOriginals` 存档（`List<string>` 格式 `thingIDNumber|原Nick`），重启后仍能恢复原名，避免误剥离玩家手动改的 Nick。

### 战斗价值公式（玩家可调）

```
战斗价值 = (射击等级 × 射击兴趣乘数 + 近战等级 × 近战兴趣乘数) × 技能权重 + Σ特质加分
```

所有参数均可在 Mod 选项内通过滑块调整，存档保存：

| 参数 | 默认 | 范围 | 含义 |
|------|------|------|------|
| 无火兴趣乘数 | 1.0 | 0.1 ~ 3.0 | 无火焰时技能等级权重 |
| 单火兴趣乘数 | 1.5 | 0.1 ~ 3.0 | Minor 兴趣时技能等级权重 |
| 双火兴趣乘数 | 2.0 | 0.1 ~ 3.0 | Major 兴趣时技能等级权重 |
| 技能整体权重 | 1.0 | 0.1 ~ 3.0 | 技能分整体缩放 |
| 坚韧（Tough）加分 | +30 | 0 ~ 100 | Tough 特质加分 |
| 乱开枪加分 | -15 | -50 ~ 0 | ShootingAccuracy degree=-1 |
| 冷枪手加分 | +15 | 0 ~ 50 | ShootingAccuracy degree=+1 |

### 价值评分（ComputePawnValueScore）

用于 ITab 面板"价值评分"徽章展示，综合反映殖民者培养深度。评分公式：

```
综合价值分 = 特质数量 × 5 + Σ(兴趣分) + Σ(技能等级)
```

| 维度 | 计分 | 说明 |
|------|------|------|
| 特质数量 | 每条 +5 分 | 玩家培养投入越多价值越高，原生上限 3 条 = 15 分 |
| 兴趣分 | Major=2, Minor=1, None=0 | 9 大核心技能求和：射击/近战/社交/手工/建造/艺术/烹饪/种植/采矿 |
| 技能等级 | 直接加 Level（0-20） | 9 大核心技能求和，最高 9×20=180 分 |

**典型分数范围**：
- 全满级全双火满特质殖民者：15 + 18 + 180 ≈ 213 分
- 新手殖民者（无火无技能无特质）：0 分
- 命中自定义评级：采用档位代表分（D=5, C=15, B=25, A=50, S=80, SS=95, SSS=110）+0.5 微量偏向

## 自动工作分配（AutoWork）

`AutoWork/WorkAllocator.cs` 提供多遍协调分配 + 工作计数跟踪的工作优先级自动分配。
所有技能类工作复用统一 `AssignWorkType` + `WorkAllocationConfig` 四大原则分配，通过数据驱动的 `skillWorkPhases` 阶段列表按固定顺序执行，前排分配结果影响后排候选排序（通过工作计数实现均衡负载）。

### 统一四大原则

所有技能类工作（重要专业/普通专业/次级专业/研究）共用统一分配 API，配置由 `WorkAllocationConfig` 结构编码：

1. **保证数量**：`GuaranteeCount` 确保至少 N 人承担（无论有无火），top N 内双火给 `GuaranteeMajorPriority`、单火给 `GuaranteeMinorPriority`、无火给 `GuaranteeNonPassionatePriority`
2. **三因子排序**：top N 人选按 Passion 降序 → SkillLevel 降序 → WorkCount 升序选择，保证数量内选兴趣最高、技能最强的
3. **有火保底**：超出 guarantee 的双火/单火者分别给 `FloorMajorPriority`/`FloorMinorPriority` 保底优先级，保留生产能力
4. **无火者**：超出 guarantee 的无火者直接给 `FloorNonPassionatePriority`（通常为0，规则要求"无火优先级0"）
5. **技能等级保底**：相关技能等级 ≥12 者 priority 不低于 2，≥8 者不低于 3（`ApplySkillFloor`），即使无火也以 priority=2/3 参与工作，不被完全排除。适用于超出 guarantee 的无火者（0 → 2/3）、保底内的无火者（3 → 2，仅≥12）与非候选满载者（0 → 2/3）

**workCount 硬上限**：每人最多承担 `MaxCoreWorkCount=3` 项 priority≤2 的专业工作。候选收集**包含满载者**，让满载者参与排序：满载者在 top N 内走 Floor 保底（不抢占 Guarantee），无火者落在 top N 外走 Floor(=0)，避免满载者被跳过后候选无火者错误获得 Guarantee 保底导致重复承担。满载者有火者仍给 Floor 保底（双火/单火），无火者给0——避免高技能有火者被硬上限完全排除。若候选不足保证人数（小殖民地人手不足），回退放宽模式下满载者走 Guarantee 逻辑，保证保底人数不失效。

**Crafting 技能组分配**：Crafting（制作）/Smithing（锻造）/Tailoring（缝制）三个工作类型都关联 Crafting 技能，通过 `AssignWorkGroup` **一次排序、同时分配**相同优先级，共享 1 个 workCount。避免分三次独立排序导致 workCount 变化影响后续排序、手工工作分散给不同人。

**奴隶处理**：奴隶与殖民者同流程，按兴趣/技能参与分配，无特殊优先级。辅助工作也按评级统一分档，无奴隶特殊处理。

### 分配规则

工作类型按以下分类与顺序分配（顺序影响工作计数，前排分配结果影响后排候选）：

| 顺序 | 工作分类 | 包含类型 | 保底 | 双火 | 单火 | 无火(top N) | 无火(超出) | 特殊约束 |
|------|---------|---------|------|------|------|-------------|------------|---------|
| 1 | 紧急 | Firefighter / Patient / PatientBedRest | — | 1 | 1 | 1 | — | 不计入 workCount |
| 2 | 重要专业 | Doctor / Warden / Childcare / Cooking / PlantCutting | 2 | 1 | 2 | 3 | 0 | — |
| 3 | 普通专业 | Construction / Mining / Growing / Smithing / Tailoring / Crafting / Art | 2 | 2 | 3 | 3 | 0 | Crafting 组分配共享 1 workCount |
| 4 | 次级专业 | Handling / Fishing / Hunting | 2 | 2 | 4 | 3 | 0 | Hunting 需远程武器+后排排序 |
| 5 | 研究 | Research / DarkStudy | 1 | 2 | 3 | 0 | 0 | 最后分配 |
| 6 | 辅助 | Hauling / Cleaning / BasicWorker 等 | — | 见辅助工作规则 | — | — | — | 不计入 workCount，按评级分档 |

**双火/单火**：对应 Passion.Major / Passion.Minor，整个候选列表中（含超出保底）双火/单火分别给"双火"/"单火"列优先级。

**无火(top N)**：保底人数内按三因子排序选取，无火者给此优先级（保底3，实现"保底2人即使无火也3"）。

**无火(超出)**：超出保底人数的无火者给此优先级（0，实现"新增更适合者原保底者降至0"）。**例外**：相关技能 ≥12 者保底 priority=2，≥8 者保底 priority=3（`ApplySkillFloor`），高技能无火者仍参与工作。

**工作计数**：跟踪每 Pawn 的 priority ≤ 2 的专业工作数量（紧急/辅助不计入）。
用于「同等兴趣下优先安排其他工作少的」实现均衡负载。
**硬上限**：每人最多 3 项 priority≤2 的专业工作，候选收集包含满载者参与排序。满载者在 top N 内走 Floor 保底（不抢占 Guarantee），无火者落在 top N 外走 Floor(=0)，避免重复承担。回退放宽模式（小殖民地）下满载者走 Guarantee 逻辑，保证保底人数不失效。
**Crafting 组分配**：Smithing/Tailoring/Crafting 三个工作类型通过 `AssignWorkGroup` 一次排序同时分配相同优先级，共享 1 个 workCount，视为 1 个专业工作。避免分三次独立排序导致 workCount 变化影响排序、手工工作分散给不同人。

**三因子排序**：Passion 降序 → SkillLevel 降序 → WorkCount 升序。
Passion 量化：None=0, Minor=1, Major=2。

**后排角色优先**（仅 Hunting）：通过 `RoleDetector.IsBackRow(role)` 判定，仅 `ArmorPreference.Flexible`（Shooter/Hunter/Leader）视为后排。
设计意图：后排角色应优先承担狩猎以练习射击能力。

**Hunting 需远程武器**：候选收集阶段过滤 `pawn.equipment?.Primary?.def.IsRangedWeapon != true` 的殖民者（未装备武器 / 装备近战武器 / 装备非武器均排除）。
设计意图：避免无远程武器者被分配狩猎工作。Fishing 虽属次级专业但关联 Animals 技能，不要求远程武器。

**循环依赖规避**：Hunting 始终设为 2 或 4，绝不设为 1，因此不会污染 `RoleDetector.DetectRole` 的 Hunter 判定（其依赖 Hunting priority == 1）。

### 辅助工作规则（搬运/清洁/非技能）

辅助工作（Hauling / Cleaning / BasicWorker 等无 relevantSkills 的工作）不使用 `WorkAllocationConfig`，按 `CombatTier` 评级分档分配：

- **评级 SSS/SS/S → priority=4**：高价值殖民者少做辅助工作
- **评级 A → priority=3**
- **评级 B/C/D/X → priority=1**
- **搬运豁免**：Hauling priority=1 会先于研究(priority>=2)执行从而打断研究，故 Pawn 的 Research/DarkStudy 优先级 >= 2 时，Hauling 降为 priority=4

辅助工作不计入 workCount（避免污染技能工作的均衡负载计算）。

**奴隶处理**：奴隶辅助工作固定 priority=1（奴隶作为劳动力主要承担搬运/清洁），专业工作按上述标准规则分配。

### 自定义优先级自动启用

执行全局工作重配时，若 `Find.PlaySettings.useWorkPriorities` 未启用，自动启用为 true，否则 1-4 优先级系统不生效。

### 入口

- **MOD 选项** → 启用/禁用"工作自动配置"（`AESettings.autoWorkEnabled`，默认勾选）
- **殖民者装备面板（ITab）底部** → "工作自动配置"勾选框
  - **勾选时**：立即执行一次工作重配，并启用自动执行（殖民者增减时标记待触发，冷却+非战斗中执行）
  - **取消勾选时**：仅停止自动执行，保留当前工作分配（工作优先级无法撤销）
  - **默认勾选**

## 自动执行（AutoExecutor）

`Core/AutoExecutor.cs` 静态类负责工作重配（事件驱动）与人员评级（周期触发）的自动执行，以及高价值非殖民者标记的 ITab 勾选消息提示。

- **入口**：由 `AutoEverythingGameComponent.GameComponentTick` 每 tick 调用 `AutoExecutor.TryTick()`。GameComponent 通过 Harmony Postfix on `Game.FinalizeInit` 在新游戏/加载存档后自动注册
- **静态门控**：每 60 tick 检查一次殖民者数量变化与周期触发
- **周期触发**：人员评级每 3000 tick（约 50 秒）执行一次；工作重配为事件驱动，无周期触发；高价值标记为实时绘制，无周期执行
- **殖民者数量变化检测**：`PawnsFinder.AllMaps_FreeColonists.Count` 增加或减少 → 标记 `work.pending` 待触发（不立即执行）。增加时额外触发评级（仅更新 Nick 前缀，不打断 Job）。工作重配延迟到冷却 2500 tick 结束且 `AnyCombatActive()` 返回 false（地图无未 Downed 敌对 Pawn）时才真正执行。延迟机制避免战斗中死亡连锁触发 `ReallocateAll`，打断医生正在执行的手术/治疗 Job。ITab 手动勾选（`TriggerWorkNow`）不受冷却限制，立即执行
- **首次初始化守卫**：`work.lastTick`/`lastTierTick` < 0 时设为当前 tick 不触发，避免存档加载误触发
- **错误隔离**：工作、评级、星标各自独立 try-catch + `Log.ErrorOnce`，salt 独立（Work=0xA200 / Tier=0xA300 / Mark=0xA500）
- **自动周期路径不弹消息框**（避免刷屏），仅走 `AEDebug.Log`；手动触发路径弹 `Messages.Message` 给玩家反馈

### 人员自动评级

- **触发**：周期 3000 tick + 新增殖民者立即触发 + ITab 勾选时立即触发
- **机制**：
  - 周期/事件触发：调用 `AESettings.ApplyTierTagsToAllPawns()`，仅更新 Nick 前缀，不重排殖民者栏（避免覆盖玩家手动排序）
  - 玩家主动触发（ITab 勾选）：调用 `AESettings.ApplyTierTagsWithDefaultSort()`，给所有殖民者（含食尸鬼）Nick 加上系统评级前缀（格式 `S#王五`），并按 Mod 选项配置的默认排序重排殖民者栏
- **取消勾选**：调用 `ClearTierTagsFromAllPawns()`，清除所有评级前缀恢复原名
- **入口**：殖民者装备面板（ITab）底部 → "人员自动评级"勾选框（`AESettings.autoTierTag`，默认勾选）

### 高价值非殖民者标记（AutoMarkPawn）

`AutoMarkPawn/PawnMarker.cs` 静态类为 S+ 档次非殖民者人类（S/SS/SSS，含自定义评级覆盖）头顶实时绘制鲜艳红色星标 `★`，便于玩家一眼识别高价值目标，优先俘虏或警惕。

- **判定**：`CombatEvaluator.GetCombatTier(pawn) >= CombatTier.S`（含自定义评级覆盖）
- **标记范围**（非殖民者人类，`PawnMarker.IsMarkableTarget`）：
  - 敌对派系敌人（来袭突袭/袭营的敌方 Pawn）
  - 友好派系访客（来访的 Visitor）
  - 交易者（派系/轨道交易商）
  - 野生人类/难民/流浪者
  - 倒下（Downed）的仍标记：便于优先俘虏高价值敌人
  - 殖民者与食尸鬼不标记
- **标记方式**：
  - 头顶世界图标：Harmony Postfix on `PawnUIOverlay.DrawPawnGUIOverlay`
  - 世界坐标 `pawn.DrawPos` 上方约 1.8 格 → 屏幕坐标 → GUI 坐标（Y 轴翻转）
  - 颜色 `Color(1.0f, 0.15f, 0.15f)` 鲜艳红色，`GameFont.Medium` 字号
  - 不修改任何 Pawn 的 Nick/Name，纯前端绘制，安全可逆，无存档副作用
- **触发**：
  - 实时绘制：Harmony 补丁每帧调用（`DrawPawnGUIOverlay` 由游戏每帧触发）
  - ITab 勾选时：统计当前非殖民者高价值对象数量并弹消息提示
  - 周期路径不执行（无 Nick 修改，无需周期刷新）
- **取消勾选**：头顶图标由 Harmony 补丁实时检查 `AESettings.autoMarkPawn` 开关，自动停止绘制
- **Harmony 补丁降级**：`PawnUIOverlay` 类型或 `DrawPawnGUIOverlay` 方法缺失时仅 `Log.Warning`，星标不显示但不崩溃
- **入口**：殖民者装备面板（ITab）底部 → "高价值标记"勾选框（`AESettings.autoMarkPawn`，默认勾选）

## 架构模型

### 目录结构

```
Source/AutoEverything/
├── AutoEverything.csproj                  # C# 7.3 项目文件
├── Core/                                  # → namespace AutoEverything.Core
│   ├── ModController.cs                   # MOD 入口，StaticConstructorOnStartup
│   ├── HarmonyPatches.cs                  # Harmony 补丁：GameComponent 注册 + 星标绘制
│   ├── AutoEverythingMod.cs               # Mod 设置入口
│   ├── AutoEverythingGameComponent.cs     # GameComponent：AutoExecutor Tick 入口
│   ├── AESettings.cs                      # ModSettings 持久化 + 设置窗口
│   ├── ColonistBarSortMode.cs             # 殖民者栏排序枚举
│   ├── DLCCompat.cs                       # DLC API 安全包装
│   ├── AEDebug.cs                         # AEDebug 日志工具
│   ├── PawnSuitabilityChecker.cs          # Pawn 适配性过滤
│   ├── PawnJobGuard.cs                    # 医疗/休养守卫（避免打断手术/休养）
│   ├── AutoExecutor.cs                    # 自动执行调度器（评级/工作/星标）
│   └── CombatTier.cs                      # 战斗价值档次枚举
├── RoleEvaluation/                        # → namespace AutoEverything.RoleEvaluation
│   ├── PawnRole.cs                        # 角色检测 + ArmorPreference（用于 IsBackRow 狩猎判定）
│   ├── GearContext.cs                    # 情境检测（仅 ITab 徽章展示）
│   ├── PawnStateCleaner.cs                # Pawn 状态清理工具
│   ├── CombatEvaluator.cs                 # 战斗价值/评级计算
│   └── PassionHelper.cs                  # VSE 兼容 passion tier 映射
├── AutoWork/                              # → namespace AutoEverything.AutoWork
│   ├── WorkAllocator.cs                   # 工作优先级自动分配
│   └── WorkAllocationConfig.cs            # 分配配置结构
├── AutoMarkPawn/                          # → namespace AutoEverything.AutoMarkPawn
│   └── PawnMarker.cs                      # 高价值非殖民者标记（S+ 头顶红色星标实时绘制）
└── UI/                                    # → namespace AutoEverything.UI
    └── ITab_GearManager.cs                # 殖民者检视面板（角色/情境/评级徽章 + 自定义评级 + 勾选框）
```

**模块职责说明：**
- **Core**：基础工具与全局状态（MOD 入口、GameComponent、Harmony 补丁、设置、调试、DLC 兼容、Pawn 适配性、医疗守卫、自动执行调度、战斗价值档次）
- **RoleEvaluation**：角色与情境评价（角色检测、情境检测、战斗价值评估、状态清理、VSE 兼容）
- **AutoWork**：工作优先级自动分配
- **AutoMarkPawn**：高价值非殖民者标记（S+ 档次头顶红色星标实时绘制）
- **UI**：玩家界面（ITab 面板）

未来扩展（自动机械族/自动训练等）可在 `Source/AutoEverything/` 下新增独立模块文件夹，按上述命名空间约定扩展。

### 评估周期

| 路径 | 周期 | 说明 |
|------|------|------|
| `AutoEverythingGameComponent.GameComponentTick` | 每 tick | 调用 `AutoExecutor.TryTick()`；GameComponent 通过 Harmony Postfix on `Game.FinalizeInit` 在新游戏/加载存档后自动注册 |
| `AutoExecutor` 殖民者检查 | 60 tick | 殖民者数量增减时标记 `work.pending`；增加时立即触发评级+星标 |
| `AutoExecutor` 工作重配 | 事件驱动 + 冷却 2500 tick + 战斗过滤 | 殖民者增减时标记待触发，冷却结束且 `AnyCombatActive()`=false（无敌对 Pawn）才执行；ITab 手动勾选时立即执行。避免战斗中死亡连锁打断手术 |
| `AutoExecutor` 人员评级 | 3000 tick | 周期 + 新增殖民者 + ITab 勾选时触发 |
| `AutoExecutor` 高价值标记 | 实时（Harmony 补丁） | S+ 档次非殖民者人类头顶绘制红色星标；ITab 勾选时统计数量弹消息，取消勾选自动停止绘制 |
| 角色缓存 | `RoleCacheInterval`（2500 tick） | 避免每 tick 重复检测 |
| 检视面板缓存 | 60 tick | ITab 角色徽章/数值摘要刷新 |
| 死亡 Pawn 字典清理 | 60000 tick | `RoleDetector`/`ContextDetector` 残留条目清理 |

## 设计原则：逻辑杜绝而非事后清理

食尸鬼（Anomaly DLC 变异体）、动物、机械族等不适用类别**绝不进入**自动管理流程：

| 入口 | 防御 |
|------|------|
| `PawnSuitabilityChecker.CanManageGear` | ITab 可见性与工作分配前过滤，仅 `race.Humanlike` 通过 |
| `GameComponent` 入口 | 全局单例，零 ThingDef.comps 注入，从源头杜绝与其他 MOD 的 Comp 注入冲突 |

**医疗/休养守卫**：全局工作重配入口 `WorkAllocator.ReallocateAll` 调用 `PawnJobGuard.ShouldSkipForMedical(pawn)` 跳过正在执行医疗工作（治疗/手术/救援）或卧床休养的殖民者，避免 `SetPriority` 取消手术 Job 导致手术死循环或重伤者死亡。

## 奴隶处理

奴隶（Ideology DLC）参与自动工作分配（作为殖民地劳动力）：

| 流程 | 奴隶处理 |
|------|---------|
| `WorkAllocator.ReallocateAll` 工作重配 | **奴隶参与分配**（通过 `map.mapPawns.SlavesOfColonySpawned` 收集） |
| 辅助工作 | 奴隶固定 priority=1（承担搬运/清洁） |
| 专业工作 | 按兴趣/技能标准规则分配 |

**奴隶收集**：`mapPawns.FreeColonistsSpawned` 不含奴隶，需单独遍历 `mapPawns.SlavesOfColonySpawned`。无 Biotech DLC 时该方法返回空列表，不影响无 DLC 环境。

## 性能约束

遵循 RimWorld MOD 开发的高性能约定：

- Tick 路径禁止 LINQ、禁止 `new List<>()`、禁止 `OrderBy`
- 集合用静态缓存或实例字段复用
- 评估日志走 `AEDebug.Log`，受 `debugLogging` 开关短路
- 可疑评分日志用 `Log.WarningOnce` 防刷屏

## 兼容性

- DLC API 调用前必须 `ModsConfig.XActive` 检查
- `DefDatabase.GetNamed` 后必须 null 检查
- 仅依赖 Harmony，无其他 MOD 依赖
- 兼容外星人 MOD（任何 `race.Humanlike` 种族）
- 支持存档中途添加

### Vanilla Skills Expanded（VSE）兼容

启动时反射检测 VSE 是否加载，构建 passion → tier 映射，运行时 O(1) 查询，无 Tick 路径反射开销。无 VSE 时仅处理原版 3 档（None/Minor/Major），行为与原版完全一致。

VSE 扩展的 6 种 passion 按以下规则统一处理：

| VSE passion | 中文名 | PassionTier | 处理规则 |
|-------------|--------|-------------|----------|
| `VSE_Apathy` | 冷漠 | -1 | 视为"无火及以下"：战斗价值乘数 = 无火 × 0.5；不参与评级 Major/Minor 计数；研究型判定满足"无火及以下" |
| `None` | 无火 | 0 | 原版无火 |
| `Minor` | 单火 | 1 | 原版单火 |
| `Major` | 双火 | 2 | 原版双火 |
| `VSE_Natural` | 自然 | 2 | 等同双火：战斗价值乘数 = 双火；评级 Major 计数；角色检测按双火处理 |
| `VSE_Critical` | 临界 | 3 | 高于双火：战斗价值乘数 = 双火 × 1.5；评级 Major 计数；综合价值评分兴趣分 = 3 |

设计要点：
- **Apathy 不加分**：评级兴趣分按 tier 累加（Minor=1, Major=2, Critical=3），Apathy(-1) 与 None(0) 均不加分
- **Major 计数含 Natural/Critical**：评级判定中 `tier >= Major` 含 Natural/Critical，符合"按双火处理"
- **Minor 计数不含 Major 及以上**：避免双计数，`tier == Minor` 严格匹配
- **反射失败降级**：VSE 检测异常时降级为原版 3 档，不阻断主功能

## 本地化

中英文双语，面向玩家的字符串均通过 `"Key".Translate()` 获取，禁止硬编码。

## 图片资源

| 资源 | 路径 | 用途 |
|------|------|------|
| Preview | `About/Preview.png` | Steam Workshop 预览图 |
| ModIcon | `Textures/UI/Icons/ModIcon.png` | Mod 列表图标（`About.xml` 的 `modIconPath`） |
| 评级徽章 | `Textures/UI/Icons/Tier/Tier_{SSS,SS,S,A,B,C,D,X}.png` | ITab 评级徽章，替代纯色块（SS/SSS 暂无图，回退纯色块） |
| 角色徽章 | `Textures/UI/Icons/Role/Role_{Brawler,Shooter,Doctor,Hunter,Worker,Pacifist,Leader,Default}.png` | ITab 角色徽章，左侧图标 + 右侧角色名 |

### 资源加载时机

`ITab_GearManager` 标记 `[StaticConstructorOnStartup]`，纹理通过 `LongEventHandler.ExecuteWhenFinished` 延迟到主线程加载完成后填充，避免跨线程 `ContentFinder` 访问崩溃：

- **禁止**在普通类的静态字段初始化器中调用 `ContentFinder`——类型首次访问可能在非主线程（DefDatabase 扫描、Harmony 反射），触发 `Tried to get a resource from a different thread` 异常
- `reportFailure=false` 时未找到返回 null，调用方处理 null 回退纯色块 + 文字
- 角色徽章因图标内无文字，绘制时在图标右侧显示中文角色名（`DrawRoleBadgeWithIcon`）

### ITab 面板文字防换行

中文文字超宽时 `Widgets.Label` 默认换行，会撑乱单行布局导致显示不全。本 MOD 强制约定：

- 所有徽章/标签/数值行绘制前 `Text.WordWrap = false`，绘制后恢复
- 标签宽度用 `Text.CalcSize(labelText).x + 留白` 动态计算，禁止固定宽度
- 超宽文字截断优于换行：截断只丢尾部，换行会撑乱整个布局
- 完整信息放 `TooltipHandler.TipRegion`，徽章/标签本身只做概览

## 构建

```bash
make check          # 验证零警告零错误（规则强制）
make build          # 构建
make rebuild-check  # 完整重建后检查
```

要求 `.NET` SDK 与 RimWorld 1.6 的 `Assembly-CSharp.dll` 引用路径已配置。

## 文档同步检查清单

修改以下任一代码/规则时，**必须同步更新本 README 对应章节**，否则视为未完成：

| 修改的代码 | 同步的 README 章节 |
|-----------|-------------------|
| `PawnRole.cs` / `RoleDetector` | `## 角色检测规则` 表格 |
| `GearContext.cs` / `ContextDetector` | `## 情境检测规则` 表格 |
| `CombatEvaluator.cs` 评级规则 | `## 全局价值评级档次（CombatTier）` 表格 |
| `CombatEvaluator.cs` 评级方法分层 | `### 评级方法分层` 表格 |
| `AESettings.cs` 自定义评级 | `### 自定义评级识别码` |
| `AESettings.cs` 评级标签 | `### 全局人物评级标签` |
| `AESettings.cs` 排序 | `### 殖民者栏默认排序` 表格 |
| `AESettings.cs` 战斗价值公式权重 | `### 战斗价值公式` 表格 |
| `CombatEvaluator.cs` ComputePawnValueScore | `### 价值评分` |
| `WorkAllocator.cs` 分配规则 | `## 自动工作分配` 分配规则表格与统一四大原则 |
| `WorkAllocator.cs` 奴隶收集/狩猎限制 | `## 奴隶处理` |
| `AutoExecutor.cs` | `## 自动执行（AutoExecutor）` + `### 评估周期` 表格 |
| `PawnMarker.cs` / `AutoMarkPawn` 模块 | `### 高价值非殖民者标记（AutoMarkPawn）` |
| `ITab_GearManager.cs` 底部勾选框 | `## 自动执行（AutoExecutor）` 入口章节 |
| `AutoEverythingGameComponent.cs` | `### 评估周期` 表格 + `## 设计原则：逻辑杜绝而非事后清理` |
| 设计原则（不适用 Pawn 处理） | `## 设计原则：逻辑杜绝而非事后清理` |
| 新增/删除源文件 | `### 目录结构` 代码块 |
| 新增/修改图片资源 | `## 图片资源` 表格与 `### 资源加载时机` |
| `ITab_GearManager.cs` 静态资源加载 | `### 资源加载时机` |
| `ITab_GearManager.cs` 文字绘制逻辑 | `### ITab 面板文字防换行` |

## 许可证

详见 [LICENSE](./LICENSE)。
