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
    /// - <see cref="Frontline"/>：盾牌（上宽下尖，经典盾形轮廓）——前排
    /// - <see cref="Ranged"/>：弓箭（左侧弓弧 + 水平箭杆 + 三角箭头）——远程
    /// - <see cref="Crafter"/>：锤子铁砧（上方锤子 + 下方铁砧）——手工
    /// - <see cref="Trader"/>：钱袋子（顶部扎口 + 圆形袋身）——贸易
    ///
    /// 4 种形状视觉差异明显，玩家通过形状 + 颜色双重区分角色定位。
    /// </summary>
    [StaticConstructorOnStartup]
    public static class RoleIconTextures
    {
        /// <summary>盾牌纹理：前排</summary>
        public static readonly Texture2D Frontline;

        /// <summary>弓箭纹理：远程</summary>
        public static readonly Texture2D Ranged;

        /// <summary>锤子铁砧纹理：手工</summary>
        public static readonly Texture2D Crafter;

        /// <summary>钱袋子纹理：贸易</summary>
        public static readonly Texture2D Trader;

        // 纹理尺寸：32x32 像素，殖民者栏头像约 48x48，图标占约 1/3 视觉空间
        private const int Size = 32;

        // 中心点（Size/2）
        private const int Center = Size / 2;

        static RoleIconTextures()
        {
            Frontline = CreateTexture(IsShieldFilled);
            Ranged = CreateTexture(IsBowArrowFilled);
            Crafter = CreateTexture(IsHammerAnvilFilled);
            Trader = CreateTexture(IsMoneyBagFilled);
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
        /// 盾牌：经典盾形轮廓（上宽下尖）。
        /// 顶部 y=4-7 水平边宽 20，中部 y=8-20 斜边收窄（宽 20→8），底部 y=21-28 尖角（宽 8→0）。
        /// 用整数乘法比较避免浮点除法精度损失。
        /// </summary>
        private static bool IsShieldFilled(int x, int y)
        {
            int dx = x < Center ? Center - x : x - Center;
            if (y < 4 || y > 28) return false;

            // 顶部水平边 y=4-7：宽 20（dx<=10）
            if (y <= 7) return dx <= 10;

            // 中部斜边 y=8-20：从宽 20（dx<=10）收到宽 8（dx<=4）
            // 等价 limit = 10 - (y-8)*0.5，用 dx*2 <= 20-(y-8) 避免除法
            if (y <= 20) return dx * 2 <= 20 - (y - 8);

            // 底部尖角 y=21-28：从宽 8（dx<=4）收到宽 0（dx<=0）
            // 等价 limit = (28-y)*4/7，用 dx*7 <= (28-y)*4 避免除法
            return dx * 7 <= (28 - y) * 4;
        }

        /// <summary>
        /// 弓箭：左侧弓弧 + 弓弦 + 水平箭杆 + 右侧三角箭头。
        /// 弓弧：中心 (10, 16)，半径 8，厚度 2（distSq 在 49-81 之间）。
        ///   左半圆从 (10, 8) 经 (2, 16) 到 (10, 24)。
        /// 弓弦：垂直线 x=10, y=8-24（连接弓两端，箭杆从其中部穿出）。
        /// 箭杆：y=15-17 中央 3 像素宽，x=10-26。
        /// 箭头：x=22-28 三角形朝右，dy ≤ 28-x。
        /// </summary>
        private static bool IsBowArrowFilled(int x, int y)
        {
            // 箭杆：y=15-17 中央，x=10-26
            if (y >= 15 && y <= 17 && x >= 10 && x <= 26) return true;

            // 箭头：x=22-28 三角形朝右
            // y=16 中心，x=28 尖端，x=22 底边宽 12
            if (x >= 22 && x <= 28)
            {
                int dy = y < 16 ? 16 - y : y - 16;
                if (dy <= 28 - x) return true;
            }

            // 弓弦：垂直线 x=10, y=8-24（连接弓两端）
            if (x == 10 && y >= 8 && y <= 24) return true;

            // 弓弧：左侧半圆，中心 (10, 16)，半径 8，厚度 2（distSq 在 49-81 之间）
            // 厚度 2 + 弓弦组合，弓形轮廓清晰可辨
            if (x >= 2 && x <= 10 && y >= 8 && y <= 24)
            {
                int cdx = x - 10;
                int cdy = y - 16;
                int distSq = cdx * cdx + cdy * cdy;
                if (distSq >= 49 && distSq <= 81) return true;
            }

            return false;
        }

        /// <summary>
        /// 锤子铁砧：上方锤子（锤头 + 锤柄）+ 下方铁砧（宽顶 + 收腰 + 底座）。
        /// 锤头 y=3-7 宽 16；锤柄 y=7-17 宽 2；
        /// 铁砧顶 y=18-21 宽 20；铁砧腰 y=22-24 宽 10；铁砧底座 y=25-28 宽 14。
        /// </summary>
        private static bool IsHammerAnvilFilled(int x, int y)
        {
            int dx = x < Center ? Center - x : x - Center;

            // 锤头：y=3-7 宽 16
            if (y >= 3 && y <= 7 && dx <= 8) return true;

            // 锤柄：y=7-17 宽 2
            if (y >= 7 && y <= 17 && dx <= 1) return true;

            // 铁砧顶部宽平台：y=18-21 宽 20
            if (y >= 18 && y <= 21 && dx <= 10) return true;

            // 铁砧收窄腰部：y=22-24 宽 10
            if (y >= 22 && y <= 24 && dx <= 5) return true;

            // 铁砧底座：y=25-28 宽 14
            if (y >= 25 && y <= 28 && dx <= 7) return true;

            return false;
        }

        /// <summary>
        /// 钱袋子：顶部扎口（小三角）+ 袋口收窄 + 圆形袋身。
        /// 扎口 y=2-4 小三角（绳扎处）；袋口 y=5-8 宽 6；
        /// 袋身 y=9-26 圆形，圆心 (16, 17)，半径 10。
        /// </summary>
        private static bool IsMoneyBagFilled(int x, int y)
        {
            int dx = x < Center ? Center - x : x - Center;

            // 顶部扎口小三角：y=2-4（袋口扎绳处）
            // y=2: dx<=4, y=4: dx<=0
            if (y >= 2 && y <= 4) return dx <= (4 - y) * 2;

            // 袋口收窄：y=5-8 宽 6
            if (y >= 5 && y <= 8 && dx <= 3) return true;

            // 中部圆形袋身：y=9-26, 圆心 (16, 17), 半径 10
            if (y >= 9 && y <= 26)
            {
                int dy = y - 17;
                if (dx * dx + dy * dy <= 100) return true;
            }

            return false;
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
