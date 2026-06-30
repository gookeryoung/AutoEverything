using System;

namespace AutoEverything.Core
{
    /// <summary>
    /// 评级前缀剥离工具：统一 CombatEvaluator 与 AESettings 的重复实现。
    /// 格式：档次名 + # + 原名（如 "S#王五"），支持多字母前缀 SS#/SSS#。
    /// 必须是合法 CombatTier 枚举名才剥离，避免误把玩家自定义 Nick 当评级前缀。
    ///
    /// 星标后缀：AutoMarkPawn 模块为 S+ 殖民者追加 "★" 后缀（如 "S#王五★"）。
    /// Strip 会同时剥离前缀与星标后缀，返回纯净名，保证自定义评级查询命中。
    /// </summary>
    public static class TierTagHelper
    {
        /// <summary>
        /// 高价值殖民者名字后缀标记（鲜艳红色星，由 AutoMarkPawn 模块追加）。
        /// 定义在此处避免 AESettings 与 PawnMarker 循环依赖。
        /// </summary>
        public const string StarMarker = "★";

        /// <summary>
        /// 剥离 Label/Nick 上的评级前缀与星标后缀。若无前缀/星标返回原值。
        /// 先剥离评级前缀，再剥离尾部星标，保证 GetPawnLookupName 返回纯净名。
        /// </summary>
        public static string Strip(string label)
        {
            if (string.IsNullOrEmpty(label)) return label;

            // 先剥离评级前缀
            int hashIdx = label.IndexOf('#');
            // hashIdx <= 0：无 # 或 # 在首位；hashIdx > 3：前缀超长（最长 SSS=3 字符）
            if (hashIdx > 0 && hashIdx <= 3)
            {
                string prefix = label.Substring(0, hashIdx);
                if (Enum.TryParse(prefix, out CombatTier _))
                {
                    label = label.Substring(hashIdx + 1);
                }
            }

            // 再剥离尾部星标
            return StripStar(label);
        }

        /// <summary>
        /// 剥离尾部星标后缀。若无星标返回原值。
        /// </summary>
        public static string StripStar(string label)
        {
            if (string.IsNullOrEmpty(label)) return label;
            return label.EndsWith(StarMarker, StringComparison.Ordinal)
                ? label.Substring(0, label.Length - StarMarker.Length)
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

        /// <summary>
        /// 检查 Label/Nick 是否带有星标后缀。
        /// </summary>
        public static bool HasStar(string label)
        {
            if (string.IsNullOrEmpty(label)) return false;
            return label.EndsWith(StarMarker, StringComparison.Ordinal);
        }
    }
}
