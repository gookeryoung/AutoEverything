using System;
using RimWorld;
using Verse;

namespace AutoEverything.Core
{
    /// <summary>
    /// DLC 兼容性检测工具：集中包装所有 DLC 特定 API，
    /// 避免未加载 DLC 时直接调用导致 TypeLoadException。
    /// </summary>
    public static class DLCCompat
    {
        // 缓存 DLC 加载状态，避免每次访问 ModsConfig 反复读取
        private static readonly bool Anomaly = ModsConfig.AnomalyActive;

        // 异常去重 ID 前缀：每个方法独立偏移，避免不同方法异常互相覆盖
        private const int GhoulErrorIdBase = 0xA100;

        /// <summary>
        /// 判断是否为食尸鬼（Anomaly DLC 的变异体）。
        /// 食尸鬼参与评级显示但不参与工作分配（由 WorkAllocator/PawnCollector 显式过滤）。
        /// </summary>
        public static bool IsGhoul(Pawn pawn)
        {
            if (!Anomaly || pawn == null) return false;
            try { return pawn.IsGhoul; }
            catch (Exception ex)
            {
                // 规则要求：异常用 Log.ErrorOnce 防重复，不能静默吞异常
                Log.ErrorOnce("[AutoEverything] DLCCompat.IsGhoul 异常: " + ex.Message,
                    (pawn?.thingIDNumber ?? 0) ^ GhoulErrorIdBase);
                return false;
            }
        }
    }
}
