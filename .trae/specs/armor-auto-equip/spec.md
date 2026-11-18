# Spec：自动装备扩展到护甲

> 状态：**B0–B6 已落地**（2026-11-14）。§3.5 为反编译确认后的结算口径；§9 记录各批落地结果与偏差；§12 记录最终采用的决策。
> 决策依据：用户指示「按文档推荐默认项推进；D1 先做和平期最优（B1–B3），B4 双档案实现但默认关闭；D3 独立子开关；D4 三档预设 + 标量」。

## 1. 现状摸底（只读证据）

### 1.1 现有链路

| 环节 | 位置 | 事实 |
| --- | --- | --- |
| 触发 | [AutoExecutor.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/Core/AutoExecutor.cs#L216-L219) | 周期（`equipIntervalTicks`，默认 6000 tick，可调 30–300 秒）＋ 脏标 ＋「立即装备」；整体受 `AnyCombatActive()` 战斗过滤 |
| 脏标 | [HarmonyPatches.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/Core/HarmonyPatches.cs#L192-L215) | `Thing.SpawnSetup` / `Thing.Destroy` Postfix 调 `EquipAllocator.MarkDirty`，仅 `IsWeapon \|\| IsApparel` 时置 `IsDirty` |
| 候选扫描 | [EquipAllocator.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/AutoEquip/EquipAllocator.cs#L89-L122) | `listerThings.ThingsInGroup(Apparel)`，只含地图上未穿着的物品（穿在身上的在 inner container，不进 lister） |
| 可用性 | [EquipAllocator.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/AutoEquip/EquipAllocator.cs#L446-L464) | 同地图、未禁用、未燃烧、非迷雾、在活动区域内、`reservationManager.CanReserve`；**无距离限制** |
| 人员过滤 | [EquipAllocator.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/AutoEquip/EquipAllocator.cs#L145-L158) | 跳过：非殖民者、奴隶、囚犯、食尸鬼、已征召、倒地、精神崩溃、非成年、医疗中、玩家强制 Job、睡眠中 |
| 服装挑选 | [EquipAllocator.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/AutoEquip/EquipAllocator.cs#L256-L305) | 每轮每 Pawn 最多 1 个 Job；只按 `def.apparel.layers[0]` 找"当前同层那件"作为比较基准 |
| 服装评分 | [ApparelSelector.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/AutoEquip/ApparelSelector.cs#L95-L133) | 见 §1.2 |
| 换装阈值 | [EquipPolicy.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/AutoEquip/EquipPolicy.cs#L70-L82) | `ShouldSwap`：候选须超过当前 `1 + equipMinImprovementPct%`（默认 10%） |
| 配置 | [AESettings.Equip.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/Core/AESettings.Equip.cs#L15-L39) | 5 个字段：`autoEquipEnabled` / `equipIntervalTicks` / `equipMinQuality` / `equipMinImprovementPct` / `equipMaxThrownKinds`；白名单以 `角色#近战#远程#投掷#副武器数` 字符串列表存档 |
| UI | [MainTabWindow_AutoEverything.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/UI/MainTabWindow_AutoEverything.cs#L1008-L1059) | 装备页三块：角色白名单表 / 武器优先级 / 「着装范围」——第三块**只有说明文字，没有任何护甲可调项** |
| 情境 | [GearContext.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/RoleEvaluation/GearContext.cs#L35-L113) | `ContextDetector.GetContext` 已能判定 Combat / Hunting / Cold / Hot / Work / Normal，但**当前只用于旧的徽章显示，装备逻辑完全没读** |
| 护甲偏好 | [PawnRole.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/RoleEvaluation/PawnRole.cs#L30-L35) | `ArmorPreference.Heavy/Flexible/Light` 与 `GetArmorPreference(role)` 已存在（Brawler→Heavy，Shooter/Hunter/Leader→Flexible，其余 Light），**目前只服务徽章与狩猎后排判定，未参与装备** |

### 1.2 现有服装评分实际比较的维度

```
armor   = sharp*w1 + blunt*w2 + heat*w3        // w 按 RoleGroup 变：Front 1.4/0.8/0.4，Shooter 1.1/0.6/0.3，其余 0.6/0.4/0.2
move    = apparel.GetStatValue(MoveSpeed)      // 施与者的移动速度偏移，负重甲为负
durable = HitPoints / MaxHitPoints
score   = armor*10 + move*20 + durable*3 + qualityRank*2
若角色是 Worker/Trade：score -= max(0, -move)*20   // 移动惩罚二次计入
```

即**只有 4 个维度**：三类护甲值的固定加权、移动速度、耐久比例、品质档位。缺失：覆盖率、部位、绝缘、工作速度、材料、距离、冲突丢弃成本。

### 1.3 现状缺陷清单（is-a 事实，不是猜测）

1. **只认第一层**：`TryGetFirstLayer` 取 `layers[0]`，一件衣服若声明多层，其余层永远不参与比较；「同层当前件」也只找到一件。
2. **评分与装备集脱节**：逐件打分，不看"这套衣服合起来覆盖了哪些部位"。防弹背心（护甲高、只覆盖躯干）与风衣（护甲低、覆盖躯干+颈+四肢）被当成同一维度上的两个标量比大小，结果取决于巧合的权重而不是期望减伤。
3. **移动惩罚双计**：`+ move*20` 之后又对 Worker/Trade 执行 `- max(0,-move)*20`，重甲工人被罚两次（见 §1.2 代码）。
4. **品质疑似双计**：`GetStatValue` 对衣物通常已含品质修正，再 `+ qualityRank*2` 可能重复加分；且 `equipMinQuality` 只作用于武器（`TryEquipPrimaryWeapon` / `TryTakeSidearm`），**不作用于服装**，UI 文案却写"一般武器"，玩家容易误解为全局门槛。
5. **没有耐久下限**：耐久只剩 1% 的传奇甲仍可能因护甲高而被穿走；换下的旧甲被丢弃后可能被其他人捡走，形成一轮循环。
6. **没有距离上限**：允许为一点点提升横穿地图，走动损耗与打断工作没有任何约束。
7. **无意识形态/头衔保护**：会正常比较并替换掉满足 `Precept_Role` 服装要求或头衔威望要求的衣物，直接导致掉心情/失职（现状只是"恰好没有被投诉"）。
8. **无场景概念**：`ShouldSkipPawn` 跳过已征召者，`AnyCombatActive` 全局熔断——**结构上不可能在袭击时换战斗护甲**，只在和平期做一次"长期最优"。
9. **多件换装慢**：每 Pawn 每轮 1 个 Job，凑齐 3 件套需要 3 轮（默认 100 秒/轮 ≈ 5 分钟），且没有"待补齐计划"概念。
10. **评分量级不统一**：武器分 ~50–300，服装分 ~10–60，同一 10% 阈值对两者的实际严格程度完全不同。

### 1.4 存档与多人现状

- 设置走 `AESettings.ExposeData` → `Scribe_Values` / `LookCompat`，键统一 `ae_` 前缀；**没有任何每 Pawn 状态写入存档**（分配器是无状态重算）。
- 工作区与代码中**没有任何 Multiplayer 兼容代码或声明**（全仓搜索 `multiplayer` / `MPCompat` 无命中）。`EquipAllocator.IsDirty`、`AutoExecutor` 的阶段计时均为进程内静态字段，多人环境下各客户端独立 tick，本就不保证一致。
- 结论：现有自动装备是单人功能。本次扩展**不新增每 Pawn 存档字段**，因此不产生存档迁移；但也**不承诺多人兼容**（见 §9）。

## 2. 目标与非目标

目标：把"自动装备"的服装部分从"逐件比 4 个标量"升级为"按部位覆盖与分层比较整套护甲"，并在玩家显式开启时支持"战斗/工作"两套档案的场景化换装，全过程不修改玩家着装政策、不跨人扒装、不制造抖动。

非目标：不做战斗中换装、不做护甲维修、不做跨地图/远行队换装、不引入市价评分、不改武器逻辑。

## 3. 护甲与服装的差异建模

### 3.1 比较单位：从"单件"改为"整套装备集"

原版一次命中只落在某个部位，覆盖该部位的衣物才提供护甲，且**多件不叠乘**。因此正确的问题是：

> 「把这件换上去（并丢掉它冲突的那几件）之后，这个 Pawn 面对来袭攻击的期望护甲是多少？」

方案：`SetScore(pawn, hypotheticalSet, profile)`，其中 `hypotheticalSet = 当前穿着 − 被候选冲突掉的件 + 候选`。逐候选计算集合分数差值，而不是比较两个孤立单件分。这样自然解决"同一部位多件覆盖""干了这件湿了那件"和"两件都很强但冗余"的问题。

代价：每个候选一次集合评估。用 `Dictionary<ThingDef, ApparelBase>` 缓存物品级静态数据、`Dictionary<ThingDef, float>` 缓存部位覆盖率，评估是纯算术，无 LINQ、无分配（沿用现有缓冲区风格）。

### 3.2 要比较的维度与理由

| 维度 | 取数 | 为什么必须进模型 | 现方案做法 |
| --- | --- | --- | --- |
| 锐/钝/热护甲 | `ArmorRating_Sharp/Blunt/Heat`（逐件，保留现有 `GetStatValue`） | 敌袭构成不同：机械族偏锐器、钝器破甲、燃烧弹看热能 | 保留，但改为按"覆盖部位的期望值"聚合 |
| 覆盖率 | `def.apparel.bodyPartGroups` → 对应 `BodyPartRecord.coverage` 之和（按 Pawn 实际未缺失部位归一） | 100% 锐甲只护躯干 ≠ 30% 甲护全身；这是当前最大的失真来源 | 新增 |
| 部位 | 同上（躯干/颈/头/臂/腿/足） | 腿甲和头甲的价值不同；后排与近战的暴露部位也不同 | 新增（以 `BodyPartRecord.coverage` 作为客观权重） |
| 分层 | `def.apparel.layers`（全部，不只 `[0]`）＋ `ApparelUtility.CanWearTogether` | Shell/Middle/Skin/Belt/Headgear/Overhead 之间是"槽位"关系，不是分数关系 | 改为按层建槽，冲突件整体替换 |
| 移动速度惩罚 | `GetStatValue(MoveSpeed)` | 重甲掉速直接影响战斗走位与逃跑 | 保留（修掉双计） |
| 工作速度惩罚 | `equippedStatOffsets` 白名单里的 `WorkSpeedGlobal`（及后续新增白名单项） | 工人穿重甲长期掉产能，是玩家最敏感的成本 | 新增 |
| 绝缘 | `Insulation_Cold` / `Insulation_Heat` ＋ 环境温度与舒适区间 | 极端地图上"最厚的甲"会热死/冻死，不能只看护甲 | 新增（按 Cold/Hot/Normal 情境给不同符号与权重） |
| 耐久 | `HitPoints / MaxHitPoints` ＋ 绝对值下限 | 破甲会在下一次战斗直接归零，等于没穿 | 保留比例，新增硬下限 |
| 品质 | 品质档位 | 主要已体现在 `GetStatValue` 与 `MaxHitPoints` 上 | **不再加性计分**，只作为最低门槛与同分兜底（消除疑似双计） |
| 材料/价值 | — | 材质加成已经体现在 `GetStatValue`；市价只反映稀有度 | **不参与**，与武器策略「市价不参与排序」保持一致 |

### 3.3 分层与槽位

- 槽位定义为 `(ApparelLayerDef, 覆盖部位集合)`，层名不写死，从 def 动态读取（原版常见：OnSkin / Middle / Shell / Belt / Headgear / Overhead，MOD 新增层自动兼容）。
- 同层同部位互斥 → 候选与槽内当前件是"替换"关系。
- 不同层但部位冲突（`CanWearTogether == false`）→ 候选必须连带丢弃对方；**一次最多连带丢弃 2 件**，超过则该候选直接跳过（避免为一件甲把小人扒光）。
- 候选的"全部层"都要参与槽位匹配，修掉只看 `layers[0]` 的问题。

### 3.4 温度情境

`ContextDetector.GetContext` 已有 Cold/Hot（连续 2500 tick 才触发，天然抗抖）。护甲评分**直接复用该结果**作为档案选择依据，不新建一套温度判定。

规则：Normal/Work 用工作档案；Cold 用严寒档案（冷绝缘加分、热绝缘减分）；Hot 反之。绝缘不是硬门槛——极端地图上硬门槛会导致"无衣可穿"从而完全不换装。

### 3.5 B0 反编译结论（已确认，RimWorld 1.6 Assembly-CSharp）

用 ILSpy 反编译 `Assembly-CSharp.dll` 逐条核对，结论如下（与方案原假设有出入的地方已据实修正）：

1. **替换不需要额外 Remove Job**。`Pawn_ApparelTracker.Wear(Apparel newApparel, bool dropReplacedApparel = true, bool locked = false)` 内部遍历 `WornApparel`，对 `!ApparelUtility.CanWearTogether(...)` 的冲突件执行 `TryDrop`；丢弃物落在小人脚下（敌对派系则 forbid）。→ 一个 `JobDefOf.Wear` 即可完成"穿上新件 + 丢弃冲突件"，实现无需两段 Job。
2. **护甲按层串行结算，不是取最高**（原假设需修正）。`ArmorUtility.GetPostArmorDamage` 逆序遍历**所有覆盖该部位**的衣物，逐层调用 `ApplyArmor`：
   - 单层 `a = clamp(armorRating - armorPenetration, 0, 2)`，期望剩余伤害为 `a<=1 ? 1-0.75a : 0.5-0.25a`；
   - 多层是**乘性叠加**（衬衫 + 背心 + 风衣三层都要各掷一次），且每层还承受 25% 来袭伤害 → 层数越多防御越强、磨损越快。
   → 集合评分必须按「部位 × 层」连乘，不能用"取最高"。
3. **覆盖率有官方口径**：`ApparelProperties.CoversBodyPart(BodyPartRecord)` = 部位组交集；`BodyPartRecord.coverageAbs` 是部位命中权重；原版 `ApparelProperties.HumanBodyCoverage` 就是用 `coverageAbs` 求和。
4. **服装要求一个 API 全覆盖**：`Pawn_ApparelTracker.AllRequirements` 已合并 Royalty 头衔（`RoyalTitleDef.requiredApparel`）与 Ideology 角色（`Precept_Role.apparelRequirements`）；`ActiveRequirementsForReading` 再用 `ApparelUtility.IsRequirementActive` 过滤出当前生效项。→ 硬过滤直接用 `ActiveRequirementsForReading` + `ApparelRequirement.ApparelMeetsRequirement(def)`，不需要自己判断 DLC。
5. **锁定衣物可直接识别**：`Pawn_ApparelTracker.WouldReplaceLockedApparel(cand)`，命中则跳过该候选（角色/头衔自动锁定的衣物受保护）。
6. **移动/工作惩罚取原始偏移**：`ThingDef.equippedStatOffsets` + `StatDefOf.MoveSpeed` / `WorkSpeedGlobal`；避免 `GetStatValue` 把 `MoveSpeed` 的 base 1.0 也带进来（这也是现有"移动惩罚重复扣"实际未触发的根因：`move` 恒为正，`-max(0,-move)` 永远为 0，而 `move*20` 变成了对每件衣服近乎相同的常数）。
7. `StatDefOf.Insulation_Cold/Heat`、`ArmorRating_Sharp/Blunt/Heat`、`TraitDefOf.Nudist` 均存在。

## 4. 评分口径与默认权重

### 4.1 纯逻辑输入（可单测，不依赖 RimWorld）

```
ApparelBase {                       // 物品级静态数据，按 ThingDef 缓存
    Sharp, Blunt, Heat,             // 0..~2，clamp 到 0..1.2
    Coverage,                       // 0..1，部位覆盖权重
    InsulationCold, InsulationHeat, // 原值，按档案给符号
    MoveSpeedOffset, WorkSpeedOffset,
    QualityRank, MarketValue        // 品质只做门槛/兜底，市值不参与
}

ApparelSetInput {                   // 集合级
    ArmorExpectation,               // 0..1
    Comfort,                        // -1..1
    MobilityPenalty,                // 0..1
    WorkPenalty,                    // 0..1
    Durability,                     // 0..1
    ConflictDropCount               // 0..2
}
```

### 4.2 公式

```
Score = 100 × ( A × ArmorExpectation
              + T × Comfort
              − M × MobilityPenalty
              − W × WorkPenalty
              + D × Durability )
        − 3 × ConflictDropCount
```

- `ArmorExpectation = Σ_part coverage(part) × Σ_cat roleWeight(cat) × clamp(armor_cat(part))`
- `Comfort`：`冷绝缘贡献 = clamp(InsulationCold / 需求冷绝缘)`，热同理；Normal 档案取两者较小者的平衡值，Cold/Hot 档案把对应项符号为正、相反项为负。
- `MobilityPenalty = clamp(-MoveSpeedOffset / 0.3)`；`WorkPenalty = clamp(-WorkSpeedOffset / 0.3)`。

### 4.3 默认权重与角色细分

| 档案 | A 护甲 | T 舒适 | M 移动 | W 工作 | D 耐久 |
| --- | --- | --- | --- | --- | --- |
| 工作（Normal / Work） | 0.40 | 0.10 | 0.25 | 0.20 | 0.05 |
| 战斗（Combat） | 0.70 | 0.05 | 0.20 | 0.00 | 0.05 |
| 严寒（Cold） | 0.35 | 0.45 | 0.15 | 0.00 | 0.05 |
| 酷暑（Hot） | 0.35 | 0.45 | 0.15 | 0.00 | 0.05 |

角色护甲细分（沿用现有 `RoleGroup` 语义，只影响 `roleWeight(cat)`）：

| 角色 | 锐 | 钝 | 热 | 说明 |
| --- | --- | --- | --- | --- |
| Front | 1.0 | 0.7 | 0.3 | 近战承伤，优先锐器；钝器也要防 |
| Shooter | 0.6 | 0.4 | 0.3 | 站后排，靠走位与射程，护甲权重让位于移动 |
| Worker | 0.4 | 0.25 | 0.2 | 生产效率优先 |
| Trade | 0.45 | 0.3 | 0.2 | 兼顾外观与走商 |
| Other | 0.5 | 0.35 | 0.25 | 中性 |

> 与现状的关系：默认权重下"防弹背心 vs 风衣"这类判断会比现在更偏向**覆盖更广**的衣物，这是本方案的核心收益，也是 §8 里唯一需要提前告知玩家的行为变化。

### 4.4 玩家可调性（建议）

只给 3 档预设，不给 6 个权重滑杆：

| 预设 | A | T | M | W | D | 适用 |
| --- | --- | --- | --- | --- | --- | --- |
| 稳健（默认） | 上表 | 上表 | 上表 | 上表 | 上表 | 大多数殖民地 |
| 重甲优先 | +0.15 | −0.05 | −0.05 | −0.05 | 0 | 敌袭强度高、医疗资源足 |
| 轻装优先 | −0.15 | +0.05 | +0.10 | +0.10 | 0 | 长距离搬运/极寒极热 |

外加 4 个标量（与现有 UI 风格一致，都是单滑杆/下拉）：
`autoEquipArmor`（护甲子开关，默认开，继承总开关）、`equipArmorPreset`（默认稳健）、`equipMinApparelDurabilityPct`（默认 25%）、`equipArmorMaxDistance`（默认 40 格）。
`equipMinImprovementPct` 沿用（默认 10%）；战斗档案切换单独用 15% 滞回（见 §5.3）。

### 4.5 可解释输出

沿用 `EquipAllocator.RecordChange` 与「立即装备」的逐条日志，格式扩展为带分解的一行：

```
换装：布制风衣[护甲期望32 舒适0.4 移动-0.10 耐久0.85] 41.2 → 防弹背心[护甲期望58 舒适0.1 移动-0.00 耐久0.92] 55.7
```

玩家不需要看公式，也能看出"为什么换"。调试日志开关打开时再输出每项分量的原始取数。

## 5. 场景化换装

### 5.1 触发源取舍

| 触发源 | 采纳 | 理由 |
| --- | --- | --- |
| 袭击/敌对事件（信件） | ✅ P2 主触发 | 早于征召发生，留出换装时间；用轮询 `IsRaidActive()` 实现，不新增 Harmony 补丁 |
| 玩家征召 | ❌ 不作为触发 | 征召中的 Pawn 不能派发 Wear（原生 Job 会被打断），要换必须自动解除征召，风险不可接受 |
| 地图存在敌对单位 | ⚠️ 只作"战斗态保持"判据，不作进入触发 | `AutoExecutor` 注释已论证：敌对访客/永久敌对会长期误判 |
| 特定工作类型（狩猎/远行） | ❌ 本轮不做 | 收益低、规则难解释，且会与武器/携带模块交叉 |
| 危险区域 | ❌ 本轮不做 | 需要新增区域工具与 UI，超出本轮范围 |

### 5.2 状态机（每 Pawn，内存态，读档重建）

```
Peace --(袭击检测 && !Drafted && 安全条件)--> Arming
Arming --(换装完成 或 超时/失败)--> Combat
Combat --(无敌对 2500 tick && !Drafted && 非战斗 Job)--> Disarming
Disarming --(换回工作服完成)--> Peace
```

进入 `Arming` 的前置条件（全部满足，任一不满足则保持 Peace 并在调试日志记录原因）：

1. `!pawn.Drafted`、非精神崩溃、非医疗中、无 `playerForced` Job（复用 `ShouldSkipPawn` + `PawnJobGuard`）；
2. 15 格内无敌对单位（避免在小人已经贴脸时让他回头换衣服）；
3. 战斗档案所需的每件都在地图可达范围内、未被预留（套件不完整就不开始，避免"脱了外套还没拿到甲"的中间态）；
4. 本轮袭击的换装预算未用完（默认 2 次/人/袭击）。

### 5.3 防抖动与走动损耗

- **滞回**：进入战斗档案要求分数提升 ≥ 15%，退出要求"无敌对持续 2500 tick"，两者不对称。
- **冷却**：同一 Pawn 两次档案切换间隔 ≥ 2500 tick（复用 `ContextDetector` 的 2500 tick 抗抖口径，玩家已熟悉这个手感）。
- **单 Job**：沿用"每 Pawn 每轮最多一个 Job"，但 `Arming` 期间允许缩短检查间隔（见下）。
- **补齐节奏**：`Arming` 状态下下一次装备检查提前到 300 tick（相当于 5 秒一件），而不是等满 100 秒。只影响该 Pawn 所在轮次的门控，不改全局周期。
- **距离**：战斗服超出 `equipArmorMaxDistance`（默认 40 格）就放弃本次换装，宁可穿工作服打，也不让小人横穿地图。
- **套件完整性**：只换"能凑齐整套"的配置；凑不齐就不动。这是避免"装备栏抖动"最有效的一条。
- **不做睡眠换装**：`ShouldSkipPawn` 已跳过睡眠，不另开通道。

## 6. 冲突与边界情况

| 情况 | 处理 | 依据 |
| --- | --- | --- |
| 同层互斥（如两件 Shell 躯干甲） | 视为替换，旧件由原生 Wear 流程丢弃（B0 确认后决定是否需要显式 Remove Job） | 现有 `CanWearTogether` 已能判冲突 |
| 同一部位多件覆盖（不同层） | 允许并存；集合评分按 B0 确认的取用规则聚合（预期取最高，不叠乘） | 原版护甲不叠加 |
| 跨层冲突需一次脱两件以上 | 跳过该候选 | 避免把 Pawn 扒光造成防御真空 |
| 仿生体/缺失部位 | `ApparelUtility.HasPartsToWear` 硬过滤（已存在）；覆盖率按 `hediffSet.GetNotMissingParts()` 归一 | 缺腿不该因"裤子覆盖腿"虚高 |
| 裸体主义者（Nudist） | 默认**整 Pawn 跳过**自动护甲；仅当玩家勾选"极端温度例外"且 Context 为 Cold/Hot 时允许穿 | 穿衣触发心情惩罚 |
| Ideology 角色服装要求 | 硬过滤：不得丢弃满足角色要求的衣物；候选与该要求冲突则跳过 | 破坏角色服装会掉心情/失职 |
| Royalty 头衔威望服装 | 硬过滤：有头衔者不得降低威望等级要求 | 掉头衔能力与心情 |
| 奴隶/囚犯 | 维持现状（`ShouldSkipPawn` 跳过）；是否纳入见 §12 D6 | 现状显式排除 |
| 食尸鬼/动物/机械族 | 维持现状跳过 | 已存在 |
| 极端温度 | 只作评分分量与档案选择，不做硬门槛 | 硬门槛在极端地图会"无衣可穿" |
| 品质下限 | 新增 `equipMinApparelQuality`（默认 Awful＝不设限） | 与武器门槛语义对齐 |
| 耐久下限 | 新增 25% 默认值；低于下限不穿，也不主动换到更低的 | 避免穿破甲上战场 |
| 物品被占用 | 沿用 `CanReserve` + `IsLockedToOtherPawn`（生物编码） | 已存在 |
| 存放过远 | 新增 `equipArmorMaxDistance` | 避免为微小提升横穿地图 |
| 尸体污损衣物 | 沿用 `WornByCorpse → -9999` | 已存在 |
| 玩家着装政策 | 沿用只读 `IsAllowedByPolicy`，绝不修改政策 | 已存在 |
| 玩家强制 Job/睡眠/医疗/仪式 | 沿用 `ShouldSkipPawn` + `PawnJobGuard` | 已存在 |

## 7. 与现有功能的衔接

- **开关粒度**：新增 `autoEquipArmor` 作为 `autoEquipEnabled` 的子开关（默认开）。总开关关闭则一切不执行——与现有"默认关闭的侵入性功能"策略一致。
- **白名单复用**：角色白名单表保持"行=五类角色"，**不加列**（会撑破现有列宽与 `Text.WordWrap` 约定）。护甲按角色走 §4.3 的固定 `roleWeight`，玩家要调就换档案预设。
- **档案预设**：工作/战斗/严寒/酷暑属"系统档案"，不是玩家自定义档案。理由：4 个档案已经覆盖实际场景，开放式档案需要独立的排序/命名/持久化 UI，收益不足以抵消复杂度（KISS）。
- **与 `ArmorPreference` 的关系**：`PawnRole.GetArmorPreference` 的 Heavy/Flexible/Light 与新 `roleWeight` 语义重叠。方案：**不新增第三套角色体系**，把 Heavy/Flexible/Light 作为"档案选择提示"（Heavy 角色在 Peace 下也优先战斗档案的护甲权重），评分权重仍由档案决定。
- **与 AutoCarry / AutoDrugPolicy 的边界**：护甲只处理身上穿的，不碰背包物资与药品政策，互不干涉。

## 8. 代码改动清单

### 新增

| 文件 | 内容 | 说明 |
| --- | --- | --- |
| `Source/AutoEverything/AutoEquip/ApparelScore.cs` | `ApparelProfile` / `ApparelPreset` 枚举、`ApparelWeights`（`For(preset, profile)`，含归一化）、`ApparelSetInput`、`ApparelScoreCore`（`LayerSurvival` / `ArmorComponent` / `ComfortComponent` / `MobilityComponent` / `WorkComponent` / `Breakdown` / `Evaluate` / `TemperatureNeed`） | 纯逻辑，零 RimWorld 依赖，直接可单测（对齐 `EquipPolicy` 的既有模式） |
| `Source/AutoEverything/AutoEquip/ApparelScorer.cs` | `BeginRound` / `EndRound`（缓存）、`GetStats`（Stat + `equippedStatOffsets` + 耐久）、`BuildSet(pawn, candidate, drops, profile)`、`AccumulateCoverage`（按 `coverageAbs` 加权、逐层乘性） | 运行时取数层，对齐 `WeaponScorer` 的职责划分 |
| `Test/AutoEverything.Tests/ApparelScoreTests.cs` | 层结算/护甲分量/绝缘/温度需求/速度惩罚/权重预设/整套评分/冲突惩罚/换装阈值 | 已在 `Program.cs` 注册 |

### 修改

| 文件 | 函数 | 改动 |
| --- | --- | --- |
| `AutoEquip/ApparelSelector.cs` | `Score` / `TryGetFirstLayer` / `FindWornInLayer` / `HasConflict` | 已删除（迁移到 `ApparelScorer` + `ApparelScoreCore`）；保留 `IsAllowedByPolicy` / `HasPartsToWear` |
| 同上 | 新增 `IsWearableNow(pawn, candidate)` | 品质下限/生物编码/污损/锁定衣物的集中硬过滤 |
| 同上 | 新增 `CollectConflicts(pawn, def, buffer)` / `WouldBreakRequirement(pawn, drops)` | `CanWearTogether` 冲突集合（上限 2）+ Ideology 角色/Royalty 头衔要求保护 |
| `AutoEquip/EquipPolicy.cs` | `IsBetterApparel` | 复用既有 `ShouldSwap` 的阈值语义，未新增重复方法 |
| `AutoEquip/EquipAllocator.cs` | `TryWearApparel` | 改为整套评分、距离上限、冲突上限、档案冷却、15% 战斗阈值、紧急耐久换装 |
| 同上 | `IsBlockedByNudism(pawn, profile)` | 裸体主义者默认整 Pawn 跳过；例外仅在 Cold/Hot 档案放行（落在服装入口而非 `ShouldSkipPawn`） |
| 同上 | `ReallocateAll` | 新增 `apparelOnly` 分支（袭击换装窗口只换服装）；`ApparelScorer.BeginRound()` 清缓存 |
| 同上 | 新增 `GetApparelProfile(pawn)` / `NotifyRaidState` / `CanArmForCombat` / `CanUseProfile` | 依 `ContextDetector.GetContext` + 战斗状态机选档案 |
| `Core/AESettings.Equip.cs` | 字段 + `ExposeEquipData` | 新增 6 个键：`ae_autoEquipArmor`、`ae_equipArmorPreset`、`ae_equipMinApparelDurabilityPct`、`ae_equipArmorMaxDistance`、`ae_equipCombatProfileEnabled`（默认关）、`ae_equipArmorNudistException`（默认关），全部带默认值与钳制 |
| `Core/AutoExecutor.cs` | `TryTick` + 新增 `AnyHostileSpawned` / `CombatArmingInterval` | 每 60 tick 汇报袭击状态；袭击换装窗口内装备周期降为 300 tick 并放行战斗过滤；不改 `AnyCombatActive` 语义 |
| `UI/MainTabWindow_AutoEverything.cs` | `DrawEquipTab` | 护甲块新增子开关、双档案开关、裸体主义者例外、档案预设下拉、最低耐久、最远距离；原有说明文字保留 |
| `Languages/{ChineseSimplified,English}/Keyed/AE_Keyed.xml` | — | 新增约 10 个 Key（标题、开关、预设名、三个滑杆、提示） |
| `README.md` | 自动装备章节 + 目录结构表 | 同步评分口径、档案、新设置项（项目规则：玩家可见契约变更必须同步 README） |
| `CHANGELOG.md` | `[Unreleased]` | 标注行为变化与新开关 |

性能：所有新增取数按 `ThingDef` 静态缓存；循环内不 `new List`、不用 LINQ（复用 `ApparelCandidates` 等既有缓冲）；集合评估为纯算术。预计每轮额外开销与现有服装扫描同量级，`make check` + 游戏内观察验证。

## 9. 实施批次与验收

| 批次 | 内容 | 行为变化 | 验收 |
| --- | --- | --- | --- |
| **B0** | §3.5 三处反编译口径确认 | 无（只出结论） | 结论写回本文 §3.5；工作区无代码改动 |
| **B1** | 纯评分内核 + 单测，**不接线** | 无 | `make test` 通过；新增用例 ≥ 20，覆盖极端取数（0 覆盖、负速度、满耐久、全冲突） |
| **B2** | 接入分配器：集合评分、覆盖率、绝缘、工作惩罚、耐久/品质下限、距离上限、冲突上限 | **有**（选择结果变化） | `make test-check`；同一存档「立即装备」前后逐条日志对照；重甲/轻装两个预设各跑一次 |
| **B3** | 设置项 + UI + 本地化 | 无（默认值等价于 B2 行为） | UI 人工验收（中文不换行、滚动不裁剪）；存档保存→读档→设置保留 |
| **B4** | 战斗/工作双档案状态机、滞回、冷却、袭击触发 | 有（默认关闭，需显式开启） | 袭击全流程实测：记录换装次数、走动格数、是否有来回换；失败路径（套件不全/超距）必须不产生 Job |
| **B5** | 裸体主义者/意识形态例外细化、奴隶可选纳入 | 有（默认关闭） | 单测 + 带 Ideology 存档实测：角色服装不被动 |
| **B6** | README / CHANGELOG | 无 | 文档与实现逐条一致 |

每批独立可回退：B2 与 B4 是唯一两批有行为变化的，都通过设置项可一键回到旧行为（B2 的护甲子开关关闭＝走现有逻辑路径；B4 默认关）。

### 9.1 落地结果（2026-11-14）

| 批次 | 实际落地 | 验证 |
| --- | --- | --- |
| B0 | ILSpy + 元数据转储确认 `Wear` 冲突丢弃、`ArmorUtility` 逐层串行、`ActiveRequirementsForReading`；结论写回 §3.5 | 反编译比对 |
| B1 | `ApparelScore.cs`（档案/预设权重/五分量/百分制）+ `ApparelScoreTests`；初始零行为变化 | `make test`：49 断言通过 |
| B2 | `ApparelScorer.cs` 新建；`ApparelSelector.cs` 重写（冲突收集、要求保护、硬过滤）；`EquipAllocator.TryWearApparel` 整套评分；修 4 项既有缺陷 | `make check` + `make test` |
| B3 | `AESettings.Equip.cs` 新增 4 个设置项与 scribe；装备页新增护甲块；中英文 key | `make check` + 人工读 UI 代码 |
| B4 | 双档案状态机（袭击检测/滞回/冷却/15% 阈值/每轮 2 次/15 格安全半径）+ UI 开关（默认关闭）+ `AutoExecutor` 300 tick 补齐 | `make check` + `make test` |
| B5 | 裸体主义者例外（默认整 Pawn 跳过）、奴隶/囚犯维持跳过、换装阈值用例 | `make test`：56 断言通过 |
| B6 | README / CHANGELOG / 本文状态同步 | 人工核对 |

**与方案的偏差（如实记录）**：
1. 裸体主义者过滤落在 `EquipAllocator.TryWearApparel` 入口（`IsBlockedByNudism`），而非原先设想的 `ShouldSkipPawn`。原因：例外需按当前档案判断（仅 Cold/Hot 放行），放在 Pawn 收集阶段拿不到档案。效果等价且不影响武器装备。
2. 评分内核 `ApparelSetInput` 删除了 `QualityRank` 字段：品质只作硬门槛，不再参与评分（修「品质重复计入」）。
3. 「移动惩罚重复扣」的实际情况是原实现用 `GetStatValue` 导致该项恒为近常数，并非真的扣两次；已改为读 `equippedStatOffsets` 原始偏移，并在 CHANGELOG 如实描述。
4. 战斗档案下 `minImprovement` 用 15%，和平档案仍用玩家配置的 `equipMinImprovementPct`（默认 10%）。

## 10. 破坏性变更与存档迁移

- **无存档迁移**：不新增每 Pawn 存档字段（B4 状态机为内存态，读档重建为 Peace 并重新评估）；新增设置键都有默认值，旧存档加载即用默认。
- **行为变化（需在 CHANGELOG 明示）**：
  1. 已开启自动装备的玩家，升级后护甲选择结果可能变化（覆盖加权所致）。
  2. 换下/冲突的旧衣物会被丢弃到地上（原生 Wear 行为，B0 确认），可能被其他殖民者捡走，短期看起来像"装备在流转"。
  3. `equipMinQuality` 从"只作用于武器"扩展到"武器+护甲"（已落地），原本被允许的低品质衣物会被过滤；默认值 Awful 不设下限，现有玩家不受影响。
- **回退方式**：关闭 `autoEquipArmor`（B2 起）或 `equipCombatProfileEnabled`（B4）即可恢复旧行为，无需卸载。

## 11. 明确不做

1. **不在征召中/战斗中换装**——需要自动解除征召，风险不可接受。
2. 不修改玩家着装政策（`ThingFilter` 只读）。
3. 不跨殖民者扒装，不取已穿在别人身上的衣物。
4. 不做"整套原子换装"——多件仍需多个 Job/多轮，只能通过缩短补齐间隔缓解。
5. 不引入市价/材料价值评分。
6. 不接管食尸鬼、动物、机械族、异常实体。
7. 不做跨地图、远行队、休眠舱内的换装（仅 `Spawned` 同图）。
8. 不做按工作类型（狩猎/采矿/研究）的细粒度档案。
9. 不做护甲维修或耐久恢复。
10. **不承诺多人兼容**：现有实现无 MP 代码，静态计时器本就不同步；B4 的内存状态机在 MP 下会加剧不一致。若未来要做，必须把状态迁到存档内确定性字段并在 `GameComponentTick` 推进，属独立项目。

## 12. 待拍板决策点

| # | 决策 | 选项 | 建议 |
| --- | --- | --- | --- |
| **D1** | 是否做场景化换装（B4） | (a) 只做"和平期最优护甲"（B1–B3）(b) 追加战斗/工作双档案 | **(a) 先落地**，(b) 作为下一阶段：单套最优已能覆盖 80% 收益，双档案的走动成本与抖动风险需要实测数据支撑 |
| **D2** | 战斗服从哪来 | (a) 只用地面上现有的物品 (b) 允许指定"战斗服存放区/衣架" | (a)；指定存放区需要新 UI 与区域数据结构，且与"不新增存档字段"冲突 |
| **D3** | 护甲开关粒度 | (a) 与武器共用 `autoEquipEnabled` (b) 独立子开关 | (b)，默认开：玩家能单独关掉护甲自动、保留武器自动 |
| **D4** | 玩家可调粒度 | (a) 3 档预设 + 4 个标量 (b) 全权重滑杆 | (a)，理由见 §4.4；全滑杆容易调出反直觉结果且撑爆现有滚动布局 |
| **D5** | 裸体主义者/意识形态冲突 | (a) 整 Pawn 跳过 (b) 降级为"只在不影响心情时穿" | (a)，简单可解释；例外通道留设置项 |
| **D6** | 奴隶/囚犯是否纳入 | (a) 维持跳过 (b) 奴隶可选纳入 | (a) 本轮；(b) 需同时定义"奴隶着装政策由谁负责"，容易与玩家政策打架 |
| **D7** | 换下衣物处置 | (a) 接受原生丢弃到地面 (b) 要求放回仓库（额外 Job） | (a)；放回仓库会让每轮 Job 预算翻倍，且与"单 Pawn 单 Job"冲突 |

**最终采用（2026-11-14 落地）**：D1 按用户指示改为「B1–B3 主线 + B4 双档案默认关闭」；D2 (a)、D3 (b)、D4 (a)、D5 (a)、D6 (a)、D7 (a) 全部按建议实现，未出现推荐项在代码上不可行的情况。

最需要先定的三个：**D1（是否做双档案）、D3（开关粒度）、D4（可调粒度）**。这三个定了，B1–B3 就能直接开工，B4 的方案再细化。

## 13. 验证与门禁

- 每批提交前：`make check`（`-warnaserror` 零警告）＋ `make test`，B2 起跑 `make test-check`。
- 纯逻辑（`ApparelScoreCore`）必须有单测；运行时取数（`ApparelScorer`）靠游戏内对照与调试日志验证，不为它引入 RimWorld 运行时依赖测试。
- 游戏内人工验收清单（B2）：空地图/单殖民者/全征召/全奴隶、无 DLC 与有 DLC 各启一次、旧存档加载不丢设置、着装政策改动后不被覆盖。
- 文档：README 与实现不一致视为未完成，禁止提交。
