using UnityEngine;
using Verse;

namespace AutoEverything.AutoMarkPawn
{
    /// <summary>
    /// 角色定位图标的纹理资源：加载外部 PNG 纹理（64x64 RGBA，白色形状+透明背景）。
    ///
    /// 设计原因：
    /// - 外部 PNG 图标比程序化生成的像素纹理视觉质量更高，符合 Useful Marks 风格
    /// - 白色纹理在绘制时由 GUI.color 染色，4 种图标共用同一套白色纹理（节省资源）
    /// - 项目已有完整的 Textures/UI/Icons/Role/ 目录结构，与现有 Role_*.png 保持一致命名约定
    ///
    /// 纹理文件（64x64 RGBA，白色形状+透明背景）：
    /// - <see cref="Tough"/>：Role_Tough.png——坚韧盾牌（带翼盾形，标识坚韧特质）
    /// - <see cref="Ranged"/>：Role_Ranged.png——弓箭（弓弧+弓弦+箭杆+箭头）
    /// - <see cref="Worker"/>：Role_Worker.png——扳手螺丝刀（交叉工具组合，标识工作倾向）
    /// - <see cref="Trader"/>：Role_Trader.png——钱袋子（扎绳+袋口+圆形袋身）
    /// - <see cref="Bond"/>：Role_Bond.png——实心爱心（心灵纽带伴侣标识）
    /// - <see cref="BondHatched"/>：Role_BondHatched.png——斜线剖面爱心（与实心组合区分配对）
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

        /// <summary>弓箭纹理：远程</summary>
        public static readonly Texture2D Ranged;

        /// <summary>扳手纹理：工人（原手工图标已并入）</summary>
        public static readonly Texture2D Worker;

        /// <summary>钱袋子纹理：贸易</summary>
        public static readonly Texture2D Trader;

        /// <summary>爱心纹理：心灵纽带伴侣（实心）</summary>
        public static readonly Texture2D Bond;

        /// <summary>爱心纹理：心灵纽带伴侣（斜线剖面，用于与实心组合区分配对）</summary>
        public static readonly Texture2D BondHatched;

        // 降级纹理尺寸常量
        private const int FallbackSize = 32;
        private const int FallbackCenter = FallbackSize / 2;

        static RoleIconTextures()
        {
            Tough = LoadOrFallback("UI/Icons/Role/Role_Tough", CreateToughShieldFallback);
            Ranged = LoadOrFallback("UI/Icons/Role/Role_Ranged", CreateBowArrowFallback);
            Worker = LoadOrFallback("UI/Icons/Role/Role_Worker", CreateWrenchFallback);
            Trader = LoadOrFallback("UI/Icons/Role/Role_Trader", CreateMoneyBagFallback);
            Bond = LoadOrFallback("UI/Icons/Role/Role_Bond", CreateHeartFallback);
            BondHatched = LoadOrFallback("UI/Icons/Role/Role_BondHatched", CreateHeartHatchedFallback);
        }

        /// <summary>
        /// 按 <see cref="RoleIconDef.RoleIconType"/> 取对应纹理。
        /// </summary>
        public static Texture2D Get(RoleIconDef.RoleIconType type)
        {
            switch (type)
            {
                case RoleIconDef.RoleIconType.Tough: return Tough;
                case RoleIconDef.RoleIconType.Ranged: return Ranged;
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

        /// <summary>
        /// Worker 降级纹理：扳手（垂直放置，开口朝上 + 圆形把手）。
        /// 简化为：顶部 U 形开口叉头 + 中间杆 + 底部圆形把手。
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

        /// <summary>
        /// Bond 降级纹理：爱心（经典心形隐函数 (x²+y²-1)³ - x²y³ ≤ 0 填充）。
        /// </summary>
        private static Texture2D CreateHeartFallback()
        {
            Color[] pixels = new Color[FallbackSize * FallbackSize];
            for (int y = 0; y < FallbackSize; y++)
            {
                for (int x = 0; x < FallbackSize; x++)
                {
                    // 映射到 [-1.15, 1.15] 并翻转 Y（纹理坐标向下为正，心形公式向上为正）
                    float nx = ((x + 0.5f) / FallbackSize * 2f - 1f) * 1.15f;
                    float ny = (1f - (y + 0.5f) / FallbackSize * 2f) * 1.15f;
                    float a = nx * nx + ny * ny - 1f;
                    bool filled = a * a * a - nx * nx * ny * ny * ny <= 0f;
                    pixels[y * FallbackSize + x] = filled ? Color.white : Color.clear;
                }
            }
            return CreateFallbackTexture(pixels);
        }

        /// <summary>
        /// BondHatched 降级纹理：带描边的斜线剖面爱心。
        /// 心形外轮廓 1.2 倍采样判定为实心描边，内部 45° 斜线填充（周期 10px、实线 6px），
        /// 描边提升小尺寸下的轮廓对比度。
        /// </summary>
        private static Texture2D CreateHeartHatchedFallback()
        {
            Color[] pixels = new Color[FallbackSize * FallbackSize];
            for (int y = 0; y < FallbackSize; y++)
            {
                for (int x = 0; x < FallbackSize; x++)
                {
                    bool outer = HeartContains(x, y, 1f);
                    bool inner = HeartContains(x, y, 1.2f);
                    bool stripe = (x + y) % 10 < 6;
                    bool filled = outer && (stripe || !inner);
                    pixels[y * FallbackSize + x] = filled ? Color.white : Color.clear;
                }
            }
            return CreateFallbackTexture(pixels);
        }

        /// <summary>心形隐函数判定：(x²+y²-1)³ - x²y³ ≤ 0 区域为心形内部；sampleScale 越大判定区域越小。</summary>
        private static bool HeartContains(int x, int y, float sampleScale)
        {
            float nx = ((x + 0.5f) / FallbackSize * 2f - 1f) * 1.15f * sampleScale;
            float ny = (1f - (y + 0.5f) / FallbackSize * 2f) * 1.15f * sampleScale;
            float a = nx * nx + ny * ny - 1f;
            return a * a * a - nx * nx * ny * ny * ny <= 0f;
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
