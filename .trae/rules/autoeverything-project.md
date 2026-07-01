# AutoEverything 项目专属规则

> 本文件为 AutoEverything MOD 的项目专属规则，补充通用规则（rimworld-mod-dev.md）。
> 两者叠加生效，通用规则未覆盖的项目特定约定在此声明。

## 项目标识

- MOD 名: `AutoEverything`
- `packageId/HarmonyID`: `gookeryoung.autoeverything`
- 日志前缀: `[AE]`
- Scribe Key 前缀: `ae_`（如 `ae_locked`、`ae_customTierEntries`）
- 设置界面显示名: `自动万物`（中英文统一）

## 命名空间与文件夹结构

命名空间必须与文件夹结构匹配（IDE0130 规则），例如：

- `Source/AutoEverything/Core/*` → `namespace AutoEverything.Core`
- `Source/AutoEverything/UI/*` → `namespace AutoEverything.UI`

## 文档语言

- README.md 与规则文件均以中文为主语言
- 公式、字段名、类名保留英文原文
- 玩家可见的说明必须可读，禁止纯技术黑话

## 术语对齐

- 单火 = `Passion.Minor`（单火焰图标）
- 双火 = `Passion.Major`（双火焰图标）
- 有火 = Minor 或 Major（任一兴趣）
- 工作狂 = 勤奋 `Industriousness degree=2`
- 工作狂+严重神经质 = **AND**（同时拥有两者，缺一不可）
- 格斗者 = `TraitDefOf.Brawler`（原生特质）
- 敏捷 = `Nimble` 特质（已有 `nimbleDef`）
- 负面特质：脑子慢 SlowLearner / 纵火狂 Pyromaniac / 脆弱 Wimp / 工作懒惰或怠惰 Industriousness degree=-1/-2

### 模块职责

- **Core**：基础工具与全局状态（MOD 入口、`AESettings`、`AEDebug`、`DLCCompat`、`PawnSuitabilityChecker`、`CombatTier`）
- **RoleEvaluation**：角色与情境评价（`PawnRole`/`RoleDetector`、`GearContext`/`ContextDetector`、`CombatEvaluator`、`PawnStateCleaner`）
- **AutoEquipment**：装备评分系统（`CompGearManager` Tick 入口、`GearScorer` 门面、`GearDefClassifier` 装备分类、`Scoring/` 评分管线与各 Scorer）
- **Allocation**：全局分配策略（`GlobalAllocator`、`SidearmAllocator`、`BeltAllocator`、`PawnCombatProfile`）
- **AutoWork**：自动工作优先级分配（`WorkAllocator`）
- **AutoMarkPawn**：高价值非殖民者标记（S+ 档次头顶红色星标实时绘制，不修改 Pawn 数据）
- **UI**：玩家界面（`ITab_GearManager`、`Dialog_GlobalReallocate`、`PresetDetailsWindow`）

未来扩展（自动药物/自动食物等）应在 `Source/AutoEverything/` 下新增独立模块文件夹，按上述命名空间约定扩展。

跨命名空间引用必须显式 `using`，禁止依赖 IDE 自动补全。

## 核心设计原则

## 计算规则（关键）

### 殖民者评级

- 评级分为 SSS/SS/S/A/B/C/D/X档
- 按三大维度（乱开枪系列 / 坚韧格斗系列 / 工作狂神经质系列）细分判定
- 坚韧+格斗单火=S, 坚韧+格斗双火=SS, 坚韧+格斗双火+敏捷/格斗者=SSS
- 乱开枪+射击单火=S, 乱开枪+射击双火=SS, 乱开枪+射击双火+坚韧=SSS
- 工作狂/严重神经质+两项专业双火=S,工作狂/严重神经质+三项专业双火=SS, 工作狂+严重神经质+手工双火=SSS
- 沉鱼落雁+社交双火=S, 沉鱼落雁+社交双火+坚韧=SSS
- 存在负面特质降低一档
- A：≥2 双火 + ≥1单火, B：≥1 双火 + ≥2单火, C：其他, X: 禁止暴力

### 战斗价值评级

- 战斗价值 = (射击×兴趣乘数 + 近战×兴趣乘数)，兴趣乘数：无火 1.0/单火 1.5/双火 2.0
- 自定义评级识别码格式 `档次#人员名`（如 `S#王五`），存于 `SGSettings.customTierEntries`
- `tierTagOriginals` 持久化到存档，避免重启后误剥离玩家手动改的 Nick
- 腰带附件分配：纯近战 Pawn 优先护盾腰带（+100）> 消防背包（+60）, 其他 Pawn 优先消防背包（+100）> 护盾腰带（+60）

### 自动工作分配原则

- 按照工作类别设置不同的优先级分配原则, 所有殖民者包括奴隶均参与分配
- 分配工作时考虑三因素相匹配：对应技能兴趣（双火>单火>无火）、技能等级（数值）、工作类型
- 紧急工作（灭火/就医/休养），所有殖民者优先级1
- 重要专业工作（保育/监管/医生/烹饪/割除），保底2人从事，三因素排序高者1，低者3
- 普通专业工作（建造/采矿/种植/缝制/艺术/锻造/制作），保底2人从事，三因素排序高者2，低者3
- 次级专业工作（驯兽/狩猎），保底1人从事，三因素排序高者2，低者4
- 研究工作（暗黑调查/研究），保底1人从事，需安排专业工作少于3的三因素排序最高的优先级1，其他有火者3
- 辅助工作（搬运/清洁等），评级S及以上者优先级4，A优先级3，其余优先级1

## 调试系统

- `AEDebug.Log` 提供 `Func<string>` 重载，延迟字符串构造避免 Tick 路径 GC
- `AEDebug.IsActive` 读取 `AESettings.debugLogging`，玩家切换立即生效
- `ScoreBreakdown` 加 `collectItems` 开关，性能路径跳过 List 分配
- `ScoringPipeline.EvaluateFast` 用于 Tick 路径，`Evaluate` 用于调试明细

## 全局分配系统

- 全局重配按钮触发 `GlobalAllocator.ReallocateAll`

## 同步计算规则（强制）

> 评分模型、权重、计算公式是面向玩家的契约，**修改代码必须同步更新文档**
> 文档与代码不一致视为"未完成"，禁止提交

### 必须同步 README.md 的变更类型

修改以下任一内容时，必须同步更新 `README.md` 对应章节：

1. **角色检测规则**（`PawnRole.cs` / `RoleDetector`）
   - 同步章节：`## 角色检测规则` 表格

2. **情境检测规则**（`GearContext.cs` / `ContextDetector`）
   - 同步章节：`## 情境检测规则` 表格

3. **权重模型**（`GearWeights.cs`）
   - 同步章节：`## 评分模型 → 权重预设方案`

4. **评分管线**（`ScoringPipelineFactory.cs`）
   - 同步章节：`## 武器评分管线` / `## 防具评分管线` 表格

5. **评分公式**（任一 `IScorer` 实现的加分逻辑）
   - 同步章节：对应 Scorer 的"说明"列与 `## 总分公式`

6. **副武器分配**（`SidearmAllocator.cs`）
   - 同步章节：`## 副武器全局分配` 与公式块

7. **预设方案**（`GearPreset.cs` / `GearPolicyEngine.cs`）
   - 同步章节：`## 权重预设方案` 表格

8. **评估周期**（`CompGearManager.cs` Tick 路径）
   - 同步章节：`## 评估周期` 表格

9. **设计原则**（不适用 Pawn 的处理逻辑）
   - 同步章节：`## 设计原则：逻辑杜绝而非事后清理`

10. **主武器选择规则**（`WeaponSkillScorer.cs` / `WeaponTraitScorer.cs` / `SidearmAllocator.cs`）
    - 同步章节：`## 主武器选择规则` 表格

11. **腰带附件分配**（`BeltAllocator.cs`）
    - 同步章节：`## 腰带附件全局分配`

12. **护甲偏好规则**（`PawnRole.cs` / `GetArmorPreference`）
    - 同步章节：`## 护甲偏好` 表格

### 同步检查清单

提交前自检：

- [ ] `make check` 通过
- [ ] 调用 `uvx --from pyflowx gitt a`, `uvx --from pyflowx pymake p` 提交代码

## UI 资源加载

- ITab_GearManager 标记 `[StaticConstructorOnStartup]`，确保 `tierBadgeTextures`/`roleBadgeTextures` 在主线程加载
- 纹理路径约定：
  - 评级徽章：`Textures/UI/Icons/Tier/Tier_{S,A,B,C,D,X}.png`（64×64）
  - 角色徽章：`Textures/UI/Icons/Role/Role_{Brawler,Shooter,Doctor,Hunter,Worker,Pacifist,Leader,Default}.png`（64×64）
  - Mod 图标：`Textures/UI/Icons/ModIcon.png`（128×128，`About.xml` 的 `modIconPath`）
- 所有 `ContentFinder<Texture2D>.Get` 使用 `reportFailure=false`，无图回退纯色块 + 文字
- 新增枚举值必须同步添加对应 PNG 图片，否则回退纯色块（视觉不统一但不崩溃）

## ITab 面板布局

- 面板尺寸 `360f × 632f`（高度容纳底部 4 勾选框 + 1 按钮），内容区用 ScrollView 包裹（inner rect 宽度比 outer 少 16f）
- 缓存周期 60 tick：角色/情境/评级/数值摘要避免每帧重算
- 徽章行 4 列等宽：角色 / 情境 / 评级 / 护甲偏好（食尸鬼用"食尸鬼"徽章替代护甲偏好）
- **文字防换行强制**：所有 `Widgets.Label` 绘制前 `Text.WordWrap = false`，绘制后恢复
- **标签宽度动态计算**：用 `Text.CalcSize(labelText).x + 留白`，禁止固定宽度（如 `60f`）
- 完整信息放 Tooltip，徽章/标签本身只做概览
- 底部 4 勾选框 + 1 按钮，固定位置不随滚动：
  - 人员自动评级（`autoTierTag`）
  - 工作自动配置（`autoWorkEnabled`）
  - 装备自动重配（`autoGearEnabled`）
  - 高价值标记（`autoMarkPawn`）
  - 全局装备重配按钮（占满宽度）
