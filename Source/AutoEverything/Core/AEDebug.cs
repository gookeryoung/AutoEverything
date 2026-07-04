using RimWorld;
using Verse;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.Core
{
    /// <summary>
    /// 调试日志工具：受 AESettings.debugLogging 开关控制。
    /// 每次读取开关实时值，玩家切换后立即生效（不缓存）。
    /// 规则依据：Tick 路径日志必须在 if (debugActive) 后短路。
    /// 关键设计：所有 Log 重载接收 Func&lt;string&gt; 或延迟构造，
    /// 字符串仅在 IsActive=true 时构造，避免 Tick 路径 GC 压力。
    /// </summary>
    public static class AEDebug
    {
        // 不缓存：玩家可在设置界面随时切换 debugLogging，缓存会导致切换后不生效
        public static bool IsActive => AESettings.debugLogging;

        /// <summary>
        /// 延迟构造版本：仅在 IsActive 时调用 factory 构造字符串。
        /// Tick 路径首选此重载，避免字符串插值在调用前已分配。
        /// </summary>
        public static void Log(System.Func<string> messageFactory)
        {
            if (IsActive) Verse.Log.Message(messageFactory());
        }

        /// <summary>
        /// 直接字符串版本：保留兼容旧调用方。
        /// 注意：调用方传入的字符串在调用前已构造，无法避免 GC。
        /// 性能路径请改用 Log(Func&lt;string&gt;) 重载。
        /// </summary>
        public static void Log(string message)
        {
            if (IsActive) Verse.Log.Message(message);
        }

        /// <summary>
        /// 返回 Pawn 的标签字符串，DEBUG 模式下以"系统评级[#名字]"格式显示。
        /// 系统评级始终显示；若玩家指定了自定义评级则写入括号中：
        ///   无自定义：S#王五
        ///   有自定义：S(A)#王五  （S 为系统档，A 为玩家指定档）
        /// 非 DEBUG 模式下返回原始 LabelShort，零开销。
        ///
        /// 防重复：若 Nick 已被"全局人物评级"按钮改为 "S#王五" 格式，
        ///   pawn.LabelShort 本身已含评级前缀，此时直接返回 LabelShort，
        ///   避免拼接出 "S#S#王五" 的双重前缀。
        /// </summary>
        public static string Label(Pawn pawn)
        {
            if (pawn == null) return "null";
            if (!IsActive) return pawn.LabelShort;

            // 若 Nick 已带评级前缀（玩家点了"全局人物评级"按钮），直接返回 LabelShort
            // 避免重复拼接 "S#S#王五"
            string labelShort = pawn.LabelShort;
            if (AESettings.HasTierTagPrefixOnLabel(labelShort))
            {
                return labelShort;
            }

            // 系统评级始终固定显示（含配偶豁免，与评级标签一致）
            CombatTier autoTier = CombatEvaluator.GetSystemTier(pawn);
            string name = CombatEvaluator.GetPawnLookupName(pawn);

            // 命中自定义评级时把自定义档写入括号
            if (AESettings.TryGetCustomTier(name, out CombatTier customTier))
            {
                return autoTier + "(" + customTier + ")#" + name;
            }
            return autoTier + "#" + name;
        }
    }
}