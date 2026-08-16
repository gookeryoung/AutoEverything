# Auto Everything

> 智能自动万物管理 MOD，适用于 RimWorld 1.6+
>
> packageId: `gookeryoung.autoeverything`

为殖民者自动执行**人员评级**、**工作优先级分配**、**高价值单位自动标记**、**自动携带物品**与**用药方案自动配置**，让玩家从繁琐的微调中解放出来。

> 装备管理使用 RimWorld 原生换装（玩家手动管理装备）。

零配置开箱即用，每个殖民者根据技能与特质自动识别角色与评级。

## 功能概览

| 模块 | 功能 | 触发方式 |
|------|------|----------|
| **AutoTier**（人员自动评级） | 按 SSS/SS/S/A/B/C/D/X 档次评级，可选应用评级前缀到 Nick 并重排殖民者栏 | 周期 3000 tick + 新增殖民者 + ITab 勾选 |
| **AutoWork**（工作自动配置） | 按工作类别与兴趣/技能多遍协调分配工作优先级 | 事件驱动（殖民者增减）+ 冷却 2500 tick + ITab 勾选 |
| **AutoMarkPawn**（高价值自动标记） | 殖民者栏角色定位图标（前排盾/远程弓/手工锤/贸易钱袋）+ 地图高价值标记（敌方/中立/野生 S+ 单位圆形标记）+ S+ 单位扫描消息通知 | 殖民者栏 Postfix + 地图 Postfix + 人员变动事件 + ITab 切换 |
| **AutoCarry**（自动携带） | 自动为殖民者背包补充食物 x3 + 药品（按药品政策"携带"列）+ S 档血清（强力+钢血），按优先级查找仓库可用物品 | 周期 6000 tick + ITab 勾选 |
| **AutoDrugPolicy**（用药方案自动配置） | 按 S/AB/CDX 评级自动创建并分配 3 套用药方案，药品"携带"列配置不同（活力水/清醒丸/思滞血清） | 周期 3000 tick + 新增殖民者 + ITab 勾选 |

## 设计思路

1. **角色驱动**：殖民者不是无差别的劳动力，而是有专长的个体。评级系统综合射击、近战、工作、社交、特质等维度评判全局价值，让玩家一眼分辨主力与辅助。
2. **情境感知**：同一殖民者在不同情境下角色不同。情境检测器识别战斗/工作/狩猎/寒冷/炎热等状态，供面板展示与工作分配参考。
3. **工作均衡**：工作分配按兴趣 → 技能 → 工作计数三因子排序，保证关键工作有人做、专业工作兴趣者优先、辅助工作按评级分档，避免高价值殖民者被杂务拖累。
4. **逻辑杜绝而非事后清理**：食尸鬼、动物等不适用类别在入口（ITab 可见性、工作分配前过滤）就被排除，绝不进入自动管理流程。
5. **安全可逆**：评级标签与角色定位图标均为纯前端展示，不修改 Pawn 核心数据；工作优先级可随时手动调整。

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
| **SSS** | 1. 乱开枪（ShootingAccuracy degree=-1）+ 坚韧（Tough）+ 射击双火<br>2. 坚韧（Tough）+ 格斗双火 + 敏捷（Nimble）或格斗者（Brawler）<br>3. 工作狂（Industriousness degree ≥ 1）+ 神经质（Neurotic degree ≥ 1）+ 3 个专业工作双火 | 顶级组合 |
| **SS** | 1. 乱开枪 + 射击双火<br>2. 坚韧 + 格斗双火<br>3. 工作狂 + 神经质 + 2 个专业工作双火 | 强化组合 |
| **S** | 1. 乱开枪 + 射击单火<br>2. 坚韧 + 格斗有火（Minor 或 Major）<br>3. 工作狂 + 神经质（组合即 S，无需工作双火）<br>4. 拥有任一特殊天赋特质：博闻强识（TooSmart）/开心果（Joyous）/极致体能（BodyMastery）/痴迷虚空（VoidFascination）/神秘学者（Occultist）/怪诞不经（Disturbing）<br>5. 沉鱼落雁（Beauty degree=2）+ 社交双火 | 全局高价值 |
| **A** | 不满足以上，但所有 9 大兴趣技能中至少 2 个双 Major + 1 个单 Minor 以上 | 多面手高价值 |
| **B** | 不满足以上，但所有 9 大兴趣技能中至少 1 个双 Major + 2 个单 Minor 以上 | 中等价值 |
| **C** | 其他情况（无特殊组合） | 普通价值 |
| **D** | 自动评级不产生此档（仅供玩家自定义评级手动设置） | 自定义低价值 |
| **X** | `WorkTagIsDisabled(WorkTags.Violent)` | 无法从事暴力活动（医疗/未成年等） |

**三大维度取最高档（MaxTier，不互斥）：** 乱开枪系列 / 坚韧格斗系列 / 工作狂神经质系列。

**专业工作技能（用于工作狂神经质系列判定）：** 手工、建造、艺术、烹饪、种植、采矿（共 6 项，统计 Major 数量）。

**负面特质不降档：** 用户决策（2026-07-26）移除原降档逻辑，S/SS/SSS 一律按命中档次标记，纵火狂（Pyromaniac）/脑子慢（SlowLearner）/脆弱（Wimp）/工作懒惰怠惰（Industriousness degree=-1/-2）等负面特质不再影响评级。

**配偶评级豁免：** 与 S 级以上（S/SS/SSS）人员结婚的殖民者，评级至少为 S（不降级 SS/SSS）。配偶评级用 `GetAutoCombatTier` 计算，避免递归。自定义评级优先于配偶豁免。

**名字评级着色：** 殖民者栏与地图名字标签按最终评级（含自定义）着色，S+ 统一紫色。

| 评级 | 名字颜色 |
|------|---------|
| SSS | 亮紫 |
| SS | 亮紫 |
| S | 亮紫 |
| A / B | 白色 |
| C / D | 灰色 |
| X | 原生浅灰（不覆盖） |

实现：Harmony Postfix `PawnNameColorUtility.PawnNameColorOf` 覆盖颜色。仅对玩家阵营人类 like 殖民者生效（排除囚犯/奴隶/精神状态，保留原生身份颜色）。

**统计范围（9 大可兴趣技能）：** 射击、近战、社交、手工、建造、艺术、烹饪、种植、采矿。

**特殊天赋特质来源：**
- 原生（Core）：`TooSmart`（博闻强识）
- 异象（Anomaly DLC）：`Joyous`（开心果）、`BodyMastery`（极致体能）、`VoidFascination`（痴迷虚空）、`Occultist`（神秘学者）、`Disturbing`（怪诞不经）
- 未加载 Anomaly DLC 时这些特质查询返回 null 自动跳过，不影响判定。

### 评级方法分层

`CombatEvaluator` 提供三个评级查询入口，按用途区分：

| 方法 | 自定义评级 | 配偶豁免 | 用途 |
|------|-----------|---------|------|
| `GetCombatTier` | 优先 | 含 | 全局重配排序、工作分配评级分档、评级标签 Nick 前缀、名字着色与加粗（自定义优先） |
| `GetSystemTier` | 不含 | 含 | ITab 面板"当前档次"显示、AEDebug.Label、腰带/副武器排序 |
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
| 2 | 重要专业 | Doctor / Childcare / Cooking / PlantCutting | 2 | 1 | 2 | 3 | 0 | — |
| 2.5 | 监管 | Warden | 2 | 1 | 2 | 3 | 3 | 无火者全员至少 3（囚犯管理需全员分担，避免仅保底 2 人管不住囚犯） |
| 3 | 普通专业 | Construction / Mining / Growing / Smithing / Tailoring / Crafting / Art | 2 | 2 | 3 | 3 | 0 | Crafting 组分配共享 1 workCount |
| 4 | 次级专业 | Handling / Fishing / Hunting | 2 | 2 | 4 | 3 | 0 | Hunting 需远程武器+后排排序 |
| 5 | 研究 | Research / DarkStudy | 1 | 2 | 3 | 0 | 0 | 最后分配；神秘学者强制 DarkStudy priority=1（绕过硬上限） |
| 6 | 辅助 | Hauling / Cleaning / BasicWorker 等 | — | 见辅助工作规则 | — | — | — | 不计入 workCount，按评级分档 |

**双火/单火**：对应 Passion.Major / Passion.Minor，整个候选列表中（含超出保底）双火/单火分别给"双火"/"单火"列优先级。

**无火(top N)**：保底人数内按三因子排序选取，无火者给此优先级（保底3，实现"保底2人即使无火也3"）。

**无火(超出)**：超出保底人数的无火者给此优先级（0，实现"新增更适合者原保底者降至0"）。**例外**：① 相关技能 ≥12 者保底 priority=2，≥8 者保底 priority=3（`ApplySkillFloor`），高技能无火者仍参与工作；② 监管（Warden）无火者全员 priority=3（`WardenConfig`），含满载者。

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

**神秘学者 DarkStudy 优先级覆盖**：用户决策（2026-07-21）神秘学者（Occultist 特质，Anomaly DLC）应优先承担暗黑调查工作。在 DarkStudy 分配阶段，所有神秘学者强制 priority=1，覆盖 ResearchConfig 默认优先级（双火 2/单火 3/无火 0），且绕过硬上限（即使满载也承担 DarkStudy）。设计意图：神秘学者天然契合暗黑调查，应优先发展该方向。无 Anomaly DLC 时 `TraitDefCache.Occultist` 为 null 安全跳过。

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

`Core/AutoExecutor.cs` 静态类负责工作重配（事件驱动）、人员评级（周期触发）、高价值标记扫描（事件驱动）与自动携带（周期触发）的自动执行。

- **入口**：由 Harmony Postfix on `TickManager.DoSingleTick` 每 tick 调用 `AutoExecutor.TryTick()`。不向存档注入 GameComponent（新存档零 MOD 组件写入，卸载 MOD 后加载更干净）；旧存档中残留的 `AutoEverythingGameComponent` 仍会调用 `TryTick`，60 tick 门控保证幂等无双倍执行
- **静态门控**：每 60 tick 检查一次殖民者数量变化、全人类单位数量变化与周期触发
- **周期触发**：人员评级每 3000 tick（约 50 秒）执行一次（走 `TierCacheService` 2500 tick 评级缓存）；自动携带每 6000 tick（约 100 秒）执行一次（战斗中暂停）；工作重配为事件驱动，无周期触发；角色定位图标由殖民者栏 Postfix 每帧绘制（判定结果按 Pawn 缓存 2500 tick，特质/技能变化最迟 42 秒内反映），S+ 高价值扫描为人员变动事件触发，无周期执行；战斗检测在同一检查周期内惰性单次计算，多分支共享结果
- **殖民者数量变化检测**：`PawnsFinder.AllMaps_FreeColonists.Count` 增加或减少 → 标记 `work.pending` 待触发（不立即执行）。增加时额外触发评级（仅更新 Nick 前缀，不打断 Job）。工作重配延迟到冷却 2500 tick 结束且 `AnyCombatActive()` 返回 false（无殖民者征召 + 无战斗 Job）时才真正执行。延迟机制避免战斗中死亡连锁触发 `ReallocateAll`，打断医生正在执行的手术/治疗 Job。ITab 手动勾选（`TriggerWorkNow`）不受冷却限制，立即执行
- **全人类单位数量变化检测**：`CountAllHumanlikeSpawned()`（含殖民者/奴隶/囚犯/敌对/中立/盟友/野生）增加时，若 `autoMarkPawn` 开启则立即调用 `ExecuteMark(resetTracking=false)` 扫描新增高价值目标，有新发现时弹消息
- **首次初始化守卫**：`work.lastTick`/`lastTierTick` < 0 时设为当前 tick 不触发，避免存档加载误触发
- **错误隔离**：工作、评级、标记、携带各自独立 try-catch + `Log.ErrorOnce`，salt 独立（Work=0xA200 / Tier=0xA300 / Mark=0xA500 / Carry=0xA400）
- **自动周期路径不弹消息框**（避免刷屏），仅走 `AEDebug.Log`；手动触发路径弹 `Messages.Message` 给玩家反馈

### 人员自动评级

- **触发**：周期 3000 tick + 新增殖民者立即触发 + ITab 勾选时立即触发
- **机制**：
  - 周期/事件触发：调用 `AESettings.ApplyTierTagsToAllPawns()`，仅更新 Nick 前缀，不重排殖民者栏（避免覆盖玩家手动排序）
  - 玩家主动触发（ITab 勾选）：调用 `AESettings.ApplyTierTagsWithDefaultSort()`，给所有殖民者（含食尸鬼）Nick 加上最终评级前缀（格式 `S#王五`，命中自定义评级时用自定义档），并按 Mod 选项配置的默认排序重排殖民者栏
  - 自定义评级变更：ITab 设置/清除自定义评级后立即调用 `ApplyTierTagsToAllPawns()` 更新前缀（不重排殖民者栏）
- **取消勾选**：调用 `ClearTierTagsFromAllPawns()`，清除所有评级前缀恢复原名
- **入口**：殖民者装备面板（ITab）底部 → "人员自动评级"勾选框（`AESettings.autoTierTag`，默认勾选）

### 高价值自动标记（AutoMarkPawn）

`AutoMarkPawn/` 模块包含两个职责：
- **角色定位图标**（`RoleIconDef.cs` + `RoleIconTextures.cs` + `HarmonyPatches.ColonistBarDrawer_DrawColonist_Patch`）：在殖民者栏 Rect 右上角绘制角色定位图标（坚韧盾/前排盾/远程弓/手工锤/工人扳手/贸易钱袋），基于特质与技能组合判定；判定结果按 Pawn 缓存 2500 tick（与评级缓存独立），命中路径仅 1 次字典查询 + 掩码展开（零分配）
- **S+ 高价值扫描通知**（`PawnMarker.cs`）：扫描所有人类单位中的 S+ 目标，通过消息通知玩家（不再绘制 ★）

#### 角色定位图标（6 种）

| 图标 | 形状 | 颜色 | 判定条件 | 设计意图 |
|------|------|------|----------|----------|
| **坚韧** | 带翼盾 | 深红 `RGB(0.6, 0.0, 0.0)` | 坚韧（Tough）特质 | 高生存力单位（减伤 50%），无论近战远程都值得标识 |
| **前排** | 盾 | 深红 `RGB(0.6, 0.0, 0.0)` | 坚韧（Tough）+ 格斗（Brawler 特质 或 近战 Major） | 高生存力近战单位，优先重甲 |
| **远程** | 弓箭 | 深红 `RGB(0.6, 0.0, 0.0)` | 乱开枪（ShootingAccuracy degree=-1）+ 射击有火（Major 或 Minor） | DPS 突出远程单位，优先射击任务 |
| **手工** | 锤子铁砧 | 深红 `RGB(0.6, 0.0, 0.0)` | 工作狂（Industriousness degree≥1）+ 神经质（Neurotic degree≥1） | 工作狂+神经质组合（精确 AND），生产效率突出，优先专业工作 |
| **工人** | 扳手螺丝刀 | 深红 `RGB(0.6, 0.0, 0.0)` | 工作狂（Industriousness degree≥1）或 严重神经质（Neurotic degree≥1） | 工作倾向单位（OR 关系）；Crafter(AND) 是其子集，工作狂+神经质会同时显示两个图标 |
| **贸易** | 钱袋 | 深红 `RGB(0.6, 0.0, 0.0)` | 俊俏/沉鱼落雁（Beauty degree≥1）+ 高社交（Social Major 或 Level≥8） | 社交优势，适合外交贸易 |

- **颜色统一深红**：用户决策（2026-07-21）原橙/绿/粉三色在殖民者栏小尺寸下看不清，统一深红色。形状本身已足够区分 6 种角色定位
- **坚韧一律标记**：用户决策（2026-07-21）带坚韧特质的角色一律标记 Tough 标识，与 Frontline 解耦——Tough 单独作为高价值特质标识，Frontline 仍保留为"坚韧+近战倾向"组合标识，两者可同时显示
- **远程判定扩展**：用户决策（2026-07-21）"乱开枪+射击单火"（S 档高价值）也标记为远程，原仅"乱开枪+射击双火"（SS/SSS 档）标记
- **工人与手工关系**：用户决策（2026-08-13）新增"工人"图标判定为 OR（工作狂或严重神经质），"手工"保留 AND（工作狂+神经质）。Crafter 是 Worker 的子集——工作狂+神经质的殖民者会同时显示手工（精确组合）+ 工人（工作倾向）两个图标
- **叠加显示**：一个殖民者符合多个角色定位时，所有图标从右往左横向排列（最多 6 个，单个 16×16 像素，间距 2px，右上角内缩 2px 留白）
- **纹理资源**：64×64 RGBA PNG 图标（白色形状 + 透明背景），位于 `Textures/UI/Icons/Role/Role_Tough.png` 等路径，由用户从 iconfont 下载的 SVG 转换而来。绘制时用 `GUI.color` 染色，无需为每种颜色单独制作图标
- **降级策略**：若外部 PNG 加载失败（ContentFinder 返回 null），自动回退到程序化生成的 32×32 像素纹理，确保 MOD 仍能正常运行
- **不依赖评级**：判定基于特质组合直接计算，不走 `TierCacheService`，避免缓存失效导致图标延迟刷新
- **覆盖范围**：殖民者栏中所有可见人类 Pawn（通过 `PawnSuitabilityChecker.CanManageGear` 过滤非人类like），不强制 Spawned（卧床/运输中的殖民者仍标记）

#### S+ 高价值扫描通知（PawnMarker）

`PawnMarker.cs` 静态类扫描所有人类单位中的 S+ 目标（S/SS/SSS，含自定义评级覆盖），通过消息通知玩家。

- **判定**：`CombatEvaluator.GetCombatTier(pawn) >= CombatTier.S`（含自定义评级覆盖，走 `TierCacheService` 共享 2500 tick 缓存）
- **扫描范围**（所有人类like 单位，`PawnMarker.IsMarkableTarget`）：
  - 殖民者（玩家阵营自由人员，含食尸鬼——食尸鬼属 Humanlike 通过过滤）
  - 奴隶（玩家阵营奴隶，Ideology DLC）
  - 囚犯（被玩家关押）
  - 敌对派系敌人（来袭突袭/袭营的敌方 Pawn）
  - 中立/盟友派系访客与交易者
  - 野生人类/难民/流浪者
  - 倒下（Downed）的仍标记：便于优先俘虏高价值敌人
- **类别**（`PawnMarker.GetMarkerCategory`）：用于消息展示中的类别名翻译（如"殖民者"/"敌对"）

- **触发**：
  - ITab 勾选切换（任一方向）：立即全局重扫描并弹消息列出所有当前高价值单位（`resetTracking=true` 清空已通知集合，所有目标都视为"新发现"）
  - 人员变动事件：全人类单位数量增加时扫描新增高价值目标，有新发现时弹消息提示（`resetTracking=false`，仅通知首次出现的目标）
  - 周期路径不执行（无 Nick 修改，无需周期刷新）
- **消息格式**（`PawnMarker.FormatMessage`）：标题 + 列表项 `- 类别 名字 (档位)`，超过 8 个时显示前 8 个加"... 等 N 个"；空列表显示"未发现高价值单位"
- **取消勾选**：`ExecuteMark` 检测 `autoMarkPawn=false` 后静默返回；殖民者栏图标由 Harmony 补丁实时检查开关自动停止绘制；`notifiedMarkedIds` 留待下次勾选时由 `resetTracking=true` 清空
- **Harmony 补丁降级**：`ColonistBarColonistDrawer.DrawColonist` 方法缺失时仅 `Log.Warning`，图标不显示但不崩溃。补丁优先级 `Priority.Last`，避免与其他 MOD 的同方法 patch 顺序冲突
- **入口**：殖民者装备面板（ITab）底部 → "高价值自动标记"勾选框（`AESettings.autoMarkPawn`，默认勾选）

#### 地图高价值标记（PawnUIOverlay Postfix）

`HarmonyPatches.PawnUIOverlay_DrawPawnGUIOverlay_Patch` 在地图上为非殖民者栏的高价值单位（敌方/中立/野生）绘制圆形标记 + 档位字母。

- **设计动机**：殖民者栏 patch 只覆盖玩家阵营单位（殖民者/奴隶/囚犯），敌方/中立/野生高价值单位在地图上没有任何可视标记。玩家反馈"标记高价值殖民者，没有标记到敌对方，只看到日志提示"——本 patch 在地图上为这些单位绘制可视标记
- **Hook 入口**：`PawnUIOverlay.DrawPawnGUIOverlay()` Postfix（RimWorld 原生绘制血条/状态 icon 的入口，此时 GUI.matrix 与坐标变换已完成）
- **覆盖范围**：仅非殖民者栏中的高价值单位
  - 跳过 `pawn.Faction == Faction.OfPlayer`（殖民者+奴隶，已在殖民者栏有角色定位图标）
  - 跳过 `pawn.IsPrisonerOfColony`（囚犯，已在殖民者栏有角色定位图标）
  - 剩余：敌方/中立/野生人类单位
- **判定**：`PawnMarker.IsHighValue(pawn)` 为 true（即 `TierCacheService.GetTier(pawn) >= CombatTier.S`，含自定义评级覆盖）
- **标记样式**：圆形背景（20×20 像素，程序化生成 32×32 纹理，边缘 alpha 渐变抗锯齿）+ 居中档位字母（S/SS/SSS，黑色 GameFont.Tiny）
- **颜色**（按 `PawnMarker.GetMarkerCategory` 类别染色）：
  - Enemy（敌对）= 红色 `RGB(0.85, 0.2, 0.2)`
  - Neutral（中立/盟友）= 青色 `RGB(0.2, 0.7, 0.85)`
  - WildHuman（野生人类）= 白色 `RGB(1, 1, 1)`
- **坐标变换**：`pawn.DrawPos + 头顶偏移(1.6f)` → 世界坐标 → `Find.Camera.WorldToScreenPoint(worldPos) / Prefs.UIScale` → `screenPos.y = Screen.height - screenPos.y`（Y 翻转，与 RimWorld 原生 PawnUIOverlay 一致）
- **Harmony 补丁降级**：`PawnUIOverlay.DrawPawnGUIOverlay` 方法缺失时仅 `Log.Warning`，地图标记不显示但不崩溃。补丁优先级 `Priority.Last`

## 自动携带（AutoCarry）

`AutoCarry/` 模块自动为殖民者背包补充生存物资，让玩家无需手动管理远征/战斗携带。

### 携带清单

| 物品 | 数量 | 过滤规则 | 设计意图 |
|------|------|----------|----------|
| 食物 | x3 | 始终携带 | 短期远征或战斗中的口粮 |
| 活力水（Luciferium） | 药品政策"携带"列 | 政策无条目/不允许/携带=0 时跳过 | 尊重玩家在药品政策中的配置，避免被游戏丢地上 |
| 清醒丸（WakeUp） | 药品政策"携带"列 | 不需要睡眠者跳过 + 政策有携带才带 | 尊重药品政策，不需要睡眠的殖民者带了也没用 |
| 思滞血清（Penoxycyline） | 药品政策"携带"列 | 政策有携带才带 | 尊重药品政策配置 |

**重要**：药品数量不再硬编码为 x1，必须读取药品政策 UI 中"携带"列的值（`takeToInventory`）。
RimWorld 药品政策系统每 tick 检查背包，实际数量超过"携带"列就会把多余的丢地上——硬塞 0 携带的药品必然立刻被丢弃。
玩家在"药品政策"里把活力水/清醒丸/思滞血清的"携带"列改成 ≥1，AutoCarry 才会去仓库补对应数量。

### 食物优先级

按以下顺序查找仓库可用物品，第一个有货的类型即为目标食物：

| 优先级 | DefName | 说明 |
|--------|---------|------|
| 0 | `MealSurvivalPack` | 包装食物（生存口粮）：不腐坏、便于远征 |
| 1 | `Pemmican` | 干粮：长久保存 |
| 2 | `MealLavish` | 奢侈餐 |
| 3 | `MealFine` | 精致餐 |
| 4 | `MealSimple` | 一般餐 |
| 5 | `MealNutrientPaste` | 营养糊（最低优先级兜底） |

缺失的 DefName（如 DLC 差异）静默跳过，自动降级到下一优先级。

### 适用范围过滤

- **食尸鬼**：跳过（用户明确要求"请勿为食尸鬼配置"）
- **机器人（机械族）**：跳过（通过 `PawnSuitabilityChecker.CanManageGear` 过滤，仅人类 like 通过）
- **奴隶**：跳过（用户决策"仅自由殖民者"）
- **医疗中/卧床休养**：跳过（复用 `PawnJobGuard.ShouldSkipForMedical`，避免打断手术）
- **仪式/聚会/商队组建参与中（含心灵仪式/婚礼/派对/授予仪式）**：跳过（`PawnJobGuard.IsInRitualOrGathering`，取药/取食会中断活动）
- **保护活动中**：跳过（`PawnJobGuard.IsDoingProtectedActivity`：睡眠/冥想/死眠/实体研究/玩家手动命令）
- **死亡/倒下**：跳过（无法去仓库拾取）
- **不需要睡眠者**：跳过清醒丸（`PawnCarryChecker.NeedSleep` 判定 `pawn.needs.rest == null`）
- **药品政策"携带"列为 0**：跳过对应药品（`PawnCarryChecker.GetDrugCarryCount` 反射读取 `DrugPolicyEntry.takeToInventory`；政策无条目/不允许/携带=0 都视为不带，避免被游戏丢地上）

### 派发机制

- **触发**：周期 6000 tick（约 100 秒）+ ITab 勾选时立即触发
- **战斗过滤**：复用 `AutoExecutor.AnyCombatActive()`，战斗中暂停派发
- **单次单 Pawn 单物品**：每周期每 Pawn 最多派发一个 `TakeInventory` Job，避免互相覆盖；缺其他物品下周期再处理
- **物品查找**：`map.listerThings.ThingsOfDef(def)` + 手动最近搜索，跳过 Spawned=false / Forbidden / 已被他人预约的目标（`map.reservationManager.CanReserve` 检查）/ 允许区域外的目标（`EffectiveAreaRestrictionInPawnCurrentMap`，殖民者划定允许区域后只在区域内拾取，防止跑出安全区）/ 迷雾中的目标（寻路不可达且拾取等于探雾作弊）
- **派发数量**：`Math.Min(缺失量, 目标堆叠数)`，避免一次拿空整个仓库堆
- **错误隔离**：单 Pawn 失败 `Log.ErrorOnce` 不影响其他 Pawn，salt=0xA400

### 入口

- **MOD 选项** → 启用/禁用"自动携带"（`AESettings.autoCarryEnabled`，默认关闭）
- **殖民者装备面板（ITab）底部** → "自动携带"勾选框
  - **勾选时**：立即执行一次携带分配（受战斗过滤），并启用周期自动
  - **取消勾选时**：仅停止自动派发，保留当前背包物品（不主动清空）
  - **默认关闭**（与评级/工作/标记的默认勾选不同，避免无需求玩家被自动派发 Job 打扰）
  - **持久化**：ITab 底部 5 个勾选框（评级/工作/星标/携带/用药）状态变化时立即调用 `ModSettings.Write()` 写盘（存档键 `ae_autoCarryEnabled` 等），重启游戏后保留；无需打开 Mod 选项窗口手动保存

## 用药方案自动配置（AutoDrugPolicy）

`AutoDrugPolicy/` 模块按殖民者评级自动创建并分配 3 套用药方案（DrugPolicy），与 AutoCarry 协同——AutoDrugPolicy 配置药品政策的"携带"列（takeToInventory），AutoCarry 读此字段去仓库补库存。

### 评级映射

| 评级范围 | 药品政策 | 政策内容 |
|----------|---------|---------|
| S / SS / SSS | **AE-S** | 社交用药 + 清醒丸1 + 活力水1 + 佩诺西林1（强力血清/钢血血清由 AutoCarry 直接携带） |
| A / B | **AE-AB** | 同 AE-S 配置 |
| C / D / X | **AE-CDX** | 社交用药 + 清醒丸1（魔鬼素不预支，活力水/佩诺西林仅 AB/S 档） |

**为什么 AB 与 S 政策内容相同**：强力血清（JuggernautSerum）与钢血血清（MetalbloodSerum）是 Anomaly DLC 的特殊血清，**没有 Comp_Drug 组件**，不属于 RimWorld 药品政策系统的"药品"（无法在药品政策 UI 中显示条目）。因此 S 档的血清由 AutoCarry 直接派发 TakeInventory Job 携带，DrugPolicy 只管理标准药品。

### RimWorld DrugPolicy 机制（反编译验证 2026-08-10）

- **默认初始化**：`new DrugPolicy(id, label)` 构造函数调用 `InitializeIfNeeded()`，为所有 `IsDrug` 物品创建默认条目（仅 `allowedForAddiction=true`，其他默认 false/0）
- **`takeToInventory` 是核心**：`Pawn_InventoryTracker.FirstUnloadableThing` 遍历 DrugPolicy，`takeToInventory>0` 的药品加入"保留列表"，背包中 `takeToInventory=0` 的药品会被自动卸下——这是"携带了会自己丢掉"的根因
- **自动补库存**：`JobGiver_MoveDrugsToInventory` 自动派发 TakeInventory Job，按 `takeToInventory - 当前背包数量` 补充
- **`takeToInventoryTempBuffer`**：UI 文本框缓冲，需同步设置避免政策 UI 显示空

### 基本配置（所有档共用）

3 套政策的基本配置均在 RimWorld 默认条目基础上修改字段（**不清空列表**，保留默认所有药品条目）：

| 药品 | allowedForJoy | allowedForAddiction | allowScheduled | daysFrequency | takeToInventory |
|------|--------------|--------------------|---------------|--------------|----------------|
| 啤酒（Beer） | true | true | false | — | 0 |
| 烟叶（SmokeleafJoint） | true | true | false | — | 0 |
| 精神茶（PsychiteTea） | true | true | **true** | **2** | **1** |
| 魔鬼素（Luciferium） | false | true | false | — | 0 |
| 清醒丸（WakeUp） | false | true | false | — | **1** |
| 薄片（Flake） | false | **false** | false | — | 0 |
| 亚咯（Yayo） | false | **false** | false | — | 0 |

**设计要点**：
- **危险成瘾品禁止**：薄片（Flake）与亚咯（Yayo）为 psychite 系强成瘾品，显式设 `allowedForAddiction=false`（覆盖 RimWorld 默认 true），成瘾者不再自动服用，强制戒断
- 计划服用（`allowScheduled`）只允许精神茶（2 天 1 次）与佩诺西林（AB/S 档，5 天 1 次）
- 魔鬼素不预支不计划服用（仅满足依赖，永久成瘾者每天自动服用）
- 清醒丸不计划服用但预支 1 个备用
- 其他成瘾品默认 `allowedForAddiction=true`（RimWorld 自带，满足依赖；薄片/亚咯除外）

### 叠加规则

AB/S 档额外叠加：

| 药品 | allowedForAddiction | allowScheduled | daysFrequency | takeToInventory |
|------|--------------------|---------------|--------------|----------------|
| 活力水（GoJuice） | true | false | — | **1** |
| 佩诺西林（Penoxycyline） | true | **true** | **5** | **1** |

- **CDX 档**：基本配置（不含活力水/佩诺西林）
- **AB 档**：基本 + 活力水 + 佩诺西林
- **S 档**：AB（血清由 AutoCarry 直接携带，不放入 DrugPolicy）

### 关键字段说明

`DrugPolicyEntry` 字段（探针验证全部公开）：

| 字段 | 类型 | 含义 |
|------|------|------|
| `drug` | ThingDef | 药品定义 |
| `allowedForJoy` | bool | 是否允许娱乐服用 |
| `allowedForAddiction` | bool | 是否允许满足依赖服用（RimWorld 默认 true） |
| `allowScheduled` | bool | 是否启用计划服用 |
| `takeToInventory` | int | **携带到背包的目标数量（>0 才不会被游戏自动卸下）** |
| `daysFrequency` | float | 计划服用频率（天） |
| `onlyIfMoodBelow` | float | 仅当心情低于此值时服用 |
| `onlyIfJoyBelow` | float | 仅当娱乐低于此值时服用 |
| `takeToInventoryTempBuffer` | string | UI 文本框缓冲（需同步 takeToInventory） |

### 政策生命周期

- **创建**：MOD 启动后首次启用 `autoDrugPolicyEnabled` 时，检查 DrugPolicyDatabase 中是否存在 `AE-S`/`AE-AB`/`AE-CDX` 三套政策，不存在则创建并按预设修改字段
- **幂等重置**：已存在的同名政策**重新填充字段**（覆盖旧内容，确保最新预设生效）——修复旧版本漏设字段的 bug
- **补全条目**：`FillPolicyEntries` 反射调用 `InitializeIfNeeded(false)` 补全缺失的 RimWorld 默认药品条目
- **保留列表**：不清空 entriesInt，只修改对应药品字段，其他药品保持 RimWorld 默认（`allowedForAddiction=true`）
- **分配**：按 `CombatEvaluator.GetCombatTier` 评级自动分配对应政策到 `pawn.drugs.CurrentPolicy`

### 触发方式

- **周期触发**：每 3000 tick（约 50 秒，与评级周期一致）自动重分配
- **事件触发**：殖民者数量增加时立即重分配（新增殖民者需分配对应政策）
- **ITab 勾选**：玩家在面板勾选时立即执行一次（弹消息框反馈）
- **不需战斗过滤**：分配 DrugPolicy 不取消 Job，可在战斗中安全执行

### RimWorld 1.6 API 兼容

`DrugPolicy.entriesInt` 字段与 `InitializeIfNeeded` 方法在编译 DLL 中为私有，统一用反射访问：
- `DrugPolicyPresets`：反射读取 `entriesInt` 字段、调用 `InitializeIfNeeded(false)` 补全条目、添加新条目到列表
- `AutoDrugPolicyManager`：反射读取 `DrugPolicyDatabase.policies` 列表，构造 `new DrugPolicy(id, label)` 后添加

### 入口

- **MOD 选项** → 启用/禁用"用药方案"（`AESettings.autoDrugPolicyEnabled`，默认关闭）
- **殖民者装备面板（ITab）底部** → "用药方案"勾选框
  - **勾选时**：立即创建政策并分配
  - **取消勾选时**：仅停止自动执行，已分配的政策不变
  - **默认关闭**
  - **持久化**：同 AutoCarry，开关状态变化时立即写盘（存档键 `ae_autoDrugPolicyEnabled`），重启游戏保留

## 架构模型

### 目录结构

```
Source/AutoEverything/
├── AutoEverything.csproj                  # C# 7.3 项目文件
├── Core/                                  # → namespace AutoEverything.Core
│   ├── ModController.cs                   # MOD 入口，StaticConstructorOnStartup
│   ├── HarmonyPatches.cs                  # Harmony 补丁：DoSingleTick 全局入口 + 角色定位图标 + 地图高价值标记
│   ├── AutoEverythingMod.cs               # Mod 设置入口
│   ├── AutoEverythingGameComponent.cs     # GameComponent：仅旧存档兼容（新存档不再写入，Tick 入口已改 DoSingleTick）
│   ├── AESettings.cs                      # ModSettings 持久化 + 设置窗口（主 partial）
│   ├── AESettings.TierTag.cs              # AESettings partial：评级标签应用/清除/排序
│   ├── ColonistBarSortMode.cs             # 殖民者栏排序枚举
│   ├── DLCCompat.cs                       # DLC API 安全包装（IsGhoul）
│   ├── AEDebug.cs                         # AEDebug 日志工具
│   ├── PawnSuitabilityChecker.cs          # Pawn 适配性过滤（仅 Humanlike 通过）
│   ├── PawnJobGuard.cs                    # 医疗/休养/仪式/聚会/保护活动守卫（避免打断手术/仪式/睡眠等）
│   ├── PawnCollector.cs                   # 殖民者+食尸鬼统一收集（AllManagedPawns）
│   ├── TierCacheService.cs                # 评级共享缓存（2500 tick TTL，自动 cleanup）
│   ├── TierTagHelper.cs                   # 评级前缀剥离工具
│   ├── TraitDefCache.cs                   # TraitDef 查询缓存
│   ├── PassionHelper.cs                   # VSE 兼容 passion tier 映射
│   ├── AutoExecutor.cs                    # 自动执行调度器（评级/工作/标记）
│   └── CombatTier.cs                      # 战斗价值档次枚举
├── RoleEvaluation/                        # → namespace AutoEverything.RoleEvaluation
│   ├── PawnRole.cs                        # 角色检测 + ArmorPreference（用于 IsBackRow 狩猎判定）
│   ├── GearContext.cs                     # 情境检测（仅 ITab 徽章展示）
│   └── CombatEvaluator.cs                 # 战斗价值/评级计算
├── AutoWork/                              # → namespace AutoEverything.AutoWork
│   ├── WorkAllocator.cs                   # 工作优先级自动分配（主 partial + WorkAllocationConfig 内嵌 struct）
│   ├── WorkAllocator.Assignment.cs        # WorkAllocator partial：单工作/组分配 + 辅助工作分配
│   └── WorkAllocator.Comparer.cs          # WorkAllocator partial：三因子排序比较器 + ApplySkillFloor
├── AutoMarkPawn/                          # → namespace AutoEverything.AutoMarkPawn
│   ├── PawnMarker.cs                      # S+ 高价值扫描通知（全人类单位扫描 + 消息通知）
│   ├── RoleIconDef.cs                     # 角色定位判定（前排/远程/手工/贸易 4 种 + 统一深红色常量）
│   └── RoleIconTextures.cs                # 角色定位纹理（程序化生成 4 个 32x32 RGBA 纹理）
├── AutoCarry/                             # → namespace AutoEverything.AutoCarry
│   ├── CarryPolicy.cs                     # 携带物品清单（食物优先级 + 药品数量常量 + CarryEntry struct）
│   ├── CarryAllocator.cs                  # 携带分配器（周期派发 TakeInventory Job）
│   └── PawnCarryChecker.cs                # 殖民者携带适用性检查（硬过滤 + 软过滤）
└── UI/                                    # → namespace AutoEverything.UI
    └── ITab_GearManager.cs                # 殖民者检视面板（角色/情境/评级徽章 + 自定义评级 + 勾选框）
```

**模块职责说明：**
- **Core**：基础工具与全局状态（MOD 入口、GameComponent、Harmony 补丁、设置、调试、DLC 兼容、Pawn 适配性、医疗/仪式/保护活动守卫、Pawn 收集、评级缓存、前缀工具、特质缓存、VSE 兼容、自动执行调度、战斗价值档次）
- **RoleEvaluation**：角色与情境评价（角色检测、情境检测、战斗价值评估）
- **AutoWork**：工作优先级自动分配（主分配器 + 分配 + 比较器三 partial）
- **AutoMarkPawn**：殖民者栏角色定位图标（前排/远程/手工/贸易 4 种，基于特质组合判定）+ S+ 全人类单位扫描消息通知
- **UI**：玩家界面（ITab 面板）

> AutoEquipment 模块（自动装备分配）已移除，改用 RimWorld 原生换装（玩家手动管理装备）。

未来扩展（自动机械族/自动训练等）可在 `Source/AutoEverything/` 下新增独立模块文件夹，按上述命名空间约定扩展。

### 评估周期

| 路径 | 周期 | 说明 |
|------|------|------|
| Harmony Postfix on `TickManager.DoSingleTick` | 每 tick | 调用 `AutoExecutor.TryTick()`；不向存档注入 GameComponent，旧存档残留组件双路调用由 60 tick 门控保证幂等 |
| `AutoExecutor` 殖民者检查 | 60 tick | 殖民者数量增减时标记 `work.pending`；增加时立即触发评级 |
| `AutoExecutor` 工作重配 | 事件驱动 + 冷却 2500 tick + 战斗过滤 | 殖民者增减时标记待触发，冷却结束且 `AnyCombatActive()`=false（无征召+无战斗 Job）才执行；ITab 手动勾选时立即执行。避免战斗中死亡连锁打断手术 |
| `AutoExecutor` 人员评级 | 3000 tick | 周期 + 新增殖民者 + ITab 勾选时触发 |
| `AutoExecutor` 全人类单位检查 | 60 tick | 全人类单位数量增加时立即触发 Mark 扫描，有新高价值目标时弹消息 |
| `AutoExecutor` 高价值标记 | 殖民者栏 Postfix 绘制 + 人员变动事件 | 殖民者栏 Rect 右上角绘制角色定位图标（前排盾/远程弓/手工锤/贸易钱袋，统一深红色）；S+ 单位扫描消息通知；与相机缩放解耦；ITab 切换时全局重扫描并弹消息；取消勾选自动停止绘制 |
| 角色缓存 | `RoleCacheInterval`（2500 tick） | 避免每 tick 重复检测 |
| 检视面板缓存 | 60 tick | ITab 角色徽章/数值摘要刷新 |
| 死亡 Pawn 字典清理 | 60000 tick | `RoleDetector`/`ContextDetector` 残留条目清理 |

## 设计原则：逻辑杜绝而非事后清理

动物、机械族、昆虫、异常实体等不适用类别**绝不进入**自动管理流程（食尸鬼属 Humanlike，按下面分模块策略处理）：

| 入口 | 防御 |
|------|------|
| `PawnSuitabilityChecker.CanManageGear` | 仅 `race.Humanlike` 通过（食尸鬼通过），用于 ITab 可见性、工作分配候选收集、AutoMarkPawn 标记目标判定 |
| `DLCCompat.IsGhoul` | 工作分配候选收集时跳过食尸鬼（食尸鬼不参与工作分配，但仍参与评级标签与高价值标记） |
| `TickManager.DoSingleTick` 入口 | Harmony Postfix 零拦截，零 ThingDef.comps 注入，零存档组件写入，从源头杜绝与其他 MOD 的 Comp 注入冲突与存档残留 |

**食尸鬼处理策略**（分模块）：评级标签应用（`AESettings.ApplyTierTagsToAllPawns` 经 `PawnCollector.AllManagedPawns` 收集食尸鬼）、高价值标记（`PawnMarker.IsMarkableTarget` 不排除食尸鬼，归为 Colonist 类别标金星）；工作分配（`WorkAllocator.ReallocateAll` 通过 `DLCCompat.IsGhoul` 跳过食尸鬼，因为食尸鬼不参与 RimWorld 工作系统）。

**医疗/休养守卫**：全局工作重配入口 `WorkAllocator.ReallocateAll` 调用 `PawnJobGuard.ShouldSkipForMedical(pawn)` 跳过正在执行医疗工作（治疗/手术/救援）或卧床休养的殖民者，避免 `SetPriority` 取消手术 Job 导致手术死循环或重伤者死亡。

**仪式/聚会守卫**：`PawnJobGuard.IsInRitualOrGathering(pawn)` 跳过仪式/聚会/商队组建参与者（Lord 的 `LordJob_VoluntarilyJoinable` 覆盖 Ideo 仪式/婚礼/派对/音乐会/演讲/授予仪式，`LordJob_PsychicRitual` 覆盖 Anomaly 心灵仪式，`LordJob_FormAndSendCaravan` 覆盖商队组建），`SetPriority`/`TryTakeOrderedJob` 取消 Job 都会中断活动；参与者下个周期（仪式结束后）补配。

**玩家命令守卫**：`pawn.CurJob?.playerForced == true`（玩家右键手动指派）时跳过工作重配与取药派发——玩家命令优先级最高，优先级变化触发的 Job 重评估会取消玩家命令。

**保护活动守卫**（仅 AutoCarry）：`PawnJobGuard.IsDoingProtectedActivity(pawn)` 跳过正常睡眠（`LayDown`）、冥想（`Meditate`/`MeditatePray`）、死眠（`Deathrest`）、实体研究（`StudyInteract`）——取药打断会丢 psyfocus/deathrest 加成与研究进度；吃饭（`Ingest`）不拦，原生 Job 也常打断吃饭属正常节奏。

## 奴隶处理

奴隶（Ideology DLC）参与自动工作分配（作为殖民地劳动力）：

| 流程 | 奴隶处理 |
|------|---------|
| `WorkAllocator.ReallocateAll` 工作重配 | **奴隶参与分配**（通过 `map.mapPawns.SlavesOfColonySpawned` 收集） |
| 辅助工作 | 奴隶固定 priority=1（承担搬运/清洁） |
| 专业工作 | 按兴趣/技能标准规则分配 |

**奴隶收集**：`mapPawns.FreeColonistsSpawned` 不含奴隶，需单独遍历 `mapPawns.SlavesOfColonySpawned`。无 Ideology DLC 时该方法返回空列表，不影响无 DLC 环境。

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

## 测试

```bash
make test           # 编译并运行控制台测试，无需进游戏
```

测试运行器位于 `Test/AutoEverything.Tests/`，零外部测试框架依赖（不使用 xUnit/NUnit），通过 `InternalsVisibleTo("AutoEverything.Tests")` 调用主程序的 `internal` 方法。

### 纯逻辑核心模式

RimWorld 运行时依赖 `Pawn`/`Map`/`Faction` 等游戏内对象，难以脱离游戏进程构造。为支持单元测试，所有可测纯逻辑统一抽取为 `*Core` 静态方法，输入参数为简单 struct 或基础类型：

| 模块 | 纯逻辑方法 | 输入 struct | 说明 |
| --- | --- | --- | --- |
| `CombatEvaluator` | `EvaluateAutoTierCore` | `TierEvaluationInput` | 自动评级档次判定 |
| `WorkAllocator` | `ApplySkillFloorCore` | `int priority, int skillLevel` | 工作优先级保底规则 |
| `PawnMarker` | `GetMarkerCategoryCore` | `PawnMarker.CategoryInput` | 派系/状态 → 类别判定 |
| `PawnMarker` | `FormatMessageCore` | `List<MessageEntry>` + 文本参数 | 高价值列表消息拼装 |
| `PawnMarker` | `ComputeNewlyMarkedIds` | `HashSet<int>` + `List<int>` | dedup 跟踪纯逻辑 |
| `TierTagHelper` | `Strip` / `HasPrefix` | `string` | 评级前缀剥离/识别（已是纯 public，无需 `*Core` 抽离） |
| `RoleDetector` | `GetArmorPreference` / `IsBackRow` / `GetRoleOrder` | `Role` | 角色 → 护甲偏好/后排/排序优先级（已是纯 public，无需 `*Core` 抽离） |

**约定**：

1. `*Core` 方法不引用 `Pawn`/`Verse`/`RimWorld` 类型，输入参数为 struct 或基础类型
2. 生产方法（`GetMarkerCategory(Pawn)` 等）从 Pawn 收集字段填入 struct，再转调 `*Core`
3. `*Core` 标记 `internal`，通过 `InternalsVisibleTo` 暴露给测试程序集
4. 性能敏感路径（如 `ScanAndMark`）保留内联实现，`*Core` 仅供测试，避免额外遍历分配

当前覆盖：196 个用例（含 `ApplySkillFloorCoreTests` 30 / `EvaluateAutoTierCoreTests` 38 / `PawnMarkerTests` 69 / `TierTagHelperTests` 35 / `RoleDetectorTests` 16 / `RoleOrderTests` 8）。

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
| `PawnMarker.cs` / `AutoMarkPawn` 模块 | `### 高价值自动标记（AutoMarkPawn）` |
| `ITab_GearManager.cs` 底部勾选框 | `## 自动执行（AutoExecutor）` 入口章节 |
| `HarmonyPatches.cs` Tick 入口 / `AutoEverythingGameComponent.cs` 兼容策略 | `## 自动执行（AutoExecutor）` 入口章节 + `### 评估周期` 表格 + `## 设计原则：逻辑杜绝而非事后清理` |
| `RoleIconDef.cs` 图标缓存 / `PawnMarker.cs` 类别缓存 | `### 高价值自动标记（AutoMarkPawn）` + `### 评估周期` 表格 |
| 设计原则（不适用 Pawn 处理） | `## 设计原则：逻辑杜绝而非事后清理` |
| 新增/删除源文件 | `### 目录结构` 代码块 |
| 新增/修改图片资源 | `## 图片资源` 表格与 `### 资源加载时机` |
| `ITab_GearManager.cs` 静态资源加载 | `### 资源加载时机` |
| `ITab_GearManager.cs` 文字绘制逻辑 | `### ITab 面板文字防换行` |

## 许可证

详见 [LICENSE](./LICENSE)。
