using RimWorld;
using Verse;

namespace AutoEverything.Core
{
    /// <summary>
    /// AbilityDef 查询缓存：集中定义需安全查询的能力。
    /// 用 GetNamed(false) 查询，未加载异常 DLC 时返回 null，调用方需判空后跳过。
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AbilityDefCache
    {
        // 异常 DLC 的变异体（CreepJoiner）专属技能：无法通过后天培养获得，
        // 因此作为 S 档的直接判定依据
        public static readonly AbilityDef UnnaturalHealing = DefDatabase<AbilityDef>.GetNamed("UnnaturalHealing", false);
        public static readonly AbilityDef TransmuteSteel = DefDatabase<AbilityDef>.GetNamed("TransmuteSteel", false);
    }
}
