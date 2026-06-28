using RimWorld;
using Verse;

namespace AutoEquipment
{
    /// <summary>
    /// Pawn 适配性检查工具：集中判断 Pawn/ThingDef 是否适合装备管理。
    /// 设计目的：避免动物、机械族、昆虫、异常实体等不适用类别
    /// 被错误注入 CompGearManager 并在 Tick 时尝试寻找装备。
    /// </summary>
    public static class PawnSuitabilityChecker
    {
        /// <summary>
        /// 判断 Pawn 是否适合装备管理。
        /// 适用：仅限人类like（含外星人 mod 的类人种族）。
        /// 排除：动物、机械族、昆虫、异常实体、食尸鬼等。
        /// </summary>
        public static bool CanManageGear(Pawn pawn)
        {
            if (pawn == null) return false;
            // 必须有 RaceProps
            if (pawn.RaceProps == null) return false;
            // 仅人类like 适合装备管理
            // 动物（RaceProps.Animal）和机械族（RaceProps.IsMechanoid）原生不使用武器装备槽
            // 昆虫（RaceProps.Insect）和异常实体（RaceProps.Entity）同样不适用
            return pawn.RaceProps.Humanlike;
        }

        /// <summary>
        /// 判断 ThingDef 是否适合注入 CompGearManager。
        /// 用于 PawnDef 注入阶段，避免给动物/机械族等 ThingDef 挂载无用 Comp。
        /// </summary>
        public static bool CanManageGearDef(ThingDef def)
        {
            if (def == null) return false;
            if (def.category != ThingCategory.Pawn) return false;
            if (def.race == null) return false;
            // 仅人类like 种族 ThingDef 适合挂载 Comp
            return def.race.Humanlike;
        }
    }
}
