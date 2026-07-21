using System;

namespace AutoEverything.Core
{
    /// <summary>
    /// 评级前缀剥离工具：统一 CombatEvaluator 与 AESettings 的重复实现。
    /// 格式：档次名 + # + 原名（如 "S#王五"），支持多字母前缀 SS#/SSS#。
    /// 必须是合法 CombatTier 枚举名才剥离，避免误把玩家自定义 Nick 当评级前缀。
    ///
    /// 星标字符：AutoMarkPawn 模块在殖民者栏固定位置绘制 "★" 图标时复用此常量。
    /// 不再修改任何 Pawn 的 Nick，星标纯前端绘制（Harmony Postfix）。
    /// </summary>
    public static class TierTagHelper
    {
        /// <summary>
        /// 高价值星标字符（由 AutoMarkPawn 模块在殖民者栏 Rect 右上角绘制）。
        /// 定义在此处供 HarmonyPatches 与 PawnMarker 共享，避免重复定义。
        /// </summary>
        public const string StarMarker = "★";

        /// <summary>
        /// 剥离 Label/Nick 上的评级前缀。若无前缀返回原值。
        /// 仅剥离前缀，不处理星标（星标不再写入 Nick）。
        /// </summary>
        public static string Strip(string label)
        {
            if (string.IsNullOrEmpty(label)) return label;

            int hashIdx = label.IndexOf('#');
            // hashIdx <= 0：无 # 或 # 在首位；hashIdx > 3：前缀超长（最长 SSS=3 字符）
            if (hashIdx > 0 && hashIdx <= 3)
            {
                string prefix = label.Substring(0, hashIdx);
                if (Enum.TryParse(prefix, out CombatTier _))
                {
                    return label.Substring(hashIdx + 1);
                }
            }
            return label;
        }

        /// <summary>
        /// 检查 Label/Nick 是否带有合法评级前缀。
        /// </summary>
        public static bool HasPrefix(string label)
        {
            if (string.IsNullOrEmpty(label)) return false;
            int hashIdx = label.IndexOf('#');
            if (hashIdx <= 0 || hashIdx > 3) return false;
            string prefix = label.Substring(0, hashIdx);
            return Enum.TryParse(prefix, out CombatTier _);
        }
    }
}
