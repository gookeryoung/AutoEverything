# RimWorld MOD 开发通用规则

## 技术栈

- C# 7.3, 禁用 C# 8.0+ 语法, `.csproj` 显式 `<LangVersion>7.3</LangVersion>`
- .NET 5.0
- RimWorld 1.6+, 覆盖 C# DLL 与 XML Def 扩展

## 编码规范

- 强制中文优先
- 遵循Karpathy 四原则, 需求模糊时先问我，不猜测隐含意图
- C# 注释用中文，解释「为什么」而非「做什么」
- C# 命名空间: 与 MOD 名一致，PascalCase
- C# 空值防御: 链式访问用 `?.`，禁止裸访问 `pawn.skills.X`
- `packageId`: `作者.mod名` 小写，发布后不可改
- DefName: `Mod缩写_类别_名称`，禁止使用原生命名空间（`Gun_`/`Bullet_`）
- 翻译 Key: `Mod缩写_类别_名称`，禁止使用原生 Key
- Scribe Key: 带 MOD 缩写前缀，避免与其他 MOD 冲突
- 日志前缀 `[Mod缩写]`，异常用 `Log.ErrorOnce` 防重复，禁止静默吞异常

## UI 与窗口

- 设置窗口内容超出可见区域时必须用 `Widgets.BeginScrollView/EndScrollView` 包裹
- ScrollView 的 inner rect 必须从 `(0, 0)` 开始，禁止用屏幕坐标作起点
- inner rect 宽度比 outer 少 16f（预留给滚动条），高度按内容预估并留余量
- `Listing_Standard` 在 ScrollView 内绘制时直接 `Begin(innerRect)`
- 设置类（继承 `Mod`）需用 `static Vector2` 字段保存滚动位置
- 自定义 `Window` 子类必须重写 `InitialSize`
- 设置项超过 8 个时必须用双列布局
- 多个操作按钮必须并排放置，避免纵向堆叠挤压可见区
- 优先用 `CheckboxLabeled` 替代独立标签+控件
- 长文本用 `Text.CalcHeight` 计算实际高度自适应，避免截断

### 文字防换行（强制）

中文文字超宽时 `Widgets.Label` 默认换行，会导致单行内容撑成两行而显示不全。

- **徽章、标签、数值行**等单行显示场景，绘制前必须 `Text.WordWrap = false`，绘制后恢复
- 禁止用固定宽度（如 `60f`、`width*0.4f`）分配标签区——中文长度不可预测
- 标签宽度必须用 `Text.CalcSize(labelText).x + 留白` 动态计算
- 超宽文字截断优于换行：截断只丢尾部，换行会撑乱整个布局
- 完整信息放 `TooltipHandler.TipRegion`，徽章/标签本身只做概览显示
- 绘制方法模板：保存 `prevWrap` → 设 `false` → 绘制 → 恢复 `prevWrap`

## 静态资源加载

- **禁止**在普通类的静态字段初始化器中调用 `ContentFinder`/`DefDatabase` 查询
  - 静态字段初始化器在类型首次访问时执行，时机由 CLR 决定，可能在非主线程（DefDatabase 扫描、Harmony 反射、序列化）
  - `ContentFinder` 必须在主线程调用，跨线程会抛 `Tried to get a resource from a different thread`
- 加载纹理/资源的类必须标记 `[StaticConstructorOnStartup]`
  - RimWorld 会在主线程启动阶段（DefDatabase 加载后）主动触发静态构造，抢占其他线程
  - 静态字段初始化器会合并进编译器合成的静态构造函数，加特性即生效
  - ITab 子类、Window 子类、含 `ContentFinder<Texture2D>.Get` 的工具类均需标记
- 替代方案：`LongEventHandler.ExecuteWhenFinished` 延迟到主线程加载完成
- 纹理路径用相对路径（相对于 `Textures/` 目录），不含扩展名
- `ContentFinder<Texture2D>.Get(path, false)`：`reportFailure=false` 时未找到返回 null，调用方必须处理 null 回退

## Harmony 补丁

- Harmony ID: `作者.mod名`，整个 MOD 单一实例
- 优先 Postfix（零侵入），避免 Transpiler（极高风险）
- Prefix 拦截（`return false`）必须复现原方法副作用
- 多 MOD Patch 同一方法时用 `Priority.Last`
- 通过 `[StaticConstructorOnStartup]` 初始化，禁止静态构造中 PatchAll
- Pawn 运行时入口（`SpawnSetup` Postfix、`CompTick`）加 try-catch 异常隔离

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
- 日志在 `if (debugActive)` 后短路，用 `Func<string>` 延迟构造避免字符串分配
- 字符串比较用 `IndexOf(StringComparison.OrdinalIgnoreCase)` 替代 `ToUpperInvariant().Contains`

## 兼容性

- DLC API 调用前必须 `ModsConfig.XActive` 检查
- `DefDatabase.GetNamed` 后必须 null 检查
- ThingComp 注入: 运行时遍历 `DefDatabase` 添加，检查是否已存在
- 依赖 MOD 必须在 `About.xml` 声明 `modDependencies` 与 `loadAfter`

### Pawn 适配性过滤

- ThingComp 注入前必须按种族过滤：动物、机械族、昆虫、异常实体等不适用类别禁止注入
- Pawn 运行时入口（`SpawnSetup` Postfix、`CompTick`）必须再加一层适用性检查，覆盖旧存档已注入异常 Comp 的情况
- 兜底防御：`CompTick` 检测到不适用 Pawn 时静默 `parent.AllComps.Remove(this)` 自移除
- 仅 `race.Humanlike` 适合装备管理（含外星人 mod 的类人种族）

### TraitDef 安全查询

- 多 degree 特质的 defName 是单一的，degree 区分变体
- 禁止把 degree 的 label 当作 defName 查询
- 自定义 TraitDef 查询用 `GetNamed(defName, false)` 安全版本
- Tick 路径必须用静态字段缓存 TraitDef 查询结果
- 原生 DefOf 始终存在，无需 null 检查

## 存档安全

- `PostExposeData` 中调用 `Scribe_*`，每个字段必须有默认值
- Thing 引用用 `Scribe_References.Look`，禁止 `Scribe_Values` 存引用
- 数据格式变更保留向后兼容读取
- Scribe Key 改名时用双读兼容旧存档

## 本地化

- 必须同时提供 `ChineseSimplified` 与 `English` Keyed XML
- 禁止硬编码显示字符串，必须 `"Key".Translate()`
- 动态字符串用 `{0}` `{1}` 占位符
- `About.xml` 的 `description` 必须用中文撰写，可附英文摘要
- 面向玩家的字符串、README、Steam 创意工坊简介均以中文为第一语言
- 枚举值对应的翻译键在启动时校验完整性，缺失则警告

## 通用工作流

### 编译验证（强制）

- **每次**修改 C#/XML/Makefile 后，必须立即执行 `make check` 验证零警告零错误
- `make check` 通过 `-warnaserror` 把警告升级为错误，任何警告都会导致失败
- 失败情形：编译错误、任意警告、输出 DLL 缺失
- 不得跳过此步骤直接交付；如失败必须先修复再继续后续工作
- 检查命令：在项目根目录执行 `make check`
- 完整重建后检查：`make rebuild-check`
- 提交前必须 `make check` 通过，否则禁止视为"已完成"

### 命名空间规则

- 命名空间必须与文件夹结构匹配（IDE0130 规则）
- 跨命名空间引用必须显式 `using`，禁止依赖 IDE 自动补全

### 文档同步（通用原则）

- 修改玩家可见契约（评分公式、阈值、规则）必须同步更新 README
- 文档与代码不一致视为"未完成"，禁止提交
- 项目专属的同步清单见项目规则文件

### 提交规范

- 提交信息必须使用中文
- 提交信息必须包含变更类型（feat/fix/refactor/perf 等）
- 提交信息简洁明了，不超过一段落
- 提交前必须 `make check` 通过

## 发布检查

- `.csproj` 引用设 `<Private>false</Private>`，禁止打包 Assembly-CSharp.dll
- `About.xml` 完整: `supportedVersions`、`packageId`、`modDependencies`、`description`（中文）
- `dotnet build -c Release` 零错误零警告
- 无 DLC 环境测试启动，有 DLC 环境测试功能
