using RimWorld;
using Verse;

namespace AutoEquipment
{
    /// <summary>
    /// 调试日志工具：受 AESettings.debugLogging 开关控制。
    /// 每次读取开关实时值，玩家切换后立即生效（不缓存）。
    /// 规则依据：Tick 路径日志必须在 if (debugActive) 后短路。
    /// </summary>
    public static class AEDebug
    {
        // 不缓存：玩家可在设置界面随时切换 debugLogging，缓存会导致切换后不生效
        public static bool IsActive => AESettings.debugLogging;

        public static void Log(string message)
        {
            if (IsActive) Verse.Log.Message(message);
        }

        /// <summary>
        /// 返回 Pawn 的标签字符串，DEBUG 模式下附加战斗价值档次（如 "王五[S]"）。
        /// 非 DEBUG 模式下返回原始 LabelShort，零开销。
        /// </summary>
        public static string Label(Pawn pawn)
        {
            if (pawn == null) return "null";
            if (!IsActive) return pawn.LabelShort;
            return pawn.LabelShort + "[" + SidearmAllocator.GetCombatTier(pawn) + "]";
        }
    }
}
