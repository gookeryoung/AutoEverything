# 迭代 16：角色定位图标升级为外部 PNG 资源

## 需求清单

- [x] 使用多模态绘制生成高质量图标素材，替代程序化生成的像素纹理
- [x] 风格参考 Useful Marks（扁平化、粗轮廓、单色填充、高对比度剪影）
- [x] 图标尺寸与项目现有 Role_*.png 保持一致（64×64 RGBA 带透明通道）
- [x] 保留程序化生成作为降级方案（PNG 缺失时自动回退）

来源：用户反馈「本项目需要调用多模态绘制更好的图片素材，风格参考 Useful Marks」。

## 迭代目标

将角色定位图标从程序化生成的 32×32 像素纹理升级为 64×64 外部 PNG 资源，提升视觉质量，符合 Useful Marks 风格，同时保留降级策略确保稳定性。

## 改动文件清单

| 文件 | 改动 |
|------|------|
| `Textures/UI/Icons/Role/Role_Frontline.png` | 新增：盾牌图标（64×64 RGBA，白色经典盾形+透明背景） |
| `Textures/UI/Icons/Role/Role_Ranged.png` | 新增：弓箭图标（64×64 RGBA，弓弧+弓弦+箭杆+箭头） |
| `Textures/UI/Icons/Role/Role_Crafter.png` | 新增：锤子铁砧图标（64×64 RGBA，锤头+锤柄+铁砧） |
| `Textures/UI/Icons/Role/Role_Trader.png` | 新增：钱袋图标（64×64 RGBA，扎绳+袋口+圆形袋身） |
| `Source/AutoEverything/AutoMarkPawn/RoleIconTextures.cs` | 重写：优先加载外部 PNG 纹理，失败回退到程序化生成 |
| `README.md` | 更新：图标描述改为外部 PNG + 降级策略说明 |

## 关键决策与依据

### 1. 图标尺寸 64×64 像素

**决策**：新图标尺寸与项目现有 `Role_*.png` 保持一致（64×64 RGBA）。

**依据**：
- 项目现有 `Role_Brawler.png`、`Role_Shooter.png` 等均为 64×64 RGBA 带透明通道
- 64×64 在殖民者栏显示 16×16 时缩放 4 倍，细节保留更好
- 与 Useful Marks 图标尺寸一致（社区标准）

### 2. 白色纹理 + GUI.color 染色

**决策**：所有图标为白色形状 + 透明背景，绘制时通过 `GUI.color` 染色为目标颜色。

**依据**：
- 4 种图标共用同一套 PNG（战斗橙/工作绿/交易粉），节省资源
- 与现有代码逻辑一致（iter-14/15 的 `GUI.color` 染色机制）
- Useful Marks 风格核心是单色填充 + 粗轮廓

### 3. 保留程序化降级

**决策**：若 `ContentFinder<Texture2D>.Get` 返回 null，自动回退到 32×32 像素程序化纹理。

**依据**：
- 确保图标文件缺失时 MOD 仍能正常运行（防御性编程）
- 日志 `Log.WarningOnce` 记录缺失路径，便于排查
- 降级纹理与 iter-15 的形状一致，功能不受影响

### 4. FilterMode.Point 保持像素风

**决策**：加载的外部 PNG 纹理设置 `filterMode = FilterMode.Point`。

**依据**：
- RimWorld UI 整体为像素风，Point 模式保持风格一致
- 64×64 缩放为 16×16 时，Point 模式比 Bilinear 更清晰

## 代码实现情况

### 加载策略核心逻辑

```csharp
private static Texture2D LoadOrFallback(string path, System.Func<Texture2D> fallbackCreator)
{
    Texture2D tex = ContentFinder<Texture2D>.Get(path, false);
    if (tex != null)
    {
        tex.filterMode = FilterMode.Point;
        return tex;
    }
    Log.WarningOnce("[AutoEverything] 角色定位图标加载失败: " + path + ", 使用降级纹理",
        path.GetHashCode() ^ 0xB100);
    return fallbackCreator();
}
```

### 静态构造函数

```csharp
static RoleIconTextures()
{
    Frontline = LoadOrFallback("UI/Icons/Role/Role_Frontline", CreateShieldFallback);
    Ranged = LoadOrFallback("UI/Icons/Role/Role_Ranged", CreateBowArrowFallback);
    Crafter = LoadOrFallback("UI/Icons/Role/Role_Crafter", CreateHammerAnvilFallback);
    Trader = LoadOrFallback("UI/Icons/Role/Role_Trader", CreateMoneyBagFallback);
}
```

## 测试验证结果

### 编译验证

```
$ make check
AutoEverything net472 已成功 (1.1 秒) → Assemblies\AutoEverything.dll
[check] PASS: No errors (0 警告, 0 错误)
```

### 单元测试

```
$ make test
All tests passed. (370 个测试全部通过)
```

### 图标文件验证

```
Role_Frontline.png    64 x 64 | Mode: RGBA | HasAlpha: True
Role_Ranged.png       64 x 64 | Mode: RGBA | HasAlpha: True
Role_Crafter.png      64 x 64 | Mode: RGBA | HasAlpha: True
Role_Trader.png       64 x 64 | Mode: RGBA | HasAlpha: True
```

## 遗留事项

- 用户需进行游戏内验证：4 种 PNG 图标在殖民者栏右上角正确显示
- 若图标视觉效果仍不理想，可使用专业绘图工具（如 Aseprite、Paint.NET）进一步优化 PNG

## 下一轮计划

无。本次迭代完成图标资源升级，等待用户游戏内验证反馈。
