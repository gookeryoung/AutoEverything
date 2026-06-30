using System;

namespace AutoEverything.Core
{
    /// <summary>
    /// 评级前缀剥离工具：统一 CombatEvaluator 与 AESettings 的重复实现。
    /// 格式：档次名 + # + 原名（如 "S#王五"），支持多字母前缀 SS#/SSS#。
    /// 必须是合法 CombatTier 枚举名才剥离，避免误把玩家自定义 Nick 当评级前缀。
    /// </summary>
    public static class TierTagHelper
    {
        /// <summary>
        /// 剥离 Label/Nick 上的评级前缀。若无前缀返回原值。
        /// </summary>
        public static string Strip(string label)
        {
            if (string.IsNullOrEmpty(label)) return label;
            int hashIdx = label.IndexOf('#');
            // hashIdx <= 0：无 # 或 # 在首位；hashIdx > 3：前缀超长（最长 SSS=3 字符）
            if (hashIdx <= 0 || hashIdx > 3) return label;
            string prefix = label.Substring(0, hashIdx);
            return Enum.TryParse(prefix, out CombatTier _)
                ? label.Substring(hashIdx + 1)
                : label;
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
