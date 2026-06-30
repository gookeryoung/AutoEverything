using RimWorld;
using Verse;

namespace AutoEverything.Core
{
    /// <summary>
    /// TraitDef 查询缓存：集中定义多 degree/非原生 DefOf 的特质查询。
    /// 抽取自 CombatEvaluator 与 WeaponTraitScorer 的重复定义，
    /// 用 GetNamed(false) 安全查询，未加载 DLC 时返回 null 跳过。
    /// 注：原生 DefOf（如 TraitDefOf.Brawler）始终存在，直接引用无需缓存。
    /// </summary>
    [StaticConstructorOnStartup]
    public static class TraitDefCache
    {
        // 多 degree 特质：ShootingAccuracy 单一 defName，degree 区分乱开枪(-1)/冷枪手(+1)
        // 禁止把 degree 的 label 当作 defName 查询
        public static readonly TraitDef ShootingAccuracy = DefDatabase<TraitDef>.GetNamed("ShootingAccuracy", false);

        // 非原生 DefOf 的战斗特质（需安全查询，未加载时返回 null）
        public static readonly TraitDef Tough = DefDatabase<TraitDef>.GetNamed("Tough", false);
        public static readonly TraitDef Nimble = DefDatabase<TraitDef>.GetNamed("Nimble", false);
        public static readonly TraitDef Bloodlust = DefDatabase<TraitDef>.GetNamed("Bloodlust", false);

        // 工作狂神经质系列（用于 S/SS/SSS 评级判定）
        public static readonly TraitDef Industriousness = DefDatabase<TraitDef>.GetNamed("Industriousness", false);
        public static readonly TraitDef Neurotic = DefDatabase<TraitDef>.GetNamed("Neurotic", false);

        // 沉鱼落雁（Beauty degree=2）+ 社交双火 = S
        public static readonly TraitDef Beauty = DefDatabase<TraitDef>.GetNamed("Beauty", false);

        // 负面特质（用于 D 档降级判定）
        public static readonly TraitDef Pyromaniac = DefDatabase<TraitDef>.GetNamed("Pyromaniac", false);
        public static readonly TraitDef SlowLearner = DefDatabase<TraitDef>.GetNamed("SlowLearner", false);
        public static readonly TraitDef Wimp = DefDatabase<TraitDef>.GetNamed("Wimp", false);

        // 裸体主义者（影响服装评分，CompGearManager 使用）
        public static readonly TraitDef Nudist = DefDatabase<TraitDef>.GetNamed("Nudist", false);

        // 特殊天赋特质（Anomaly DLC，未加载时返回 null 跳过）
        public static readonly TraitDef TooSmart = DefDatabase<TraitDef>.GetNamed("TooSmart", false);
        public static readonly TraitDef Joyous = DefDatabase<TraitDef>.GetNamed("Joyous", false);
        public static readonly TraitDef BodyMastery = DefDatabase<TraitDef>.GetNamed("BodyMastery", false);
        public static readonly TraitDef VoidFascination = DefDatabase<TraitDef>.GetNamed("VoidFascination", false);
        public static readonly TraitDef Occultist = DefDatabase<TraitDef>.GetNamed("Occultist", false);
        public static readonly TraitDef Disturbing = DefDatabase<TraitDef>.GetNamed("Disturbing", false);
    }
}
