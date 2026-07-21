using UnityEngine;
using Verse;

namespace AutoEverything.AutoMarkPawn
{
    /// <summary>
    /// 角色定位图标的纹理资源：程序化生成 4 个 32x32 RGBA 纹理。
    ///
    /// 设计原因：
    /// - RimWorld 默认字体（Arial）对 Unicode 符号（⛨ ⚒ 等几何符号）支持有限，渲染可能缺失或显示方框
    /// - 程序化生成避免依赖外部 PNG 资源，MOD 单文件分发更简洁，且纹理颜色由 GUI.color 在绘制时染色
    /// - 纹理在主线程 [StaticConstructorOnStartup] 时机创建，避免跨线程访问 Unity API
    ///
    /// 纹理形状（白色像素 + 透明背景，绘制时用 GUI.color 染色）：
    /// - <see cref="Frontline"/>：盾形（上窄下宽，下端圆角）——前排
    /// - <see cref="Ranged"/>：箭头（垂直箭杆 + 三角形箭头）——远程
    /// - <see cref="Crafter"/>：T 字形（横向锤头 + 垂直锤柄）——手工
    /// - <see cref="Trader"/>：实心圆——贸易（钱袋简化为圆形，颜色粉红区分）
    ///
    /// 4 种形状视觉差异明显，玩家通过形状 + 颜色双重区分角色定位。
    /// </summary>
    [StaticConstructorOnStartup]
    public static class RoleIconTextures
    {
        /// <summary>盾形纹理：前排</summary>
        public static readonly Texture2D Frontline;

        /// <summary>箭头纹理：远程</summary>
        public static readonly Texture2D Ranged;

        /// <summary>T 字形纹理：手工</summary>
        public static readonly Texture2D Crafter;

        /// <summary>实心圆纹理：贸易</summary>
        public static readonly Texture2D Trader;

        // 纹理尺寸：32x32 像素，殖民者栏头像约 48x48，图标占约 1/3 视觉空间
        private const int Size = 32;

        // 中心点（Size/2）
        private const int Center = Size / 2;

        static RoleIconTextures()
        {
            Frontline = CreateTexture(IsShieldFilled);
            Ranged = CreateTexture(IsArrowFilled);
            Crafter = CreateTexture(IsHammerFilled);
            Trader = CreateTexture(IsCoinFilled);
        }

        /// <summary>
        /// 按 <see cref="RoleIconDef.RoleIconType"/> 取对应纹理。
        /// </summary>
        public static Texture2D Get(RoleIconDef.RoleIconType type)
        {
            switch (type)
            {
                case RoleIconDef.RoleIconType.Frontline: return Frontline;
                case RoleIconDef.RoleIconType.Ranged: return Ranged;
                case RoleIconDef.RoleIconType.Crafter: return Crafter;
                default: return Trader;
            }
        }

        // ── 形状判定函数：返回 (x, y) 坐标是否为实心像素 ──

        /// <summary>
        /// 盾形：上窄下宽，下端圆角。
        /// 上 1/3 宽 14，中 1/3 宽 22，下 1/3 椭圆圆角。
        /// </summary>
        private static bool IsShieldFilled(int x, int y)
        {
            int dx = x < Center ? Center - x : x - Center;
            // 上部窄矩形
            if (y < 10) return dx <= 7;
            // 中部宽矩形
            if (y < 22) return dx <= 11;
            // 下部圆角：椭圆方程
            int dy = y - 22;
            return (dx * dx) + (dy * dy * 2) <= 100;
        }

        /// <summary>
        /// 箭头形：垂直箭杆 + 三角形箭头。
        /// 箭杆 4-26 行的中央 2 像素宽，箭头 2-10 行的三角形。
        /// </summary>
        private static bool IsArrowFilled(int x, int y)
        {
            int dx = x < Center ? Center - x : x - Center;
            // 箭杆：y=6-26 的中央 2 像素宽
            if (y >= 6 && y <= 26 && dx <= 1) return true;
            // 箭头：y=2-10 的三角形（y=2 宽 8，y=10 宽 0）
            if (y >= 2 && y <= 10)
            {
                int arrowWidth = 10 - y;
                return dx <= arrowWidth;
            }
            return false;
        }

        /// <summary>
        /// T 字形（锤子）：上 1/3 横条（锤头）+ 中间垂直条（锤柄）。
        /// 锤头 y=4-12 宽 24，锤柄 y=12-28 宽 2。
        /// </summary>
        private static bool IsHammerFilled(int x, int y)
        {
            int dx = x < Center ? Center - x : x - Center;
            // 锤头：y=4-12 的宽 24 横条
            if (y >= 4 && y <= 12 && dx <= 11) return true;
            // 锤柄：y=12-28 的中央 2 像素宽
            if (y >= 12 && y <= 28 && dx <= 1) return true;
            return false;
        }

        /// <summary>
        /// 实心圆：半径 14 的圆，中心 (16, 16)。
        /// 钱袋简化为圆形，颜色粉红区分。
        /// </summary>
        private static bool IsCoinFilled(int x, int y)
        {
            int dx = x < Center ? Center - x : x - Center;
            int dy = y < Center ? Center - y : y - Center;
            return (dx * dx) + (dy * dy) <= 14 * 14;
        }

        /// <summary>
        /// 通用纹理生成：用形状判定函数填充白色像素，未填充为透明。
        /// 白色纹理在绘制时由 GUI.color 染色为目标颜色。
        /// filterMode=Point 保持像素风，避免 UI 缩放模糊。
        /// </summary>
        private static Texture2D CreateTexture(System.Func<int, int, bool> fillPredicate)
        {
            Color[] pixels = new Color[Size * Size];
            Color filled = Color.white;
            Color clear = Color.clear;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    pixels[y * Size + x] = fillPredicate(x, y) ? filled : clear;
                }
            }
            Texture2D tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.SetPixels(pixels);
            tex.Apply();
            tex.filterMode = FilterMode.Point;
            return tex;
        }
    }
}
