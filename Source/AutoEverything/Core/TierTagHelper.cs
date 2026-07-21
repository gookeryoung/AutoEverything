using System;

namespace AutoEverything.Core
{
    /// <summary>
    /// 评级前缀剥离工具：统一 CombatEvaluator 与 AESettings 的重复实现。
    /// 格式：档次名 + # + 原名（如 "S#王五"），支持多字母前缀 SS#/SSS#。
    /// 必须是合法 CombatTier 枚举名才剥离，避免误把玩家自定义 Nick 当评级前缀。
    ///
    /// 注：早期版本曾在此处定义 StarMarker "★" 字符常量供 HarmonyPatches 与 PawnMarker 共享，
    /// v3 演进后殖民者栏改为角色定位图标（RoleIconTextures 程序化生成纹理），不再使用 ★ 字符，
    /// 该常量已删除。
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
