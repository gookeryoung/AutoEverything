# Auto Everything

> 智能自动万物管理 MOD，适用于 RimWorld 1.6+
>
> packageId: `gookeryoung.autoeverything`

殖民者会根据自身**角色**与**情境**，自动挑选最合适的武器、防具与腰带附件，并按需携带药品与 EMP 手雷。

零配置开箱即用，每个殖民者根据技能与特质自动识别角色。

## 设计思路

传统的"自动装备"MOD 常常陷入两个极端：要么把所有殖民者一视同仁（按 DPS 排序随便塞），要么要求玩家手动配置每个殖民者的偏好。本 MOD 的设计原则是：

1. **角色驱动**：殖民者不是无差别的劳动力，而是有专长的个体。射击等级 12 双火的角色应当优先拿狙击枪，医疗等级 10 的角色应当带药品、穿医疗属性服装。
2. **情境感知**：同一把武器在不同情境下价值不同。征召战斗时优先 DPS，狩猎时优先射程，低温环境下优先保温服装。
3. **评分模型**：每个候选装备按多维评分累加，分数最高的胜出。评分维度独立封装，可单独替换或扩展。
4. **逻辑杜绝而非事后清理**：食尸鬼、动物等不适用类别在入口（Pawn 生成、CompTick）就被排除，绝不进入装备管理流程，玩家手动给食尸鬼装备由玩家自行负责。
5. **EMP 手雷全局分配**：每人只选最适合自己的主武器，不再自动分配反向类型副武器；EMP 手雷作为库存携带的副武器特例，按 `CombatTier` 升序（评级低者优先）为 Flexible 后排前 2 人分配。

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

**关键约束**：

1. **Brawler 特质拒绝远程**：仅拥有真正 `Brawler` 特质（`TraitDefOf.Brawler`）的殖民者会被 `Veto(-9000f)` 拒绝远程武器。基于技能判定的 `Brawler` 角色（近战 ≥ 8 且近战 > 射击，但无特质）**不拒绝远程武器**——此类殖民者近战远程双修，应优先拿远程武器作为主武器，贴身时再切换近战副武器。
2. **非格斗者拒绝近战**：`WeaponTraitScorer` 在评分开头检查 Role，仅 `Brawler` 角色（基于特质或技能判定）允许装备近战武器；`Worker`/`Doctor`/`Pacifist`/`Default`（Light）与 `Shooter`/`Hunter`/`Leader`（Flexible）在武器评分时会触发 `Veto(-9000f)` 拒绝近战武器。设计意图：轻甲无防护不宜近战，后排应优先远程输出。

## 主武器选择规则

每人只选最适合自己的主武器，不再自动分配反向类型副武器。EMP 手雷作为库存携带的副武器特例，见下方 [EMP 手雷全局分配](#emp-手雷全局分配库存携带)。

| 殖民者类型 | 主武器 | 说明 |
|-----------|--------|------|
| `Brawler` 特质 | 近战 | Veto 远程武器 |
| 双修（射击+近战均有火） | 远程 | `WeaponSkillScorer` 给远程 +50 偏好分，贴身时 `CheckMeleeSidearm` 切换 |
| 纯近战（射击无火） | 近战 | 按 `WeaponSkillScorer` 评分自然选择 |
| 纯远程（近战无火） | 远程 | 按评分自然选择 |

**贴身切换**：当殖民者持远程武器受近战攻击时，`CheckMeleeSidearm`（30 tick 周期）检测库存近战副武器并自动切换；取消征召时 `OnUndraft` 恢复主武器。该功能仅应对玩家手动给的副武器，自动分配已取消反向类型副武器。

**护盾腰带约束**：护盾腰带会阻挡所有远程武器射击。护盾腰带仅属于重甲前排（Brawler），通过三重保险确保不误配：分配 gate（`BeltAllocator`）+ 评分 Veto（`ApparelShieldBeltScorer`，非 Brawler → `-9999f`）+ 已穿纠错（`RemoveWrongShieldBelt` 自动卸下）。自由后排（Flexible）与轻甲工人（Light）不参与腰带分配（见下方 [腰带附件全局分配](#腰带附件全局分配)）。

**护甲偏好**：`RoleDetector.GetArmorPreference(role)` 根据角色返回护甲偏好，通过**硬否决（Veto）**影响装备拾取，通过**有条件卸下**纠正已穿戴的不匹配护甲：

| 角色 | 护甲偏好 | 说明 |
|------|---------|------|
| `Brawler` | `Heavy`（重甲[前排]）| 强制重甲，承担伤害；穿轻甲时 `RemoveWrongArmorType` 有条件卸下 |
| `Shooter`/`Hunter`/`Leader` | `Flexible`（自由[后排]）| 按评分自由选择，有重甲盈余时考虑 |
| `Worker`/`Doctor`/`Pacifist`/`Default` | `Light`（轻甲[工人]）| 强制轻甲以保持工作效率；穿重甲时 `RemoveWrongArmorType` 有条件卸下 |

**硬否决规则**（`Heavy` 偏好拒绝轻甲，`Light` 偏好拒绝重甲，`Flexible` 不否决）：
- `GlobalAllocator.ReallocateApparel`：候选循环 `continue` 跳过不匹配护甲
- `CompGearManager.EvaluateApparel`：Tick 路径候选循环 `continue` 跳过

**有条件卸下**（避免反复脱穿）：
- `CompGearManager.RemoveWrongArmorType`：仅当地图上存在可拾取的匹配护甲时才卸下不匹配躯干护甲（`HasMatchingArmorOnMap` 检查），避免卸下后赤身

**过渡兜底不检查护甲偏好**：
- `CompGearManager.TryFallbackApparel`：赤身时穿任意护甲过渡（比赤身强），不匹配护甲由 `RemoveWrongArmorType` 在有匹配替换时主动卸下

### 禁止类装备

部分装备因机制或定位不适合自动穿戴，通过评分管线 Veto 拒绝（不主动拾取）：

| 装备类别 | 识别方式 | 处理 | Scorer |
|---------|---------|------|--------|
| 奴隶项圈 | `apparel.slaveApparel == true`（RimWorld 原生标志位）| 非奴隶角色 Veto `-9999f` + 已穿纠错 `RemoveSlaveCollar` 自动卸下 | `ApparelForbiddenScorer` |
| 死气背包 | defName 含 `DEADLIFE`（释放毒云伤友军）| Veto `-9999f`（不主动穿，玩家手动给的保留） | `ApparelForbiddenScorer` |
| 手榴弹 | defName/label 含 `GRENADE`（破片/燃烧瓶/毒气手雷）| Veto `-9000f`（单次消耗品，不适合持续主武器） | `WeaponForbiddenScorer` |
| 火箭发射器 | label 含 `rocket launcher`（末日/三连火箭）| Veto `-9000f`（单次消耗品） | `WeaponForbiddenScorer` |

**例外**：EMP 手雷作为库存携带特例由 `SidearmAllocator` 分配，不经过武器评分管线。`TryFallbackApparel` 兜底排除 Veto 的防具（奴隶项圈/死气背包/护盾腰带）——这些比赤身更糟；但不排除护甲偏好不匹配的防具（赤身时穿任意护甲过渡）。

### 研究型殖民者偏好

非战斗型殖民者（近战远程均无火）若医疗或研究技能高，优先穿实验服（`Apparel_LabCoat`，提供 `ResearchSpeed +0.05`、`EntityStudyRate +0.1`）。

**研究型判定**（`ApparelLabCoatScorer.IsResearchOriented`）：
- 角色 ≠ `Brawler`（轻甲工人/自由后排/医生等）
- 射击与近战 passion 均为 `None`（非战斗型）
- 医疗 ≥ 8 **或** 研究 ≥ 8（有专长可发挥）

**加分**：+50（参考 `WeaponSkillScorer` 双修远程偏好分，让实验服在同类防具评分中显著领先）。

### 过渡装备兜底

当殖民者空手或赤身、且评分管线找不到匹配装备时，`CompGearManager` 会兜底拾取"最不差"的过渡装备，避免空手/赤身状态持续。过渡装备在下次评估周期会被更匹配的装备替换。

| 兜底场景 | 触发条件 | 候选选择规则 | 例外 |
|---------|---------|-------------|------|
| 武器兜底（`TryFallbackWeapon`）| `EvaluateWeapon` 末尾 + `currentWeapon == null` | 扫描地图所有武器，跳过 Forbidden/无法到达/非暴力禁用，用 `ScoreBreakdown.Total`（含 Veto 前的技能分）选技能最契合者 | 格斗者特质（`TraitDefOf.Brawler`）+ 远程武器 → 跳过（拿远程会不开心） |
| 防具兜底（`TryFallbackApparel`）| `EvaluateApparel` 末尾 + `Pawn.apparel.WornApparel.Count == 0` | 扫描地图所有防具，跳过 Forbidden/无法到达/部位不匹配/性别不符/生物编码不匹配/玩家 outfit 策略不允许，用 `ScoreBreakdown.Total` 选评分最高者（沾染/低品质也胜过赤身） | Veto 的防具（如护盾腰带对非 Brawler）仍排除——比赤身更糟，会阻挡远程射击 |

**设计意图**：
- 空手比拿一把不理想的武器更糟；过渡武器在下次评估时会被更好的匹配替换
- 赤身受温度/美观惩罚；过渡防具即使沾染/低品质也胜过赤身
- `Total` 而非最终分比较：含 Veto 前的技能分，让过渡装备选技能最契合的（即使因角色/特质被 Veto，技能分仍可用于排序）

## 腰带附件全局分配

`BeltAllocator.cs` 为重甲前排（Heavy=Brawler）分配腰带附件（护盾腰带 / 消防背包）：

- **周期**：3000 tick（约 50 秒）全局扫描一次
- **候选**：仅重甲前排（Heavy=Brawler）+ belt 层空缺；地图上所有 belt 类附件
- **排序**：按 `CombatTier` 升序（评级低者优先）
- **分配规则**：前 2 人强制分配消防背包（若库存有），其余配护盾腰带
- **评分**：护盾腰带对重甲前排 +100；消防背包对所有候选 +60；品质 ×5 加分

| 腰带类型 | 评分 | 适用对象 |
|---------|------|---------|
| 护盾腰带 | +100 | 重甲前排（Brawler），贴身近战免疫远程射击 |
| 消防背包 | +60 | 重甲前排（Brawler），应对火灾/机械族，优先给评级较低者 |

**消防背包优先级**：重甲前排至少 2 人配备消防背包，优先给评级较低者。评级低的重甲前排承担伤害能力较弱，更需要消防背包增强生存。`CombatTier` 升序排序确保 D/C 档优先于 S/SS/SSS 档获得消防背包。

**护盾腰带分配规则**：护盾腰带会阻挡所有远程射击，自由后排（Shooter/Hunter/Leader）需远程输出，不适用护盾；重甲前排（Brawler）以近战为主，护盾提供远程免疫最为契合。已穿护盾腰带的 Pawn 在武器评分时会触发 Veto（见下方）。

**护盾腰带三重保险**：护盾腰带仅属于重甲前排（Brawler），通过三层约束确保不误配：
1. **分配 gate**：`BeltAllocator` 候选收集时 `GetArmorPreference(role) != Heavy` 直接过滤，远程角色不进入分配池。
2. **评分 Veto**：`ApparelShieldBeltScorer`（防具评分管线首位）检测非 Brawler 角色 + 护盾腰带 → `Veto(-9999f)`，即使因角色瞬变/玩家手动操作进入评分路径也会被拒绝。
3. **已穿纠错**：`CompGearManager.EvaluateApparel` 周期调用 `RemoveWrongShieldBelt(role)`，检测已穿护盾腰带的非 Brawler 角色，卸下并丢到脚下（复用 `GlobalAllocator` 的 `pawn.apparel.Remove` + `GenDrop.TryDropSpawn` 模式）。

**护盾腰带武器 Veto**：`WeaponTraitScorer` 在武器评分开头检查 Pawn 是否穿戴护盾腰带，若候选武器为远程武器则直接 `Veto(-9000f)`，确保持盾者不会被分配远程武器。

## 情境检测规则

`GearContext.cs` 中的 `ContextDetector.GetContext(pawn)` 判定以下情境：

| 情境 | 触发条件 |
|------|----------|
| `Combat` | 已征召 |
| `Hunting` | 当前工作为 `Hunt` 或 `PredatorHunt` |
| `Cold` | 环境温度低于舒适下限 + `tempDangerMargin`，持续 2500 tick（约 42 秒） |
| `Hot` | 环境温度高于舒适上限 + `tempDangerMargin`，持续 2500 tick |
| `Work` | 正在执行非战斗工作 |
| `Normal` | 默认 |

情境变化触发立即装备评估；温度情境需持续暴露，避免频繁切换。

## 评分模型

### 总分公式

```
装备总分 = Σ(各评分维度加分) × 耐久修正
```

若任一硬性约束被触发（如生物编码不匹配、格斗者持远程），直接 `Veto(-9000f)`，管线短路。

**意识形态惩罚**：文化鄙夷（Despised）武器施加 `-w_ideology_despised`（默认 -300）大额负分，但不触发 Veto。与硬约束 Veto（-9000）区分：文化鄙夷是强烈负面偏好，玩家应保留选择权（如战利品中只有该武器时仍可拾取过渡）。文化尊崇（Noble）武器施加 `+w_ideology_noble`（默认 +200）加分。

### 权重预设方案

`GearWeights` 结构包含 15 个权重字段（含意识形态尊崇/鄙夷）。提供 4 个预设方案：

| 方案 | 特点 | 适用场景 |
|------|------|----------|
| `Standard` | 平衡护甲/DPS/工作 | 日常殖民 |
| `Aggressive` | 护甲与 DPS 优先，移速次要 | 高强度战斗 |
| `Economic` | 耐久与品质优先，避免浪费 | 资源紧张 |
| `Hunting` | 远程射程与精准优先 | 狩猎为主 |

玩家可在设置界面循环切换，存档持久化。

**意识形态权重**（需 Ideology DLC）：
- `w_ideology_noble`：文化尊崇武器加分（Standard=200, Aggressive=250, Economic=150, Hunting=200）
- `w_ideology_despised`：文化鄙夷武器惩罚（取负值，Standard=300, Aggressive=400, Economic=200, Hunting=300）

鄙夷武器使用大额负分（-300）而非 Veto，与硬约束（如非 Brawler Veto 近战）语义区分：文化鄙夷是强烈负面偏好，玩家应保留选择权（如战利品中只有该武器时仍可拾取过渡）。

### 武器评分管线

`ScoringPipelineFactory.GetWeaponPipeline()` 按以下顺序执行 10 个 Scorer：

| 顺序 | Scorer | 说明 |
|------|--------|------|
| 1 | `WeaponBiocodedScorer` | 生物编码检查，不匹配直接 Veto |
| 2 | `WeaponForbiddenScorer` | 禁止类武器（手榴弹/火箭发射器）Veto `-9000f`，单次消耗品不适合持续主武器 |
| 3 | `WeaponTraitScorer` | 特质（仅 `Brawler` 特质否决远程武器，技能型 Brawler 不否决） |
| 4 | `WeaponSkillScorer` | 技能等级 × 兴趣乘数（无火 1.0 / 单火 1.5 / 双火 2.0）；双修角色（射击+近战均有火）远程武器额外 +50 |
| 5 | `WeaponContextScorer` | 情境加成（战斗 +DPS / 狩猎 +射程） |
| 6 | `WeaponDpsScorer` | DPS 与伤害倍率 |
| 7 | `WeaponRangeScorer` | 射程 |
| 8 | `WeaponQualityScorer` | 品质 |
| 9 | `WeaponIdeologyScorer` | 意识形态武器偏好（Noble +w_ideology_noble / Despised -w_ideology_despised，需 Ideology DLC） |
| 10 | `WeaponDurabilityScorer` | 耐久修正（乘法） |

### 防具评分管线

`ScoringPipelineFactory.GetApparelPipeline()` 按以下顺序执行 15 个 Scorer：

| 顺序 | Scorer | 说明 |
|------|--------|------|
| 1 | `ApparelShieldBeltScorer` | 护盾腰带硬约束（非 Brawler 角色 + 护盾腰带 → Veto `-9999f`） |
| 2 | `ApparelForbiddenScorer` | 禁止类防具（奴隶项圈非奴隶 / 死气背包）Veto `-9999f` |
| 3 | `ApparelTaintedScorer` | 沾染惩罚 |
| 4 | `ApparelTraitScorer` | 特质偏好 |
| 5 | `ApparelWorkScorer` | 工作属性加成 |
| 6 | `ApparelLabCoatScorer` | 实验服偏好（研究型殖民者 +50，见下方研究型殖民者偏好） |
| 7 | `ApparelContextScorer` | 温度情境 |
| 8 | `ApparelArmorScorer` | 护甲值 |
| 9 | `ApparelInsulationScorer` | 保温 |
| 10 | `ApparelMoveSpeedScorer` | 移速影响 |
| 11 | `ApparelQualityScorer` | 品质 |
| 12 | `ApparelRoyaltyScorer` | 皇家头衔需求 |
| 13 | `ApparelIdeologyScorer` | 意识形态服装 |
| 14 | `ApparelDurabilityScorer` | 耐久修正（按 HP 比例乘法） |
| 15 | `ApparelCurrentWornScorer` | 平局决胜（当前穿戴小幅加分） |

### 副武器评分

`GearScorer.ScoreSidearm()` 独立于管线，逻辑较简单：

- 副武器应为主武器的相反类型（射手配近战副武器，格斗者配远程副武器）
- 近战副武器：偏好高 DPS、轻便（低重量）
- 远程副武器：偏好短冷却、轻便
- 品质加分

> 注：自动分配已取消反向类型副武器，每人只选主武器。`GearScorer.ScoreSidearm()` 仅保留供 `CheckMeleeSidearm` 等历史路径与玩家手动操作场景使用。

### EMP 手雷全局分配（库存携带）

`SidearmAllocator.cs` 实现为 Flexible 后排评级较低者分配 EMP 手雷（库存携带，副武器特例）：

- **周期**：2000 tick（约 33 秒）全局扫描一次
- **候选**：仅 Flexible 后排（`IsBackRow` = Shooter/Hunter/Leader）+ 有主武器 + 库存无 EMP 武器
- **排序**：按 `CombatTier` 升序（评级低者优先）
- **分配规则**：前 2 人分配 EMP 武器（库存携带），其余不分配
- **EMP 识别**：`GearDefClassifier.IsEmpWeapon` 通过 `defName`/`label` 启发式识别（包含 "EMP"）

**设计意图**：

- 取消携带多个装备：每人只选最适合自己的主武器，不再自动分配反向类型副武器
- EMP 手雷特例：评级较低的 Flexible 后排至少 2 人持有 EMP 手雷，应对机械族/护盾等需要 EMP 的战术场景
- 评级低者优先：重甲前排承担近战，评级低的后排承担 EMP 战术支援。`CombatTier` 升序排序确保 D/C 档优先于 S/SS/SSS 档获得 EMP 手雷

### 副武器类型选择规则

| 主武器类型 | 副武器类型 | 特殊规则 |
|-----------|-----------|----------|
| 远程 | 近战 | 仅玩家手动给副武器时生效，`CheckMeleeSidearm` 贴身时自动切换 |
| 近战 | 远程 | 仅玩家手动给副武器时生效 |

**EMP 手雷特例**：Flexible 后排（Shooter/Hunter/Leader）评级较低者前 2 人自动分配 EMP 手雷（库存携带），不依赖主武器类型。EMP 武器通过 `defName`/`label` 启发式识别（包含 "EMP"）。

## 全局重配

`GlobalAllocator.cs` 提供手动触发的全局装备重分配：

- **入口**：殖民者检视面板 `ITab_GearManager` 最下方"全局重配"按钮（占满宽度）
- **规则面板**：直接展开在 `ITab_GearManager` 滚动区内，无需另开窗口，所有规则可见即可调
- **流程**：
  1. 收集所有非征召、非锁定殖民者，按战斗价值降序排序
  2. **第一遍**：所有殖民者放下当前武器到地上（进入地图候选池），释放无火小人手里的好武器
     - 跳过征召中（正在战斗）、生物编码武器（个人绑定不可释放）
  3. 收集地图候选武器池（含刚放下的武器）
  4. **第二遍**：按战斗价值降序，为每个殖民者从候选池评分选最佳武器
     - 已分配武器从候选池移除，避免重复抢占
     - 直接 `MakeJob(Equip)`，不依赖 `EvaluateWeapon` 的 job 流程
  5. 服装/副武器/库存仍用 `ForceEvaluate` 评估
- **效果**：无火小人手里的好武器会释放给双火小人，实现"高价值装备优先分配给高价值殖民者"
- **过滤**：跳过食尸鬼、动物、奴隶、未成年、已倒下、征召中、已锁定的殖民者
- **自动触发**：`AutoExecutor.ExecuteGear` 周期按评级排序调用 `ForceEvaluate`（不脱光），与手动触发语义不同（见 [装备自动重配](#装备自动重配按评级排序升级评估)）

### 全局价值评级档次（CombatTier）

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

**统计范围（9 大可兴趣技能）：** 射击、近战、社交、手工、建造、艺术、烹饪、种植、采矿。

**特殊天赋特质来源：**
- 原生（Core）：`TooSmart`（博闻强识）
- 异象（Anomaly DLC）：`Joyous`（开心果）、`BodyMastery`（极致体能）、`VoidFascination`（痴迷虚空）、`Occultist`（神秘学者）、`Disturbing`（怪诞不经）
- 未加载 Anomaly DLC 时这些特质查询返回 null 自动跳过，不影响判定。

### 武器与护甲分配的评级使用

- **武器分配**：沿用原战斗维度评分 `SidearmAllocator.ComputeCombatValue`（射击/格斗等级 × 兴趣乘数 + 战斗特质加分），武器评分体系完全不变。
- **护甲分配**：在 `GlobalAllocator.ReallocateApparel` 入口处按 `CombatTier` 降序重排 `sortedPawns`，让 S 档殖民者优先获得价值最高的护甲；同档内再用 `ComputePawnValueScore` 精排，让同档中培养更深的殖民者优先。

### 护甲分配算法（重甲单位优先）

护甲分配采用"逐件分配"算法：每件护甲按内在价值降序进入分配流程，分配给"评分最高"的殖民者。

评分公式：
```
护甲评分 = GearScorer.ScoreApparel(基础分) + 角色偏好调整 + 评级权重
```

| 角色偏好 | 护甲类型 | 调整 | 说明 |
|---------|---------|------|------|
| Heavy（重甲[前排]）| 重甲 | `+heavyArmorMatchBonus`（默认 **500**） | 匹配奖励，让 Heavy 显著胜过 Flexible |
| Heavy（重甲[前排]）| 轻甲 | **Veto（`continue` 跳过）** | 硬否决，强制选重甲 |
| Light（轻甲[工人]）| 轻甲 | `+heavyArmorMatchBonus`（默认 **500**） | 匹配奖励，让 Light 显著胜过 Flexible |
| Light（轻甲[工人]）| 重甲 | **Veto（`continue` 跳过）** | 硬否决，强制选轻甲 |
| Flexible（自由[后排]）| 任意 | 0 | 既不奖励也不否决 |

**评级权重**：`+CombatTier × 0.5`（最大 7 档 × 0.5 = 3.5），仅用于打破同分平局，远小于匹配奖励（500）。

**设计意图**：硬否决彻底杜绝"重甲前排穿轻甲"；匹配奖励 +500 让重甲偏好单位（Brawler）显著优先获得重甲，避免高评级 Flexible 殖民者抢占重甲。Flexible 殖民者仍可在无 Heavy 候选时获得重甲（无竞争者时 score 不需要超过任何人）。

> 注：`heavyArmorPenaltyForLight` / `lightArmorPenaltyForHeavy` 设置字段保留以兼容旧存档，但已不再使用（改为 Veto）。

### 同档精排评分（ComputePawnValueScore）

用于同 `CombatTier` 档内的精细排序。评分公式：

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
- 命中自定义评级：采用档位代表分（D=5, C=15, B=25, A=50, S=80, SS=95, SSS=110）+0.5 微量偏向，让同档自定义略优先于同档自动

**使用范围**：仅护甲分配的档内精排；武器分配不使用此分数，仍用 `ComputeCombatValue`（仅战斗维度）。

### 自定义评级识别码（玩家可调）

玩家可在殖民者装备面板（ITab）"全局重配规则"区为指定殖民者手动指定档次，**跳过自动公式计算**，直接按对应等级优先分配高价值装备。

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

**覆盖范围**：殖民者 + 食尸鬼（Anomaly DLC）。食尸鬼也按相同规则评级，但不参与装备分配——玩家可一眼分辨其价值。排序仅作用于 `PawnsFinder.AllMaps_FreeColonists`（不含食尸鬼），通过 `pawn.playerSettings.displayOrder` 写入并 `Find.ColonistBar.MarkColonistsDirty()` 刷新。

### 殖民者栏默认排序（Mod 选项）

在 Mod 选项 → "默认排序" 里配置，`AESettings.defaultSortMode` 字段，存档键 `ae_defaultSortMode`，默认 `ByTierThenValue`。

| 排序模式 | 比较器 | 规则 |
|---------|--------|------|
| 不排序 | — | 仅应用前缀，保留殖民者栏原顺序 |
| 按评级+价值（推荐） | `ComparePawnByTierThenValueDesc` | 先按 `CombatTier` 降序 SSS→SS→S→A→B→C→D→X，同档内按 `ComputeCombatValue` 降序 |
| 按角色+评级 | `ComparePawnByRoleThenValueDesc` | 按角色分组，同角色内按评级降序 |
| 按战斗价值 | `ComparePawnByCombatValueOnlyDesc` | 纯按 `ComputeCombatValue` 降序，不区分评级（高技能和平主义者可能挤占前列） |

**按评级+价值的设计意图**：和平主义者（X 档）即使技能高也排在最右，避免挤占 S/A 档位置。

**角色排序优先级**（`SGSettings.GetRoleOrder`，用于"按角色+评级"模式）：

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

**全局装备重配不改变殖民者栏顺序**：`GlobalAllocator.ReallocateAll` 仅影响装备分配，不修改 `displayOrder`，玩家可放心使用。

**防双重前缀**：
- `SidearmAllocator.GetPawnLookupName` 会自动剥离 Nick 上的评级前缀返回纯净名，确保：
  - 自定义评级查询仍能命中（玩家设置时用的是原名）
  - 面板"当前档次"行拼接出 `S#王五` 而非 `S#S#王五`
- `AEDebug.Label` 在 Nick 已带前缀时直接返回 LabelShort，不再拼接
- 面板"当前角色"行在 Nick 已带前缀时不显示额外 `[S#王五]` 后缀

**持久化**：原名字典 `tierTagOriginals` 通过 `ae_tierTagOriginals` 存档（`List<string>` 格式 `thingIDNumber|原Nick`），重启后仍能恢复原名，避免误剥离玩家手动改的 Nick。

### 战斗价值公式（玩家可调）

```
战斗价值 = (射击等级 × 射击兴趣乘数 + 近战等级 × 近战兴趣乘数) × 技能权重 + Σ特质加分
```

所有参数均可在面板内通过滑块调整，存档保存：

| 参数 | 默认 | 范围 | 含义 |
|------|------|------|------|
| 无火兴趣乘数 | 1.0 | 0.5 ~ 3.0 | 无火焰时技能等级权重 |
| 单火兴趣乘数 | 1.5 | 0.5 ~ 3.0 | Minor 兴趣时技能等级权重 |
| 双火兴趣乘数 | 2.0 | 0.5 ~ 3.0 | Major 兴趣时技能等级权重 |
| 技能整体权重 | 1.00 | 0.50 ~ 2.00 | 技能分整体缩放 |
| 坚韧（Tough）加分 | +30 | -50 ~ +100 | Tough 特质加分 |
| 乱开枪加分 | -15 | -50 ~ +50 | ShootingAccuracy degree=-1 |
| 冷枪手加分 | +15 | -50 ~ +100 | ShootingAccuracy degree=+1 |

### 保护规则（玩家可调）

| 规则 | 默认 | 关闭后果 |
|------|------|---------|
| 重配前先放下当前武器 | 开 | 仅评估地图上已有的武器，无火小人手里的好武器不会被释放 |
| 跳过征召中的殖民者 | 开 | 强制重配征召中的殖民者（打断战斗） |
| 跳过已锁定的殖民者 | 开 | 无视玩家在面板上勾选的锁定开关，强制重配 |
| 跳过生物编码武器 | 开 | 仍尝试放下生物编码武器（但游戏原生会拒绝他人拾取） |

## 自动工作分配（AutoWork）

`AutoWork/WorkAllocator.cs` 提供多遍协调分配 + 工作计数跟踪的工作优先级自动分配。
所有技能类工作复用统一 `AssignWorkType` + `WorkAllocationConfig` 四大原则分配，将工作分为 6 类按固定顺序分配，前排分配结果影响后排候选排序（通过工作计数实现均衡负载）。

### 统一四大原则

所有技能类工作（关键/狩猎类/研究/普通技能）共用统一分配 API，配置由 `WorkAllocationConfig` 结构编码：

1. **保证数量**：`GuaranteeCount` 确保至少 N 人承担（无论有无火），top N 内有火给 `GuaranteePassionatePriority`、无火给 `GuaranteeNonPassionatePriority`
2. **三因子排序**：top N 人选按 Passion 降序 → SkillLevel 降序 → WorkCount 升序选择，保证数量内选兴趣最高、技能最强的
3. **有火保底**：超出 guarantee 的有火者至少给 `FloorPassionatePriority`（如 3），保留生产能力
4. **无火技能兜底**：超出 guarantee 的无火者，`UseSkillFloorForNonPassionate=true` 时按技能等级兜底（≥12→2, ≥8→3, 否则 0）；`=false` 时直接给 `FloorNonPassionatePriority`

**workCount 硬上限**：每人最多承担 `MaxCoreWorkCount=2` 项 priority≤2 的专业工作。候选收集阶段跳过已满载者，强制均衡负载。若严格收集后候选不足保证人数，回退放宽（含满载者），保证小殖民地工作有人做。

**奴隶处理**：奴隶在专业工作中与殖民者同流程，按兴趣/技能参与分配，无特殊优先级。奴隶的特殊处理仅在服务类工作（搬运/清洁/非技能）中生效，见下方[服务类工作规则](#服务类工作规则搬运清洁非技能)。

### 分配规则

工作类型按以下分类与顺序分配（顺序影响工作计数，前排分配结果影响后排候选）：

| 顺序 | 工作分类 | 包含类型 | 保证人数 | top N 有火 | top N 无火 | 有火保底 | 无火兜底 | 特殊约束 |
|------|---------|---------|---------|-----------|-----------|---------|---------|---------|
| 1 | 紧急 | Firefighter / Patient / PatientBedRest | — | 全部 → 1 | 全部 → 1 | — | — | 不计入 workCount |
| 2 | 关键 | Doctor / Warden / Childcare | 2 | 1 | 3 | 3 | 技能兜底 | — |
| 2 | 烹饪 | Cooking | 1 | 1 | 2 | 3 | 0 | 生存关键，保证 1 人 priority≤2 |
| 3 | 狩猎 | Hunting | 2 | 2 | 2 | 4 | 技能兜底 | 需远程武器 + 后排排序 |
| 3 | 钓鱼 | Fishing | 2 | 3 | 3 | 3 | 技能兜底 | 需远程武器 + 后排排序 |
| 3 | 割除 | PlantCutting | 2 | 1 | 0 | 3 | 0 | — |
| 3 | 种植 | Growing | 2 | 2 | 0 | 3 | 0 | — |
| 4 | 研究 | Research | 1 | 2 | 2 | 4 | 技能兜底 | — |
| 5 | 普通技能 | Mining / Crafting / Smithing / Tailoring / Art / Construction / Handling 等 | 2 | 2 | 3 | 3 | 技能兜底 | — |
| 6 | 服务类 | Hauling / Cleaning / BasicWorker 等 | — | 见下方服务类规则 | — | — | — | 不计入 workCount，奴隶优先 |

**top N 有火/无火**：保证人数内按三因子排序选取，有火者给"top N 有火"优先级，无火者给"top N 无火"优先级。

**有火保底**：超出保证人数的有火者至少给此优先级（原则 3），确保有兴趣者保留生产能力。

**无火兜底**：超出保证人数的无火者，"技能兜底"表示按相关技能最高等级判定（≥12→2, ≥8→3, 否则 0，原则 4）；"0"表示直接禁用。

**割除/种植特殊处理**：无火者（含 top N 内）一律 priority=0，仅在有足够有火者时才分配。设计意图：割除/种植无兴趣者效率极低且影响心情，不强制承担。

**工作计数**：跟踪每 Pawn 的 priority ≤ 2 的专业工作数量（紧急/服务类不计入）。
用于「同等兴趣下优先安排其他工作少的」实现均衡负载。
**硬上限**：每人最多 2 项 priority≤2 的专业工作，候选收集阶段跳过已满载者，候选不足时回退放宽。

**三因子排序**：Passion 降序 → SkillLevel 降序 → WorkCount 升序。
Passion 量化：None=0, Minor=1, Major=2。

**后排角色优先**（仅狩猎）：通过 `RoleDetector.IsBackRow(role)` 判定，仅 `ArmorPreference.Flexible`（Shooter/Hunter/Leader）视为后排。
设计意图：后排角色应优先承担狩猎以练习射击能力。

**狩猎需远程武器**：候选收集阶段过滤 `pawn.equipment?.Primary?.def.IsRangedWeapon != true` 的殖民者（未装备武器 / 装备近战武器 / 装备非武器均排除）。
设计意图：避免无兴趣低技能者被分配 priority=2 的狩猎工作（狩猎需远程武器才能进行）。优先级顺序不变（兴趣>等级仍由 `ComparePawnsForHunting` 保证）。

**循环依赖规避**：Hunting 始终设为 2 或 0，绝不设为 1，因此不会污染 `RoleDetector.DetectRole` 的 Hunter 判定（其依赖 Hunting priority == 1）。

### 服务类工作规则（搬运/清洁/非技能）

服务类工作（Hauling / Cleaning / BasicWorker 等无 relevantSkills 的工作）不使用 `WorkAllocationConfig`，而是采用独立的"保证基本数量"原则，按 **奴隶优先 → 评级升序（最低档在前）→ 工作计数升序** 排序后依次判定：

1. **奴隶均 priority=1**：所有奴隶殖民者强制 priority=1（奴隶承担服务类工作）
2. **保底 1 人 priority=1**：若尚无 priority=1 者，排序首位非奴隶（即评级最低者）priority=1
3. **工作计数 < 3 → priority=1**：其他优先 1/2 工作数量少于 3 的殖民者 priority=1（均衡负载，让工作少的承担服务类）
4. **按评级分档**：以上均不满足者按 `CombatTier` 分档：SSS/SS/S=4, A/B/C=3, D/X=1（高价值殖民者少做服务类工作）

**设计意图**：
- 奴隶优先承担服务类工作（受限工作类型多，适合体力劳动）
- 评级低者优先（高价值殖民者应专注技能工作）
- 工作计数少者优先（均衡负载，避免少数人承担过多服务类工作）
- 服务类工作不计入 workCount（避免污染后续技能工作的均衡负载计算）

### 自定义优先级自动启用

执行全局工作重配时，若 `Find.PlaySettings.useWorkPriorities` 未启用，自动启用为 true，否则 1-4 优先级系统不生效。

### 入口

- **MOD 选项** → 启用/禁用"工作自动配置"（`AESettings.autoWorkEnabled`，默认勾选）
- **殖民者装备面板（ITab）底部** → "工作自动配置"勾选框
  - **勾选时**：立即执行一次工作重配，并启用自动执行（每 3000 tick + 新增殖民者时立即触发）
  - **取消勾选时**：仅停止自动执行，保留当前工作分配（工作优先级无法撤销）
  - **默认勾选**

## 自动执行（AutoExecutor）

`Core/AutoExecutor.cs` 静态类负责工作重配、人员评级、装备重配的周期/新增殖民者自动触发，以及高价值非殖民者标记的 ITab 勾选消息提示。

- **入口**：由 `CompGearManager.CompTick` 每 tick 调用 `AutoExecutor.TryTick()`
- **静态门控**：每 60 tick 检查一次殖民者数量变化与周期触发
- **周期触发**：每 3000 tick（约 50 秒）执行一次工作重配、人员评级、装备重配（高价值标记为实时绘制，无周期执行）
- **新增殖民者检测**：`PawnsFinder.AllMaps_FreeColonists.Count` 增加 → 立即触发（不弹消息框）
- **首次初始化守卫**：`lastWorkTick`/`lastTierTick`/`lastGearTick`/`lastMarkTick` < 0 时设为当前 tick 不触发，避免存档加载误触发
- **错误隔离**：工作、评级、装备重配、星标各自独立 try-catch + `Log.ErrorOnce`，salt 独立（Work=0xA200 / Tier=0xA300 / Gear=0xA400 / Mark=0xA500）
- **自动周期路径不弹消息框**（避免刷屏），仅走 `AEDebug.Log`；手动触发路径弹 `Messages.Message` 给玩家反馈

### 装备自动重配（按评级排序升级评估）

- **触发**：周期 3000 tick + 新增殖民者立即触发 + ITab 勾选时立即触发
- **机制**：按战斗价值降序逐个调用 `CompGearManager.ForceEvaluate(ReloadTarget.All)`，通过升级阈值检查是否有更优装备可换（不主动脱光当前装备）
- **高评级优先**：高战斗价值殖民者优先评估，通过升级阈值拾取地图上的更好装备
- **护甲偏好硬否决**：重甲前排（Heavy）拒绝拾取轻甲，轻甲工人（Light）拒绝拾取重甲，自由后排（Flexible）自由选择
- **有条件卸下**：`RemoveWrongArmorType` 仅当地图上有匹配护甲可换时才卸下不匹配护甲，避免赤身反复脱穿
- **不打断战斗**：征召中（`Drafted`）的殖民者跳过
- **奴隶排除**：未征召奴隶不参与自动装备重配（与 `CompTick` 一致）
- **未成年**：仅评估防具（`ForceEvaluate(Apparel)`），跳过武器/副武器/库存
- **尊重锁定**：`comp.locked` 为 true 的殖民者跳过
- **与手动"全局重配"区别**：自动重配不放下当前装备（避免频繁脱穿导致心情差），低评分殖民者手里的好装备不会主动让出；手动"全局重配"按钮触发 `ReallocateAll` 放下所有装备重新分配
- **入口**：殖民者装备面板（ITab）底部 → "装备自动重配"勾选框（`AESettings.autoGearReallocate`，默认勾选）

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

### 设计模式

| 模式 | 实现 | 职责 |
|------|------|------|
| 策略模式 | `IScorer<TThing>` | 每个评分维度独立封装，可替换 |
| 责任链/管线 | `ScoringPipeline<T>` | 按顺序执行 Scorer，Veto 短路 |
| 工厂模式 | `ScoringPipelineFactory` | 静态缓存管线实例 |
| 建造者模式 | `ScoreBreakdown` | 累积评分项，生成调试明细 |
| 策略调度 | `GearPolicyEngine` | 全局预设方案切换 |
| 门面模式 | `GearScorer` | 简化外部调用 |
| 观察者模式 | `DebugMonitor` | 调试事件监测 |

### 目录结构

```
Source/AutoEverything/
├── AutoEverything.csproj                  # C# 7.3 项目文件
├── Core/                                  # → namespace AutoEverything.Core
│   ├── ModController.cs                   # MOD 入口，StaticConstructorOnStartup
│   ├── HarmonyPatches.cs                  # Harmony 补丁：Comp 注入 + 取消征召恢复
│   ├── AutoEverythingMod.cs               # Mod 设置入口（从 SGSettings 拆分）
│   ├── AESettings.cs                      # ModSettings 持久化 + 设置窗口（从 SGSettings 拆分）
│   ├── ColonistBarSortMode.cs             # 殖民者栏排序枚举（从 SGSettings 拆分）
│   ├── DLCCompat.cs                       # DLC API 安全包装
│   ├── AEDebug.cs                         # AEDebug 日志工具（原 DebugHelper.cs 重命名）
│   ├── DebugMonitor.cs                    # 调试监测
│   ├── PawnSuitabilityChecker.cs          # Pawn 适配性过滤
│   └── CombatTier.cs                      # 战斗价值档次枚举
├── RoleEvaluation/                        # → namespace AutoEverything.RoleEvaluation
│   ├── PawnRole.cs                        # 角色检测 + 护甲偏好
│   ├── GearContext.cs                    # 情境检测
│   ├── PawnStateCleaner.cs                # Pawn 状态清理工具
│   └── CombatEvaluator.cs                 # 战斗价值/评级计算（从 SidearmAllocator 拆分）
├── AutoEquipment/                         # → namespace AutoEverything.AutoEquipment
│   ├── CompGearManager.cs                # ThingComp，Tick 入口与评估协调
│   ├── GearScorer.cs                      # 评分门面
│   ├── GearDefClassifier.cs               # 装备 Def 分类工具（武器/防具/腰带识别）
│   └── Scoring/                           # → namespace AutoEverything.AutoEquipment.Scoring
│       ├── IScorer.cs                     # 评分策略接口
│       ├── ScoringPipeline.cs             # 评分管线
│       ├── ScoringPipelineFactory.cs      # 管线工厂
│       ├── ScoreBreakdown.cs              # 评分明细
│       ├── GearWeights.cs                 # 权重结构 + 4 预设
│       ├── GearPreset.cs                  # 预设枚举
│       ├── GearPolicyEngine.cs            # 策略调度
│       ├── Weapon/                        # → ...AutoEquipment.Scoring.Weapon（10 个文件）
│       └── Apparels/                      # → ...AutoEquipment.Scoring.Apparels（12 个文件）
├── Allocation/                            # → namespace AutoEverything.Allocation
│   ├── GlobalAllocator.cs                 # 全局装备重配（手动触发）
│   ├── SidearmAllocator.cs                # 副武器全局分配
│   ├── BeltAllocator.cs                   # 腰带附件全局分配（护盾腰带/消防背包）
│   └── PawnCombatProfile.cs               # Pawn 战斗画像（技能/特质/兴趣聚合）
├── AutoMarkPawn/                          # → namespace AutoEverything.AutoMarkPawn
│   └── PawnMarker.cs                      # 高价值非殖民者标记（S+ 头顶红色星标实时绘制）
└── UI/                                    # → namespace AutoEverything.UI
    ├── ITab_GearManager.cs                # 装备管理面板
    ├── Dialog_GlobalReallocate.cs         # 全局重配规则对话框
    └── PresetDetailsWindow.cs             # 预设方案详情窗口（从 SGSettings 拆分）
```

**模块职责说明：**
- **Core**：基础工具与全局状态（MOD 入口、设置、调试、DLC 兼容、Pawn 适配性、战斗价值档次）
- **RoleEvaluation**：角色与情境评价（角色检测、情境检测、战斗价值评估、状态清理）
- **AutoEquipment**：装备评分系统（CompTick 协调、评分门面、装备分类、评分管线与各 Scorer）
- **Allocation**：全局分配策略（全局重配、副武器、腰带附件、Pawn 战斗画像）
- **AutoMarkPawn**：高价值非殖民者标记（S+ 档次头顶红色星标实时绘制）
- **UI**：玩家界面（ITab 面板、对话框、预设详情窗口）

未来扩展（自动药物/自动食物等）可在 `Source/AutoEverything/` 下新增独立模块文件夹，按上述命名空间约定扩展。

### 评估周期

| 路径 | 周期 | 说明 |
|------|------|------|
| `CompTick` 主评估 | `evaluateInterval`（默认 500 tick ≈ 8 秒） | 武器/防具/药品/副武器 |
| 征召副武器检查 | 30 tick | 战斗紧迫，需快速切近战 |
| `SidearmAllocator` | 2000 tick | 全局副武器分配 |
| `BeltAllocator` | 3000 tick | 全局腰带附件分配（护盾腰带/消防背包） |
| `AutoExecutor` 殖民者检查 | 60 tick | 殖民者数量增加时立即触发工作+评级+装备重配 |
| `AutoExecutor` 工作重配 | 3000 tick | 周期 + 新增殖民者 + ITab 勾选时触发 |
| `AutoExecutor` 人员评级 | 3000 tick | 周期 + 新增殖民者 + ITab 勾选时触发 |
| `AutoExecutor` 装备重配 | 3000 tick | 按评级排序 `ForceEvaluate`（不脱光）；周期 + 新增殖民者 + ITab 勾选时触发 |
| `AutoExecutor` 高价值标记 | 实时（Harmony 补丁） | S+ 档次非殖民者人类头顶绘制红色星标；ITab 勾选时统计数量弹消息，取消勾选自动停止绘制 |
| 角色缓存 | `RoleCacheInterval`（2500 tick） | 避免每 tick 重复检测 |
| 检视面板缓存 | 60 tick | ITab 角色徽章/数值摘要刷新 |
| 死亡 Pawn 字典清理 | 60000 tick | `RoleDetector`/`ContextDetector` 残留条目清理 |

所有周期都通过 `(TicksGame + thingIDNumber) % interval` 分散，避免所有 Pawn 同 tick 触发卡顿。

## 设计原则：逻辑杜绝而非事后清理

食尸鬼（Anomaly DLC 变异体）、动物、机械族等不适用类别**绝不进入**装备管理流程：

| 入口 | 防御 |
|------|------|
| `PawnSuitabilityChecker.CanManageGearDef` | 注入 ThingDef 前过滤，仅 `race.Humanlike` 通过 |
| `Pawn_SpawnSetup_Patch` | 运行时注入前检查 `IsGhoul` + `CanManageGear` |
| `CompTick` 入口 | 兜底：旧存档已注入的不适用 Pawn 静默 `AllComps.Remove(this)` |
| `ForceEvaluate` / `ReloadAllColonists` | 入口防御：食尸鬼/不适用类别跳过 |

**玩家手动给食尸鬼装备的物品由玩家自行负责，MOD 不干预、不清理。**

## 奴隶处理

奴隶（Ideology DLC）不参与自动装备管理，但**参与自动工作分配**（作为殖民地劳动力）：

| 流程 | 奴隶处理 |
|------|---------|
| `CompGearManager.CompTick` 日常评估 | 未征召时直接 return，不主动找武器/防具/药品 |
| `GlobalAllocator.ReallocateAll` 全局重配 | 候选收集时跳过奴隶 |
| `AutoExecutor.ExecuteGear` 自动装备重配 | 按评级排序调用 `ForceEvaluate`，候选收集时跳过奴隶（与 `CompTick` 一致） |
| `SidearmAllocator` / `BeltAllocator` 全局分配 | 候选收集时跳过奴隶 |
| `WorkAllocator.ReallocateAll` 工作重配 | **奴隶参与分配**（通过 `map.mapPawns.SlavesOfColonySpawned` 收集） |

**设计意图**：奴隶的装备由玩家完全控制，MOD 不干预；但奴隶作为殖民地劳动力，应参与工作优先级自动分配（紧急工作、搬运、清洁等）。征召时的副武器切换逻辑保留（奴隶无副武器本就跳过）。

**奴隶收集**：`mapPawns.FreeColonistsSpawned` 不含奴隶，需单独遍历 `mapPawns.SlavesOfColonySpawned`。无 Biotech DLC 时该方法返回空列表，不影响无 DLC 环境。

## 性能约束

遵循 RimWorld MOD 开发的高性能约定：

- Tick 路径禁止 LINQ、禁止 `new List<>()`、禁止 `OrderBy`
- 集合用静态缓存或实例字段复用（如 `SidearmAllocator` 的 `candidatePawns`/`candidateWeapons`）
- 评估日志走 `AEDebug.Log`，受 `debugLogging` 开关短路
- 决策日志（换装结果）保留 `Log.Message`，玩家可见
- 可疑评分日志用 `Log.WarningOnce` 防刷屏

## 兼容性

- DLC API 调用前必须 `ModsConfig.XActive` 检查
- `DefDatabase.GetNamed` 后必须 null 检查
- 仅依赖 Harmony，无其他 MOD 依赖
- 兼容外星人 MOD（任何 `race.Humanlike` 种族）
- 支持存档中途添加

## 调试

设置界面开启 `debugLogging` 后：

- 评估过程日志（每个 Pawn 每 500 tick 一次）
- 评分明细（可疑评分时输出完整 breakdown）
- 装备管理面板显示当前评分

监测开关可单独控制：

- 换装事件
- 武器评分
- 防具评分
- 评分明细
- 候选对比

## 本地化

中英文双语，面向玩家的字符串均通过 `"Key".Translate()` 获取，禁止硬编码。

## 图片资源

| 资源 | 路径 | 用途 |
|------|------|------|
| Preview | `About/Preview.png` | Steam Workshop 预览图 |
| ModIcon | `Textures/UI/Icons/ModIcon.png` | Mod 列表图标（`About.xml` 的 `modIconPath`） |
| 标题图标 | `Textures/UI/Icons/AutoEverythingTitle.png` | 面板/文档标题装饰 |
| 评级徽章 | `Textures/UI/Icons/Tier/Tier_{SSS,SS,S,A,B,C,D,X}.png` | ITab 评级徽章，替代纯色块（SS/SSS 暂无图，回退纯色块） |
| 角色徽章 | `Textures/UI/Icons/Role/Role_{Brawler,Shooter,Doctor,Hunter,Worker,Pacifist,Leader,Default}.png` | ITab 角色徽章，左侧图标 + 右侧角色名 |

### 资源加载时机

`ITab_GearManager` 标记 `[StaticConstructorOnStartup]`，纹理在主线程启动阶段加载：

```
ContentFinder<Texture2D>.Get("UI/Icons/Tier/Tier_S", false)  // reportFailure=false
```

- **禁止**在普通类的静态字段初始化器中调用 `ContentFinder`——类型首次访问可能在非主线程（DefDatabase 扫描、Harmony 反射），触发 `Tried to get a resource from a different thread` 异常
- `[StaticConstructorOnStartup]` 让 RimWorld 在主线程启动时主动触发静态构造，抢占其他线程
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
| `GearWeights.cs` | `## 评分模型 → 权重预设方案` |
| `ScoringPipelineFactory.cs` | `## 武器评分管线` / `## 防具评分管线` 表格 |
| 任一 `IScorer` 实现的加分公式 | 对应 Scorer 的"说明"列与 `## 总分公式` |
| `SidearmAllocator.cs` | `## 副武器全局分配` 与公式块 |
| `BeltAllocator.cs` | `## 腰带附件全局分配` |
| `GearPreset.cs` / `GearPolicyEngine.cs` | `## 权重预设方案` 表格 |
| `CompGearManager.cs` Tick 路径 | `## 评估周期` 表格 |
| `WeaponSkillScorer.cs` / `WeaponTraitScorer.cs` | `## 主武器选择规则` 表格 |
| `PawnRole.cs` / `GetArmorPreference` | `## 护甲偏好` 表格 |
| 设计原则（不适用 Pawn 处理） | `## 设计原则：逻辑杜绝而非事后清理` |
| `GlobalAllocator.cs` / `Dialog_GlobalReallocate.cs` | `## 全局重配` 与 `### 保护规则` |
| `GlobalAllocator.cs` 护甲匹配奖励 | `### 护甲分配算法（重甲单位优先）` 表格 |
| `AutoExecutor.cs` | `## 自动执行（AutoExecutor）` + `### 评估周期` 表格 |
| `WorkAllocator.cs` 奴隶收集/狩猎限制/工作分配规则 | `## 奴隶处理` + `## 自动工作分配（AutoWork）` 分配规则表格与统一四大原则 |
| `PawnMarker.cs` / `AutoMarkPawn` 模块 | `### 高价值非殖民者标记（AutoMarkPawn）` |
| `ITab_GearManager.cs` 底部勾选框 | `## 自动执行（AutoExecutor）` 入口章节 |
| `SGSettings.cs` 排序相关 | `### 殖民者栏默认排序` 表格 |
| 新增/删除源文件 | `### 目录结构` 代码块 |
| 新增/修改图片资源 | `## 图片资源` 表格与 `### 资源加载时机` |
| `ITab_GearManager.cs` 静态资源加载 | `### 资源加载时机` |
| `ITab_GearManager.cs` 文字绘制逻辑 | `### ITab 面板文字防换行` |

## 许可证

详见 [LICENSE](./LICENSE)。
