# AutoEverything 项目专属规则

> 本文件为 AutoEverything MOD 的项目专属规则，补充通用规则（rimworld-mod-dev.md）。
> 两者叠加生效，通用规则未覆盖的项目特定约定在此声明。

## 项目标识

- MOD 名: `AutoEverything`
- `packageId`: `gookeryoung.autoeverything`（发布后不可改）
- Harmony ID: `gookeryoung.autoeverything`（与 packageId 一致，整个 MOD 单一实例）
- 日志前缀: `[AutoEverything]`
- Scribe Key 前缀: `ae_`（如 `ae_locked`、`ae_customTierEntries`）
- 设置界面显示名: `自动万物`（中英文统一，禁止用全大写 `AUTOEVERYTHING`）

## 命名空间与文件夹结构

命名空间必须与文件夹结构匹配（IDE0130 规则）：

- `Source/AutoEverything/*` → `namespace AutoEverything`
- `Source/AutoEverything/Scoring/*` → `namespace AutoEverything.Scoring`
- `Source/AutoEverything/Scoring/Weapon/*` → `namespace AutoEverything.Scoring.Weapon`
- `Source/AutoEverything/Scoring/Apparels/*` → `namespace AutoEverything.Scoring.Apparels`

跨命名空间引用必须显式 `using`，禁止依赖 IDE 自动补全。

## 核心设计原则

### 食尸鬼排除

- 食尸鬼（`DLCCompat.IsGhoul`）即使种族是 Humanlike 也必须排除在所有装备管理外
- `ContextDetector.GetContext` 中食尸鬼直接返回 `Normal`，避免 CurJob 误判为 Work
- ITab 显示时食尸鬼情境徽章显示"闲置"（`AE_Context_Idle`）而非"日常"
- 食尸鬼仍显示 ITab 与评级信息供玩家参考，但不参与自动万物分配

### 不适用 Pawn 兜底

- `CompTick` 开头必须 `CanManageGear` 检查，不适用时 `parent.AllComps.Remove(this)` 自移除
- 覆盖场景：旧存档已注入异常 Comp、其他 mod 冲突、玩家控制机械族（Mechinator DLC）

## 调试系统

- `AEDebug.Log` 提供 `Func<string>` 重载，延迟字符串构造避免 Tick 路径 GC
- `AEDebug.IsActive` 读取 `AESettings.debugLogging`，玩家切换立即生效
- `ScoreBreakdown` 加 `collectItems` 开关，性能路径跳过 List 分配
- `ScoringPipeline.EvaluateFast` 用于 Tick 路径，`Evaluate` 用于调试明细

## 全局分配系统

- 全局重配按钮触发 `GlobalAllocator.ReallocateAll`
- 战斗价值 = (射击×兴趣乘数 + 近战×兴趣乘数)，兴趣乘数：无火 1.0/单火 1.5/双火 2.0
- 自定义评级识别码格式 `档次#人员名`（如 `S#王五`），存于 `SGSettings.customTierEntries`
- `tierTagOriginals` 持久化到存档，避免重启后误剥离玩家手动改的 Nick
- 腰带附件分配：纯近战 Pawn 优先护盾腰带（+100）> 消防背包（+60）

## 同步计算规则（强制）

> 评分模型、权重、计算公式是面向玩家的契约，**修改代码必须同步更新文档**。
> 文档与代码不一致视为"未完成"，禁止提交。

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

- [ ] 改了 `GearWeights.cs`？→ README `权重预设方案` 表格已更新
- [ ] 改了 `ScoringPipelineFactory.cs`？→ README 管线表格已更新
- [ ] 改了任一 Scorer 的加分公式？→ README 对应行说明已更新
- [ ] 改了 `PawnRole.cs`？→ README 角色表格已更新
- [ ] 改了 `GearContext.cs`？→ README 情境表格已更新
- [ ] 改了 `SidearmAllocator.cs`？→ README 副武器章节已更新
- [ ] 改了 Tick 周期？→ README `评估周期` 表格已更新
- [ ] 改了 Brawler Veto/双修偏好/EMP 副武器/贴身切换？→ README `主武器选择规则` 表格已更新
- [ ] 改了 `BeltAllocator.cs`？→ README `腰带附件全局分配` 章节已更新
- [ ] 改了 `GetArmorPreference`？→ README `护甲偏好` 表格已更新
- [ ] `make check` 通过
- [ ] 调用 `uvx --from pyflowx gitt a`, `uvx --from pyflowx pymake p` 提交代码

## 文档语言

- README.md 与规则文件均以中文为主语言
- 公式、字段名、类名保留英文原文
- 玩家可见的说明必须可读，禁止纯技术黑话

## UI 资源加载

- ITab_GearManager 标记 `[StaticConstructorOnStartup]`，确保 `tierBadgeTextures`/`roleBadgeTextures` 在主线程加载
- 纹理路径约定：
  - 评级徽章：`Textures/UI/Icons/Tier/Tier_{S,A,B,C,D,X}.png`（64×64）
  - 角色徽章：`Textures/UI/Icons/Role/Role_{Brawler,Shooter,Doctor,Hunter,Worker,Pacifist,Leader,Default}.png`（64×64）
  - Mod 图标：`Textures/UI/Icons/ModIcon.png`（128×128，`About.xml` 的 `modIconPath`）
- 所有 `ContentFinder<Texture2D>.Get` 使用 `reportFailure=false`，无图回退纯色块 + 文字
- 新增枚举值必须同步添加对应 PNG 图片，否则回退纯色块（视觉不统一但不崩溃）

## ITab 面板布局

- 面板尺寸 `360f × 560f`，内容区用 ScrollView 包裹（inner rect 宽度比 outer 少 16f）
- 缓存周期 60 tick：角色/情境/评级/数值摘要避免每帧重算
- 徽章行 4 列等宽：角色 / 情境 / 评级 / 护甲偏好（食尸鬼用"食尸鬼"徽章替代护甲偏好）
- **文字防换行强制**：所有 `Widgets.Label` 绘制前 `Text.WordWrap = false`，绘制后恢复
- **标签宽度动态计算**：用 `Text.CalcSize(labelText).x + 留白`，禁止固定宽度（如 `60f`）
- 完整信息放 Tooltip，徽章/标签本身只做概览
- 底部双按钮：全局人物评级 + 全局装备重配，固定位置不随滚动
