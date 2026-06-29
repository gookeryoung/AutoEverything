using System;
using Verse;

namespace AutoEquipment
{
    /// <summary>
    /// 装备 defName 启发式识别工具：统一判定武器/附件的类别归属。
    /// 抽取自 SidearmAllocator 与 BeltAllocator 的重复实现，
    /// 用 defName/label 子串匹配覆盖原生与 MOD 扩展的同类装备。
    /// 用 IndexOf(OrdinalIgnoreCase) 避免每次 ToUpperInvariant 分配字符串。
    /// </summary>
    public static class GearDefClassifier
    {
        /// <summary>
        /// EMP 武器判定：通过 defName/label 启发式识别。
        /// 覆盖原生 EMP 手榴弹、EMP 炮及 MOD 扩展的 EMP 武器。
        /// </summary>
        public static bool IsEmpWeapon(Thing weapon)
        {
            if (weapon?.def == null) return false;
            return weapon.def.defName.IndexOf("EMP", StringComparison.OrdinalIgnoreCase) >= 0
                || weapon.def.label.IndexOf("EMP", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// 护盾腰带判定：通过 defName 启发式识别。
        /// 覆盖原生护盾腰带及 MOD 扩展的护盾类附件。
        /// </summary>
        public static bool IsShieldBelt(Thing thing)
        {
            return thing?.def != null
                && thing.def.defName.IndexOf("SHIELD", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// 消防背包判定：通过 defName 启发式识别。
        /// 覆盖原生消防背包及 MOD 扩展的泡沫类灭火附件。
        /// </summary>
        public static bool IsFirefoamPack(Thing thing)
        {
            return thing?.def != null
                && thing.def.defName.IndexOf("FIREFOAM", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
