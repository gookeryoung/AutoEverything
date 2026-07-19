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

- **Core**：基础工具与全局状态（MOD 入口 `ModController`、`AutoEverythingGameComponent` Tick 入口、`HarmonyPatches`、`AESettings`（含 `AESettings.TierTag.cs` partial）、`AEDebug`、`DLCCompat`、`PawnSuitabilityChecker`、`PawnJobGuard`、`PawnCollector`、`TierCacheService`、`AutoExecutor`、`CombatTier`、`ColonistBarSortMode`、`PassionHelper`）
- **RoleEvaluation**：角色与情境评价（`PawnRole`/`RoleDetector`、`GearContext`/`ContextDetector`、`CombatEvaluator`）
- **AutoWork**：自动工作优先级分配（`WorkAllocator`、`WorkAllocationConfig`）
- **AutoMarkPawn**：高价值自动标记（S+ 档次所有人类单位头顶彩色星标实时绘制，按类别区分颜色，事件驱动扫描，不修改 Pawn 数据）
- **UI**：玩家界面（`ITab_GearManager`）

未来扩展（自动机械族/自动训练等）应在 `Source/AutoEverything/` 下新增独立模块文件夹，按上述命名空间约定扩展。

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
- 自定义评级识别码格式 `档次#人员名`（如 `S#王五`），存于 `AESettings.customTierEntries`
- `tierTagOriginals` 持久化到存档（Scribe Key `ae_tierTagOriginals`），避免重启后误剥离玩家手动改的 Nick

### 自动工作分配原则

- 按照工作类别设置不同的优先级分配原则, 所有殖民者包括奴隶均参与分配
- 分配工作时考虑两因素：对应技能兴趣（双火>单火>无火）、技能等级（数值）
- 紧急工作（灭火/就医/休养），所有殖民者优先级1
- 关键专业工作（保育/监管/医生/烹饪/割除），双火优先级1，单火优先级2，无火优先级0
- 普通专业工作（建造/采矿/种植/缝制/艺术/锻造/制作），双火优先级2，单火优先级3，无火优先级0
- 次级专业工作（驯兽/狩猎），双火优先级2，单火优先级4，无火优先级0
- 以上专业工作类型，保底2人从事，即使无火也保证优先级3，如新增更适合此工作的，原优先级3的降至0
- 研究工作（暗黑调查/研究），双火优先级2，单火优先级3，无火优先级0
- 辅助工作（搬运/清洁等），评级S及以上者优先级4，A优先级3，其余优先级1

## 调试系统

- `AEDebug.Log` 提供 `Func<string>` 重载，延迟字符串构造避免 Tick 路径 GC
- `AEDebug.IsActive` 读取 `AESettings.debugLogging`，玩家切换立即生效

## 全局分配系统

- 工作重配入口：`WorkAllocator.ReallocateAll`（由 `AutoExecutor` 事件驱动调度，ITab 勾选框触发）
- 评级标签入口：`AESettings.ApplyTierTagsToAllPawns`（仅更新 Nick 前缀）/ `ApplyTierTagsWithDefaultSort`（同时重排殖民者栏）

## 同步计算规则（强制）

> 评分模型、权重、计算公式是面向玩家的契约，**修改代码必须同步更新文档**
> 文档与代码不一致视为"未完成"，禁止提交

### 必须同步 README.md 的变更类型

修改以下任一内容时，必须同步更新 `README.md` 对应章节：

1. **角色检测规则**（`PawnRole.cs` / `RoleDetector`）
   - 同步章节：`## 角色检测规则` 表格

2. **情境检测规则**（`GearContext.cs` / `ContextDetector`）
   - 同步章节：`## 情境检测规则` 表格

3. **评级规则**（`CombatEvaluator.cs` 评级判定）
   - 同步章节：`## 全局价值评级档次（CombatTier）` 表格

4. **评级方法分层**（`CombatEvaluator` 的 `GetCombatTier`/`GetSystemTier`/`GetAutoCombatTier`）
   - 同步章节：`### 评级方法分层` 表格

5. **战斗价值公式权重**（`AESettings.cs` 中的 `cv*` 字段）
   - 同步章节：`### 战斗价值公式` 表格

6. **价值评分公式**（`CombatEvaluator.ComputePawnValueScore`）
   - 同步章节：`### 价值评分`

7. **自定义评级识别码**（`AESettings.cs` 自定义评级读写）
   - 同步章节：`### 自定义评级识别码`

8. **评级标签与殖民者栏排序**（`AESettings.TierTag.cs`、`ColonistBarSortMode`）
   - 同步章节：`### 全局人物评级标签` + `### 殖民者栏默认排序`

9. **工作分配规则**（`WorkAllocator.cs` / `WorkAllocationConfig.cs`）
   - 同步章节：`## 自动工作分配` 分配规则表格与统一四大原则

10. **奴隶处理**（`WorkAllocator.cs` 奴隶收集/分配）
    - 同步章节：`## 奴隶处理`

11. **自动执行调度**（`AutoExecutor.cs`）
    - 同步章节：`## 自动执行（AutoExecutor）` + `### 评估周期` 表格

12. **GameComponent 入口**（`AutoEverythingGameComponent.cs` / `HarmonyPatches.cs` 的 `Game_FinalizeInit_Patch`）
    - 同步章节：`### 评估周期` 表格 + `## 设计原则：逻辑杜绝而非事后清理`

13. **高价值自动标记**（`PawnMarker.cs` / `AutoMarkPawn` 模块）
    - 同步章节：`### 高价值自动标记（AutoMarkPawn）`

14. **ITab 底部勾选框**（`ITab_GearManager.cs`）
    - 同步章节：`## 自动执行（AutoExecutor）` 入口章节

15. **设计原则**（不适用 Pawn 的处理逻辑）
    - 同步章节：`## 设计原则：逻辑杜绝而非事后清理`

16. **护甲偏好规则**（`PawnRole.cs` / `GetArmorPreference`）
    - 同步章节：`## 角色检测规则` 末尾的护甲偏好说明

17. **新增/删除源文件**（目录结构变更）
    - 同步章节：`### 目录结构` 代码块

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

- 面板尺寸 `360f × 420f`（高度容纳底部 3 勾选框），内容区用 ScrollView 包裹（inner rect 宽度比 outer 少 16f）
- 缓存周期 60 tick：角色/情境/评级/数值摘要避免每帧重算
- 徽章行 4 列等宽：角色 / 情境 / 评级 / 护甲偏好（食尸鬼用"食尸鬼"徽章替代护甲偏好）
- **文字防换行强制**：所有 `Widgets.Label` 绘制前 `Text.WordWrap = false`，绘制后恢复
- **标签宽度动态计算**：用 `Text.CalcSize(labelText).x + 留白`，禁止固定宽度（如 `60f`）
- 完整信息放 Tooltip，徽章/标签本身只做概览
- 底部 3 勾选框，固定位置不随滚动：
  - 人员自动评级（`autoTierTag`）
  - 工作自动配置（`autoWorkEnabled`）
  - 高价值自动标记（`autoMarkPawn`）
- **勾选框行为说明**：勾选框勾选立即触发一次 + 启用周期自动；取消勾选仅停止自动（保留当前配置）。评级取消勾选额外清除所有 Nick 评级前缀
