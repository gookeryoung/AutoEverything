using RimWorld;
using Verse;

namespace AutoEverything.Core
{
    /// <summary>
    /// Pawn 适配性检查工具：集中判断 Pawn 是否适合自动管理（工作分配/评级/星标）。
    /// 设计目的：避免动物、机械族、昆虫、异常实体等不适用类别被纳入自动流程。
    /// </summary>
    public static class PawnSuitabilityChecker
    {
        /// <summary>
        /// 判断 Pawn 是否适合自动管理。
        /// 适用：仅限人类like（含外星人 mod 的类人种族）。
        /// 排除：动物、机械族、昆虫、异常实体、食尸鬼等。
        /// </summary>
        public static bool CanManageGear(Pawn pawn)
        {
            if (pawn == null) return false;
            // 必须有 RaceProps
            if (pawn.RaceProps == null) return false;
            // 仅人类like 适合自动管理
            // 动物（RaceProps.Animal）和机械族（RaceProps.IsMechanoid）原生不使用工作/装备系统
            // 昆虫（RaceProps.Insect）和异常实体（RaceProps.Entity）同样不适用
            return pawn.RaceProps.Humanlike;
        }
    }
}
