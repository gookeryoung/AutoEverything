# Auto Equipment

> 智能自动装备管理 MOD，适用于 RimWorld 1.6+
>
> packageId: `gookeryoung.autoequipment`

殖民者会根据自身**角色**与**情境**，自动挑选最合适的武器、防具与副武器，并按需携带药品。

零配置开箱即用，每个殖民者根据技能与特质自动识别角色。

## 设计思路

传统的"自动装备"MOD 常常陷入两个极端：要么把所有殖民者一视同仁（按 DPS 排序随便塞），要么要求玩家手动配置每个殖民者的偏好。本 MOD 的设计原则是：

1. **角色驱动**：殖民者不是无差别的劳动力，而是有专长的个体。射击等级 12 双火的角色应当优先拿狙击枪，医疗等级 10 的角色应当带药品、穿医疗属性服装。
2. **情境感知**：同一把武器在不同情境下价值不同。征召战斗时优先 DPS，狩猎时优先射程，低温环境下优先保温服装。
3. **评分模型**：每个候选装备按多维评分累加，分数最高的胜出。评分维度独立封装，可单独替换或扩展。
4. **逻辑杜绝而非事后清理**：食尸鬼、动物等不适用类别在入口（Pawn 生成、CompTick）就被排除，绝不进入装备管理流程，玩家手动给食尸鬼装备由玩家自行负责。
5. **副武器全局分配**：地图副武器数量有限，按战斗价值（双火高技能优先）排序分配，避免低价值角色先抢占。

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

**关键约束**：仅拥有真正 `Brawler` 特质（`TraitDefOf.Brawler`）的殖民者会被 `Veto(-9000f)` 拒绝远程武器。基于技能判定的 `Brawler` 角色（近战 ≥ 8 且近战 > 射击，但无特质）**不拒绝远程武器**——此类殖民者近战远程双修，应优先拿远程武器作为主武器，贴身时再切换近战副武器。

## 主武器选择规则

| 殖民者类型 | 主武器 | 副武器 | 说明 |
|-----------|--------|--------|------|
| `Brawler` 特质 | 近战 | 远程（纯近战角色优先 EMP） | Veto 远程武器 |
| 双修（射击+近战均有火） | 远程 | 近战 | `WeaponSkillScorer` 给远程 +50 偏好分，贴身时 `CheckMeleeSidearm` 切换 |
| 纯近战（射击无火） | 近战 | 远程（优先 EMP） | `SidearmAllocator` 对 EMP 武器 +1000 分 |
| 纯远程（近战无火） | 远程 | 近战 | 按评分自然选择 |

**贴身切换**：当殖民者持远程武器受近战攻击时，`CheckMeleeSidearm`（30 tick 周期）检测库存近战副武器并自动切换；取消征召时 `OnUndraft` 恢复主武器。

**护盾腰带约束**：护盾腰带会阻挡所有远程武器射击。带护盾腰带的纯近战角色**不分配远程/EMP 副武器**；带消防背包的纯近战角色**可正常分配远程/EMP 副武器**。

## 腰带附件全局分配

`BeltAllocator.cs` 为纯近战角色（射击无火）分配腰带附件（护盾腰带 / 消防背包）：

- **周期**：3000 tick（约 50 秒）全局扫描一次
- **候选**：纯近战角色 + belt 层空缺；地图上所有 belt 类附件
- **优先级**：按战斗价值降序分配（复用 `SidearmAllocator.ComputeCombatValue`）
- **全局保底**：若全局无任何殖民者穿戴消防背包，强制最高战斗价值纯近战角色拿消防背包
- **评分**：护盾腰带 +100（近战防护优先），消防背包 +60，品质 ×5 加分

| 腰带类型 | 评分 | 适用场景 |
|---------|------|---------|
| 护盾腰带 | +100 | 贴身近战，免疫远程射击 |
| 消防背包 | +60 | 应对火灾/机械族，且允许配远程/EMP 副武器 |

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

### 权重预设方案

`GearWeights` 结构包含 13 个权重字段。提供 4 个预设方案：

| 方案 | 特点 | 适用场景 |
|------|------|----------|
| `Standard` | 平衡护甲/DPS/工作 | 日常殖民 |
| `Aggressive` | 护甲与 DPS 优先，移速次要 | 高强度战斗 |
| `Economic` | 耐久与品质优先，避免浪费 | 资源紧张 |
| `Hunting` | 远程射程与精准优先 | 狩猎为主 |

玩家可在设置界面循环切换，存档持久化。

### 武器评分管线

`ScoringPipelineFactory.GetWeaponPipeline()` 按以下顺序执行 9 个 Scorer：

| 顺序 | Scorer | 说明 |
|------|--------|------|
| 1 | `WeaponBiocodedScorer` | 生物编码检查，不匹配直接 Veto |
| 2 | `WeaponTraitScorer` | 特质（仅 `Brawler` 特质否决远程武器，技能型 Brawler 不否决） |
| 3 | `WeaponSkillScorer` | 技能等级 × 兴趣乘数（无火 1.0 / 单火 1.5 / 双火 2.0）；双修角色（射击+近战均有火）远程武器额外 +50 |
| 4 | `WeaponContextScorer` | 情境加成（战斗 +DPS / 狩猎 +射程） |
| 5 | `WeaponDpsScorer` | DPS 与伤害倍率 |
| 6 | `WeaponRangeScorer` | 射程 |
| 7 | `WeaponQualityScorer` | 品质 |
| 8 | `WeaponIdeologyScorer` | 意识形态偏好 |
| 9 | `WeaponDurabilityScorer` | 耐久修正（乘法） |

### 防具评分管线

`ScoringPipelineFactory.GetApparelPipeline()` 按以下顺序执行 11 个 Scorer：

| 顺序 | Scorer | 说明 |
|------|--------|------|
| 1 | `ApparelTaintedScorer` | 沾染惩罚 |
| 2 | `ApparelTraitScorer` | 特质偏好 |
| 3 | `ApparelWorkScorer` | 工作属性加成 |
| 4 | `ApparelContextScorer` | 温度情境 |
| 5 | `ApparelArmorScorer` | 护甲值 |
| 6 | `ApparelInsulationScorer` | 保温 |
| 7 | `ApparelMoveSpeedScorer` | 移速影响 |
| 8 | `ApparelQualityScorer` | 品质 |
| 9 | `ApparelRoyaltyScorer` | 皇家头衔需求 |
| 10 | `ApparelIdeologyScorer` | 意识形态服装 |
| 11 | `ApparelCurrentWornScorer` | 平局决胜（当前穿戴小幅加分） |

### 副武器评分

`GearScorer.ScoreSidearm()` 独立于管线，逻辑较简单：

- 副武器应为主武器的相反类型（射手配近战副武器，格斗者配远程副武器）
- 近战副武器：偏好高 DPS、轻便（低重量）
- 远程副武器：偏好短冷却、轻便
- 品质加分

### 副武器全局分配

`SidearmAllocator.cs` 实现按战斗价值优先级的全局分配：

```
战斗价值 = 射击等级 × 兴趣乘数 + 近战等级 × 兴趣乘数
兴趣乘数：无火 1.0，单火 1.5，双火 2.0
```

每 2000 tick（约 33 秒）全局扫描一次：

1. 收集所有需要副武器的殖民者（非奴隶、非未成年、非食尸鬼、有主武器、库存无副武器）
2. 按战斗价值**降序排序**——双火高技能角色最优先
3. 收集地图上所有武器候选
4. 依次为高价值 Pawn 匹配最佳副武器，分配后从候选池移除
5. 候选不足时低价值角色自然拿不到——"有盈余才考虑"

### 副武器类型选择规则

| 主武器类型 | 副武器类型 | 特殊规则 |
|-----------|-----------|----------|
| 远程 | 近战 | 贴身时自动切换（`CheckMeleeSidearm`） |
| 近战 | 远程 | 纯近战角色（射击无火）优先 EMP，应对机械族/护盾；其余优先普通远程 |

**EMP 副武器偏好**：当殖民者射击技能无火（`passion=None`）且主武器为近战时，副武器评分对 EMP 武器加 1000 分，确保优先选取 EMP 手榴弹/EMP 炮等。EMP 武器通过 `defName`/`label` 启发式识别（包含 "EMP"）。

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
- **过滤**：跳过食尸鬼、动物、奴隶、未成年、已倒下、征召中的殖民者

### 战斗价值档次（DEBUG 显示）

殖民者战斗价值按 `CombatTier` 枚举离散化为 6 档，DEBUG 模式下在面板角色行与日志中以 `名字[档次]` 显示（如 `王五[S]`）：

| 档次 | 判定 | 说明 |
|------|------|------|
| **S** | 双 Major 兴趣 + 战斗特质 | 最高优先级（双火带 Tough/Brawler 等） |
| **A** | 双 Major 兴趣 | 双火但无战斗特质 |
| **B** | 任一兴趣（Minor 或单 Major） | 单火/单 Major |
| **C** | 无兴趣但有战斗特质 | Tough 农民等 |
| **D** | 无兴趣无战斗特质 | 基础农民 |
| **X** | `WorkTagIsDisabled(Violent)` | 无法从事暴力活动（医疗/未成年等） |

战斗特质：`Brawler`、`Tough`、`Nimble`、`Bloodlust`、`ShootingAccuracy`（乱开枪/冷枪手任意 degree）。

档次仅用于人眼可读的分级展示；精确排序仍由连续的战斗价值分数决定。

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
Source/AutoEquipment/
├── AutoEquipment.csproj       # C# 7.3 项目文件
├── ModController.cs           # MOD 入口，StaticConstructorOnStartup
├── HarmonyPatches.cs          # Harmony 补丁：Comp 注入 + 取消征召恢复
├── CompGearManager.cs         # ThingComp，Tick 入口与评估协调
├── GearScorer.cs              # 评分门面
├── GearContext.cs             # 情境检测
├── PawnRole.cs                # 角色检测
├── PawnSuitabilityChecker.cs  # Pawn 适配性过滤
├── DLCCompat.cs                # DLC API 安全包装
├── SidearmAllocator.cs        # 副武器全局分配
├── DebugHelper.cs             # AEDebug 日志工具
├── DebugMonitor.cs            # 调试监测
├── SGSettings.cs              # 设置窗口与持久化
├── ITab_GearManager.cs        # 装备管理面板
└── Scoring/
    ├── IScorer.cs             # 评分策略接口
    ├── ScoringPipeline.cs     # 评分管线
    ├── ScoringPipelineFactory.cs
    ├── ScoreBreakdown.cs      # 评分明细
    ├── GearWeights.cs         # 权重结构 + 4 预设
    ├── GearPreset.cs          # 预设枚举
    ├── GearPolicyEngine.cs    # 策略调度
    ├── Weapon/                # 9 个武器 Scorer
    └── Apparels/              # 11 个防具 Scorer
```

### 评估周期

| 路径 | 周期 | 说明 |
|------|------|------|
| `CompTick` 主评估 | `evaluateInterval`（默认 500 tick ≈ 8 秒） | 武器/防具/药品/副武器 |
| 征召副武器检查 | 30 tick | 战斗紧迫，需快速切近战 |
| `SidearmAllocator` | 2000 tick | 全局副武器分配 |
| 角色缓存 | `RoleCacheInterval` | 避免每 tick 重复检测 |

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

## 构建

```bash
make check          # 验证零警告零错误（规则强制）
make build          # 构建
make rebuild-check  # 完整重建后检查
```

要求 `.NET` SDK 与 RimWorld 1.6 的 `Assembly-CSharp.dll` 引用路径已配置。

## 许可证

详见 [LICENSE](./LICENSE)。
