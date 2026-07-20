# RimWorld MOD 开发通用规则

## 技术栈

- C# 7.3（禁用 8.0+，`.csproj` 显式 `<LangVersion>7.3</LangVersion>`），.NET 5.0，RimWorld 1.6+

## 编码规范

- 强制中文优先；遵循 Karpathy 四原则：简单优于复杂、删除优于扩展、理解优于记忆、原型优于规划
- 需求模糊时先问我，不猜测隐含意图
- C# 注释用中文，解释「为什么」而非「做什么」
- 命名空间与文件夹匹配（IDE0130）：`Source/ModName/Folder/*` → `namespace ModName.Folder`
- 跨命名空间引用必须显式 `using`，禁止循环依赖
- 空值防御用 `?.`，禁止裸访问 `pawn.skills.X`
- `packageId`: `作者.mod名` 小写，发布后不可改
- DefName/翻译 Key/Scribe Key 带 MOD 缩写前缀，避免与原生/其他 MOD 冲突
- 日志前缀 `[Mod缩写]`，异常用 `Log.ErrorOnce` 防重复，禁止静默吞异常

## 代码组织

- 单一职责：一个类/文件只做一件事（参考 `PawnJobGuard` 只管 Job 守卫）
- 静态类模式：无状态分配器/评估器用 `static class` + 静态缓存字段
- 文件超 500 行考虑拆分；KISS 原则优先，能用 if-else 不用策略模式

## UI 与窗口

- 超出可见区必须用 `Widgets.BeginScrollView/EndScrollView` 包裹
- ScrollView 的 inner rect 从 `(0, 0)` 开始，宽度比 outer 少 16f（预留滚动条）
- `Listing_Standard` 在 ScrollView 内直接 `Begin(innerRect)`
- 设置类（继承 `Mod`）用 `static Vector2` 保存滚动位置；`Window` 子类重写 `InitialSize`
- 设置项超 8 个用双列布局；按钮并排放置；优先 `CheckboxLabeled`
- 长文本用 `Text.CalcHeight` 自适应，避免截断

### 文字防换行（强制）

中文超宽时 `Widgets.Label` 默认换行会撑乱布局：

- 单行场景（徽章/标签/数值行）绘制前 `Text.WordWrap = false`，绘制后恢复
- 标签宽度用 `Text.CalcSize(text).x + 留白` 动态计算，禁止固定宽度
- 完整信息放 `TooltipHandler.TipRegion`，标签本身只做概览
- 模板：保存 `prevWrap` → 设 `false` → 绘制 → 恢复 `prevWrap`

## 静态资源加载

- 禁止在普通类静态字段初始化器中调用 `ContentFinder`/`DefDatabase`（可能跨线程抛异常）
- 加载纹理/资源的类必须标记 `[StaticConstructorOnStartup]`（ITab/Window 子类、含 `ContentFinder` 的工具类均需标记）
- 替代方案：`LongEventHandler.ExecuteWhenFinished` 延迟到主线程加载完成
- 纹理路径相对于 `Textures/`，不含扩展名
- `ContentFinder<Texture2D>.Get(path, false)` 返回 null 时必须回退处理

## Harmony 补丁

- Harmony ID: `作者.mod名`，整个 MOD 单一实例
- 优先 Postfix（零侵入），避免 Transpiler（极高风险）
- Prefix 拦截（`return false`）必须复现原方法副作用
- 多 MOD Patch 同一方法时用 `Priority.Last`
- 通过 `[StaticConstructorOnStartup]` 初始化，禁止静态构造中 PatchAll
- Pawn 运行时入口（`SpawnSetup` Postfix、`CompTick`）加 try-catch 异常隔离

## XML Def

- 禁止修改游戏原始 XML，必须用 `Patch` 操作
- Patch 防御性：`PatchOperationConditional` 先检查再操作
- Xpath 用 `defName` 精确匹配，禁用顺序索引
- 继承用 `<ParentName>`，禁止修改原生抽象 Def

## 性能

- Tick 路径：禁止 LINQ、`new List<>()`、`OrderBy`
- 取模分散：`(TicksGame + thingIDNumber % interval) % interval != 0` 则 return
- 集合用静态缓存或实例字段复用；查询用 `GenClosest.ClosestThingReachable` 与 `ThingsInGroup`
- 缓存 TTL ≥ 2500 tick，状态变化时失效
- 日志在 `if (debugActive)` 后短路，用 `Func<string>` 延迟构造避免字符串分配
- 字符串比较用 `IndexOf(StringComparison.OrdinalIgnoreCase)` 替代 `ToUpperInvariant().Contains`

## 错误处理与日志

- Tick 路径必须 try-catch 隔离：单 Pawn 评估失败不影响其他 Pawn
- `Log.ErrorOnce(message, id)` 防重复，id 用 `thingIDNumber ^ salt`（每个错误点独立 salt，如 `GhoulErrorIdBase = 0xA100`）
- DLC API 必须 `ModsConfig.XActive` 检查 + try-catch 包装
- 禁止静默吞异常，至少 `Log.Error` 记录
- 错误恢复优先降级而非崩溃：纹理失败回退纯色块，DLC API 异常返回 false

## 兼容性

- DLC API 调用前 `ModsConfig.XActive` 检查；`DefDatabase.GetNamed` 后 null 检查
- ThingComp 注入按种族过滤，仅 `race.Humanlike` 适用（含外星人 mod 类人种族）
- `CompTick` 兜底：不适用 Pawn 静默 `parent.AllComps.Remove(this)` 自移除
- TraitDef 查询用 `GetNamed(defName, false)`；多 degree 特质 defName 单一，degree 区分变体，禁止把 degree label 当 defName
- Tick 路径用静态字段缓存 TraitDef 查询结果；原生 DefOf 始终存在无需 null 检查
- 依赖 MOD 在 `About.xml` 声明 `modDependencies` 与 `loadAfter`

## 存档安全

- `PostExposeData` 中每个字段必须有默认值
- Thing 引用用 `Scribe_References.Look`，禁止 `Scribe_Values` 存引用
- 数据格式变更保留向后兼容；Scribe Key 改名用双读兼容旧存档

## 本地化

- 必须同时提供 `ChineseSimplified` 与 `English` Keyed XML
- 禁止硬编码显示字符串，必须 `"Key".Translate()`；动态字符串用 `{0}` `{1}` 占位符
- `About.xml` 的 `description` 以中文为主语言
- 枚举值对应的翻译键在启动时校验完整性，缺失则警告

## 工作流与验证

### 编译验证（强制）

- 每次修改 C#/XML/Makefile 后立即 `make check`（`-warnaserror` 把警告升级为错误）
- 大改动后用 `make rebuild-check` 完整重建
- 失败情形：编译错误、任意警告、输出 DLL 缺失
- 提交前必须 `make check` 通过，否则禁止视为"已完成"

### 文档同步

- 修改玩家可见契约（评分公式、阈值、规则）必须同步更新 README
- 文档与代码不一致视为"未完成"，禁止提交
- 项目专属的同步清单见项目规则文件

### 提交规范

- 提交信息使用中文，包含变更类型（feat/fix/refactor/perf 等），简洁明了
- 提交前必须 `make check` 通过
- **提交前必须审视是否遵循 Karpathy 四原则**：
  - 简单优于复杂（Simple > Complex）：是否有更简单的实现？
  - 删除优于扩展（Delete > Extend）：是否删除了无用代码而非扩展？
  - 理解优于记忆（Understand > Memorize）：代码是否易于理解？
  - 原型优于规划（Prototype > Plan）：是否先验证再优化？

### 游戏内验证

- 无 DLC / 有 DLC 环境均启动无报错
- 旧存档加载不丢失数据；新建殖民地功能正常
- 边界用例：空地图、单殖民者、全奴隶/全食尸鬼、全征召状态

## 发布检查

- `.csproj` 引用设 `<Private>false</Private>`，禁止打包 Assembly-CSharp.dll
- `About.xml` 完整：`supportedVersions`、`packageId`、`modDependencies`、`description`（中文）
- `dotnet build -c Release` 零错误零警告
