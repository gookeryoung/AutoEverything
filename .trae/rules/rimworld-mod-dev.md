# RimWorld MOD 开发规则

> 强制约定，中文优先。适用于 RimWorld 1.6+，覆盖 C# DLL 与 XML Def 扩展。

## 命名约定

- `packageId`: `gookeryoung.mod名` 小写，发布后不可改
- 命名空间: 与 MOD 名一致，PascalCase（如 `AutoEquipment`）
- DefName: `Mod缩写_类别_名称`，禁止使用原生命名空间（`Gun_`/`Bullet_`）
- 翻译 Key: `Mod缩写_类别_名称`，禁止使用原生 Key
- Scribe Key: 带 MOD 缩写前缀（如 `ae_locked`）

## C# 代码

- 锁定 C# 7.3，`.csproj` 显式 `<LangVersion>7.3</LangVersion>`
- 禁用 C# 8.0+ 语法（using 声明、可空引用、范围运算符）
- 注释用中文，解释「为什么」而非「做什么」
- 空值防御: 链式访问用 `?.`，禁止裸访问 `pawn.skills.X`
- 日志前缀 `[Mod缩写]`，异常用 `Log.ErrorOnce` 防重复

## UI 与窗口

- 设置窗口内容超出可见区域时必须用 `Widgets.BeginScrollView/EndScrollView` 包裹
- ScrollView 的 inner rect 必须从 `(0, 0)` 开始，禁止用屏幕坐标作起点——否则内容会偏移到 ScrollView 外导致不可见
- inner rect 宽度比 outer 少 16f（预留给滚动条），高度按内容预估并留余量
- `Listing_Standard` 在 ScrollView 内绘制时直接 `Begin(innerRect)`，由 ScrollView 自动应用坐标变换
- 设置类（继承 `Mod`）需用 `static Vector2` 字段保存滚动位置，避免每次重置
- 自定义 `Window` 子类必须重写 `InitialSize`，且窗口大小需能容纳所有按钮（每按钮约 30f 高）
- 设置项超过 8 个时必须用双列布局：把 `viewRect` 分成左右两半，分别 `Begin` 两个 `Listing_Standard`
- 多个操作按钮（如调试工具）必须并排放置：用 `GetRect(30f)` 取一行后按 `btnWidth = (width - gap) * 0.5f` 分割，避免按钮纵向堆叠挤压可见区
- 优先用 `CheckboxLabeled` 替代独立标签+控件，减少行数

## Harmony 补丁

- Harmony ID: `作者.mod名`，整个 MOD 单一实例
- 优先 Postfix（零侵入），避免 Transpiler（极高风险）
- Prefix 拦截（`return false`）必须复现原方法副作用
- 多 MOD Patch 同一方法时用 `Priority.Last`
- 通过 `[StaticConstructorOnStartup]` 初始化，禁止静态构造中 PatchAll

## XML Def

- 禁止修改游戏原始 XML，必须用 `Patch` 操作
- Patch 必须防御性: `PatchOperationConditional` 先检查再操作
- Xpath 用 `defName` 精确匹配，禁用顺序索引
- 继承用 `<ParentName>`，禁止修改原生抽象 Def

## 性能

- Tick 路径: 禁止 LINQ、禁止 `new List<>()`、禁止 `OrderBy`
- Tick 取模分散: `(TicksGame + thingIDNumber % interval) % interval != 0` 则 return
- 集合用静态缓存或实例字段复用
- 查询用 `GenClosest.ClosestThingReachable` 与 `ThingsInGroup`
- 缓存 TTL ≥ 2500 tick，状态变化时失效
- 日志在 `if (debugActive)` 后短路

## 兼容性

- DLC API 调用前必须 `ModsConfig.XActive` 检查（如 `AnomalyActive`、`BiotechActive`）
- `DefDatabase.GetNamed` 后必须 null 检查
- ThingComp 注入: 运行时遍历 `DefDatabase` 添加，检查是否已存在
- 依赖 MOD 必须在 `About.xml` 声明 `modDependencies` 与 `loadAfter`

### Pawn 适配性过滤

- ThingComp 注入前必须用 `PawnSuitabilityChecker.CanManageGearDef(def)` 过滤：
  动物（`race.Animal`）、机械族（`race.IsMechanoid`）、昆虫（`race.Insect`）、异常实体等不适用类别禁止注入
- Pawn 运行时入口（`SpawnSetup` Postfix、`CompTick`、`TriggerReload`）必须再加一层 `CanManageGear(pawn)` 检查，覆盖旧存档已注入异常 Comp 的情况
- 兜底防御：`CompTick` 检测到不适用 Pawn 时静默 `parent.AllComps.Remove(this)` 自移除，避免 Tick 持续空转
- 仅 `race.Humanlike` 适合装备管理（含外星人 mod 的类人种族）
- 食尸鬼（`DLCCompat.IsGhoul`）即使种族是人类like 也必须排除

### TraitDef 安全查询

- 多 degree 特质（如 `ShootingAccuracy`、`Industriousness`、`Neurotic`、`Beauty`）的 defName 是单一的，degree 区分变体：
  - `ShootingAccuracy` degree=-1 是乱开枪、degree=+1 是冷枪手
  - `Industriousness` degree=2 是勤奋、1 是努力工作、-1 是懒惰
- 禁止把 degree 的 label（如 `TriggerHappy`、`CarefulShooter`）当作 defName 查询
- 自定义 TraitDef 查询用 `DefDatabase<TraitDef>.GetNamed(defName, false)` 安全版本，未找到返回 null 而非抛异常
- Tick 路径必须用静态字段缓存 TraitDef 查询结果，避免每次重复字典查询
- 原生 DefOf（如 `TraitDefOf.Brawler`）始终存在，无需 null 检查

## 存档安全

- `PostExposeData` 中调用 `Scribe_*`，每个字段必须有默认值
- Thing 引用用 `Scribe_References.Look`，禁止 `Scribe_Values` 存引用
- 数据格式变更保留向后兼容读取

## 本地化

- 必须同时提供 `ChineseSimplified` 与 `English` Keyed XML
- 禁止硬编码显示字符串，必须 `"Key".Translate()`
- 动态字符串用 `{0}` `{1}` 占位符
- `About.xml` 的 `description` 必须用中文撰写，可附英文摘要
- 面向玩家的字符串、README、Steam 创意工坊简介均以中文为第一语言

## 发布检查

- `.csproj` 引用设 `<Private>false</Private>`，禁止打包 Assembly-CSharp.dll
- `About.xml` 完整: `supportedVersions`、`packageId`、`modDependencies`、`description`（中文）
- `dotnet build -c Release` 零错误零警告
- 无 DLC 环境测试启动，有 DLC 环境测试功能

## 更改后强制检查（强制）

- **每次**修改 C#/XML/Makefile 后，必须立即执行 `make check` 验证零警告零错误
- `make check` 通过 `-warnaserror` 把警告升级为错误，任何警告都会导致失败
- 失败情形：编译错误、任意警告、输出 DLL 缺失
- 不得跳过此步骤直接交付；如失败必须先修复再继续后续工作
- 检查命令：在项目根目录执行 `make check`
- 完整重建后检查：`make rebuild-check`
- 命名空间必须与文件夹结构匹配（IDE0130 规则）：
  - `Source/AutoEquipment/Scoring/*` → `namespace AutoEquipment.Scoring`
  - `Source/AutoEquipment/Scoring/Weapon/*` → `namespace AutoEquipment.Scoring.Weapon`
  - `Source/AutoEquipment/Scoring/Apparels/*` → `namespace AutoEquipment.Scoring.Apparels`
- 跨命名空间引用必须显式 `using`，禁止依赖 IDE 自动补全
- 提交前必须 `make check` 通过，否则禁止视为"已完成"

## 同步计算规则（强制）

> 评分模型、权重、计算公式是面向玩家的契约，**修改代码必须同步更新文档**。
> 文档与代码不一致视为"未完成"，禁止提交。

### 必须同步 README.md 的变更类型

修改以下任一内容时，必须同步更新 `README.md` 对应章节：

1. **角色检测规则**（`PawnRole.cs` / `RoleDetector`）
   - 修改角色判定优先级、新增/删除角色枚举值、调整阈值（如 `>= 8` 改为 `>= 10`）
   - 同步章节：`## 角色检测规则` 表格

2. **情境检测规则**（`GearContext.cs` / `ContextDetector`）
   - 新增/删除情境枚举值、修改触发条件、调整持续 tick 阈值
   - 同步章节：`## 情境检测规则` 表格

3. **权重模型**（`GearWeights.cs`）
   - 新增/删除权重字段、修改字段语义、调整任一预设方案的默认值
   - 同步章节：`## 评分模型 → 权重预设方案` 与 README 中字段说明

4. **评分管线**（`ScoringPipelineFactory.cs`）
   - 新增/删除 Scorer、调整 Scorer 执行顺序、修改管线构造逻辑
   - 同步章节：`## 武器评分管线` / `## 防具评分管线` 表格

5. **评分公式**（任一 `IScorer` 实现的加分逻辑）
   - 修改兴趣乘数（如单火 1.5 → 1.8）、修改硬编码特质加分、修改 Veto 阈值
   - 同步章节：对应 Scorer 的"说明"列与 `## 总分公式`

6. **副武器分配**（`SidearmAllocator.cs`）
   - 修改战斗价值公式、调整分配周期、修改优先级规则
   - 同步章节：`## 副武器全局分配` 与公式块

7. **预设方案**（`GearPreset.cs` / `GearPolicyEngine.cs`）
   - 新增/删除预设枚举值、修改方案特点说明
   - 同步章节：`## 权重预设方案` 表格

8. **评估周期**（`CompGearManager.cs` Tick 路径）
   - 修改 `evaluateInterval` 默认值、调整 Tick 分散策略、新增周期路径
   - 同步章节：`## 评估周期` 表格

9. **设计原则**（不适用 Pawn 的处理逻辑）
   - 修改入口防御策略、调整兜底处理方式
   - 同步章节：`## 设计原则：逻辑杜绝而非事后清理`

10. **主武器选择规则**（`WeaponSkillScorer.cs` / `WeaponTraitScorer.cs` / `SidearmAllocator.cs`）
    - 修改 Brawler 特质 Veto、双修远程偏好、纯近战 EMP 副武器、贴身切换逻辑
    - 同步章节：`## 主武器选择规则` 表格

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
- [ ] `make check` 通过

### 文档语言

- README.md 与 rimworld-mod-dev.md 均以中文为主语言
- 公式、字段名、类名保留英文原文
- 玩家可见的说明必须可读，禁止纯技术黑话

