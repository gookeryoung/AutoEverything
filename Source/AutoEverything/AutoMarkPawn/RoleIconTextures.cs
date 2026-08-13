using UnityEngine;
using Verse;

namespace AutoEverything.AutoMarkPawn
{
    /// <summary>
    /// 角色定位图标的纹理资源：加载外部 PNG 纹理（64x64 RGBA，白色形状+透明背景）。
    ///
    /// 设计原因：
    /// - 外部 PNG 图标比程序化生成的像素纹理视觉质量更高，符合 Useful Marks 风格
    /// - 白色纹理在绘制时由 GUI.color 染色，6 种图标共用同一套白色纹理（节省资源）
    /// - 项目已有完整的 Textures/UI/Icons/Role/ 目录结构，与现有 Role_*.png 保持一致命名约定
    ///
    /// 纹理文件（64x64 RGBA，白色形状+透明背景）：
    /// - <see cref="Tough"/>：Role_Tough.png——坚韧盾牌（带翼盾形，标识坚韧特质）
    /// - <see cref="Frontline"/>：Role_Frontline.png——盾牌（上宽下尖的经典盾形）
    /// - <see cref="Ranged"/>：Role_Ranged.png——弓箭（弓弧+弓弦+箭杆+箭头）
    /// - <see cref="Crafter"/>：Role_Crafter.png——锤子铁砧（锤头+锤柄+铁砧平台）
    /// - <see cref="Worker"/>：Role_Worker.png——扳手螺丝刀（交叉工具组合，标识工作倾向）
    /// - <see cref="Trader"/>：Role_Trader.png——钱袋子（扎绳+袋口+圆形袋身）
    ///
    /// 降级策略：
    /// - 若外部 PNG 加载失败（ContentFinder 返回 null），回退到程序化生成的 32x32 像素纹理
    /// - 确保即使图标文件缺失，MOD 仍能正常运行
    /// </summary>
    [StaticConstructorOnStartup]
    public static class RoleIconTextures
    {
        /// <summary>坚韧盾牌纹理：坚韧</summary>
        public static readonly Texture2D Tough;

        /// <summary>盾牌纹理：前排</summary>
        public static readonly Texture2D Frontline;

        /// <summary>弓箭纹理：远程</summary>
        public static readonly Texture2D Ranged;

        /// <summary>锤子铁砧纹理：手工</summary>
        public static readonly Texture2D Crafter;

        /// <summary>扳手纹理：工人</summary>
        public static readonly Texture2D Worker;

        /// <summary>钱袋子纹理：贸易</summary>
        public static readonly Texture2D Trader;

        // 纹理尺寸常量
        private const int ExternalSize = 64;
        private const int FallbackSize = 32;
        private const int FallbackCenter = FallbackSize / 2;

        static RoleIconTextures()
        {
            Tough = LoadOrFallback("UI/Icons/Role/Role_Tough", CreateToughShieldFallback);
            Frontline = LoadOrFallback("UI/Icons/Role/Role_Frontline", CreateShieldFallback);
            Ranged = LoadOrFallback("UI/Icons/Role/Role_Ranged", CreateBowArrowFallback);
            Crafter = LoadOrFallback("UI/Icons/Role/Role_Crafter", CreateHammerAnvilFallback);
            Worker = LoadOrFallback("UI/Icons/Role/Role_Worker", CreateWrenchFallback);
            Trader = LoadOrFallback("UI/Icons/Role/Role_Trader", CreateMoneyBagFallback);
        }

        /// <summary>
        /// 按 <see cref="RoleIconDef.RoleIconType"/> 取对应纹理。
        /// </summary>
        public static Texture2D Get(RoleIconDef.RoleIconType type)
        {
            switch (type)
            {
                case RoleIconDef.RoleIconType.Tough: return Tough;
                case RoleIconDef.RoleIconType.Frontline: return Frontline;
                case RoleIconDef.RoleIconType.Ranged: return Ranged;
                case RoleIconDef.RoleIconType.Crafter: return Crafter;
                case RoleIconDef.RoleIconType.Worker: return Worker;
                default: return Trader;
            }
        }

        /// <summary>
        /// 优先加载外部 PNG 纹理，失败则回退到程序化生成。
        /// ContentFinder 路径相对于 Textures/ 目录，不含扩展名。
        /// </summary>
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

        // ── 降级纹理生成（32x32 像素，仅在 PNG 缺失时使用） ──

        /// <summary>
        /// Tough 降级纹理：带翼盾形（与 Frontline 盾形区分，顶部多两个翼状装饰）。
        /// 简化为：顶部两翼 + 中央盾牌主体。
        /// </summary>
        private static Texture2D CreateToughShieldFallback()
        {
            Color[] pixels = new Color[FallbackSize * FallbackSize];
            for (int y = 0; y < FallbackSize; y++)
            {
                for (int x = 0; x < FallbackSize; x++)
                {
                    int dx = x < FallbackCenter ? FallbackCenter - x : x - FallbackCenter;
                    bool filled = false;
                    // 顶部两翼（y=2~5，x 在 4~10 与 22~28 范围内）
                    if (y >= 2 && y <= 5)
                    {
                        if (x >= 4 && x <= 10) filled = true;
                        else if (x >= 22 && x <= 28) filled = true;
                    }
                    // 中央盾牌主体（y=6~28，上宽下尖）
                    else if (y >= 6 && y <= 28)
                    {
                        if (y <= 9) filled = dx <= 11;
                        else if (y <= 20) filled = dx * 2 <= 22 - (y - 10);
                        else filled = dx * 7 <= (28 - y) * 4;
                    }
                    pixels[y * FallbackSize + x] = filled ? Color.white : Color.clear;
                }
            }
            return CreateFallbackTexture(pixels);
        }

        private static Texture2D CreateShieldFallback()
        {
            Color[] pixels = new Color[FallbackSize * FallbackSize];
            for (int y = 0; y < FallbackSize; y++)
            {
                for (int x = 0; x < FallbackSize; x++)
                {
                    int dx = x < FallbackCenter ? FallbackCenter - x : x - FallbackCenter;
                    bool filled = false;
                    if (y >= 4 && y <= 28)
                    {
                        if (y <= 7) filled = dx <= 10;
                        else if (y <= 20) filled = dx * 2 <= 20 - (y - 8);
                        else filled = dx * 7 <= (28 - y) * 4;
                    }
                    pixels[y * FallbackSize + x] = filled ? Color.white : Color.clear;
                }
            }
            return CreateFallbackTexture(pixels);
        }

        private static Texture2D CreateBowArrowFallback()
        {
            Color[] pixels = new Color[FallbackSize * FallbackSize];
            for (int y = 0; y < FallbackSize; y++)
            {
                for (int x = 0; x < FallbackSize; x++)
                {
                    bool filled = false;
                    if (y >= 15 && y <= 17 && x >= 10 && x <= 26) filled = true;
                    else if (x >= 22 && x <= 28)
                    {
                        int dy = y < 16 ? 16 - y : y - 16;
                        if (dy <= 28 - x) filled = true;
                    }
                    else if (x == 10 && y >= 8 && y <= 24) filled = true;
                    else if (x >= 2 && x <= 10 && y >= 8 && y <= 24)
                    {
                        int cdx = x - 10, cdy = y - 16;
                        int distSq = cdx * cdx + cdy * cdy;
                        if (distSq >= 49 && distSq <= 81) filled = true;
                    }
                    pixels[y * FallbackSize + x] = filled ? Color.white : Color.clear;
                }
            }
            return CreateFallbackTexture(pixels);
        }

        private static Texture2D CreateHammerAnvilFallback()
        {
            Color[] pixels = new Color[FallbackSize * FallbackSize];
            for (int y = 0; y < FallbackSize; y++)
            {
                for (int x = 0; x < FallbackSize; x++)
                {
                    int dx = x < FallbackCenter ? FallbackCenter - x : x - FallbackCenter;
                    bool filled = false;
                    if (y >= 3 && y <= 7 && dx <= 8) filled = true;
                    else if (y >= 7 && y <= 17 && dx <= 1) filled = true;
                    else if (y >= 18 && y <= 21 && dx <= 10) filled = true;
                    else if (y >= 22 && y <= 24 && dx <= 5) filled = true;
                    else if (y >= 25 && y <= 28 && dx <= 7) filled = true;
                    pixels[y * FallbackSize + x] = filled ? Color.white : Color.clear;
                }
            }
            return CreateFallbackTexture(pixels);
        }

        /// <summary>
        /// Worker 降级纹理：扳手（垂直放置，开口朝上 + 圆形把手）。
        /// 简化为：顶部 U 形开口叉头 + 中间杆 + 底部圆形把手。
        /// 与 Crafter 锤子铁砧区分：扳手有开口叉头，铁砧有平台底座。
        /// </summary>
        private static Texture2D CreateWrenchFallback()
        {
            Color[] pixels = new Color[FallbackSize * FallbackSize];
            for (int y = 0; y < FallbackSize; y++)
            {
                for (int x = 0; x < FallbackSize; x++)
                {
                    int dx = x < FallbackCenter ? FallbackCenter - x : x - FallbackCenter;
                    bool filled = false;
                    // 顶部 U 形开口叉头：左右两叉（dx=3~5），中间开口（dx<3）
                    if (y >= 2 && y <= 5)
                    {
                        if (dx >= 3 && dx <= 5) filled = true;
                    }
                    // 叉头底部连接：dx<=5 实心横条，闭合 U 形
                    else if (y >= 6 && y <= 9)
                    {
                        if (dx <= 5) filled = true;
                    }
                    // 中间杆：dx<=2 的窄矩形
                    else if (y >= 10 && y <= 21)
                    {
                        if (dx <= 2) filled = true;
                    }
                    // 底部圆形把手：以 (16, 25) 为圆心、半径约 3 的圆
                    else if (y >= 22 && y <= 28)
                    {
                        int dy = y - 25;
                        if (dx * dx + dy * dy <= 10) filled = true;
                    }
                    pixels[y * FallbackSize + x] = filled ? Color.white : Color.clear;
                }
            }
            return CreateFallbackTexture(pixels);
        }

        private static Texture2D CreateMoneyBagFallback()
        {
            Color[] pixels = new Color[FallbackSize * FallbackSize];
            for (int y = 0; y < FallbackSize; y++)
            {
                for (int x = 0; x < FallbackSize; x++)
                {
                    int dx = x < FallbackCenter ? FallbackCenter - x : x - FallbackCenter;
                    bool filled = false;
                    if (y >= 2 && y <= 4) filled = dx <= (4 - y) * 2;
                    else if (y >= 5 && y <= 8 && dx <= 3) filled = true;
                    else if (y >= 9 && y <= 26)
                    {
                        int dy = y - 17;
                        if (dx * dx + dy * dy <= 100) filled = true;
                    }
                    pixels[y * FallbackSize + x] = filled ? Color.white : Color.clear;
                }
            }
            return CreateFallbackTexture(pixels);
        }

        private static Texture2D CreateFallbackTexture(Color[] pixels)
        {
            Texture2D tex = new Texture2D(FallbackSize, FallbackSize, TextureFormat.RGBA32, false);
            tex.SetPixels(pixels);
            tex.Apply();
            tex.filterMode = FilterMode.Point;
            return tex;
        }
    }
}
