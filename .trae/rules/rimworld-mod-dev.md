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
