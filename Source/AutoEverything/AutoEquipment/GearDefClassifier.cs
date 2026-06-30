using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using AutoEverything.RoleEvaluation;
using AutoEverything.Allocation;
using AutoEverything.Core;

namespace AutoEverything.AutoEquipment
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
        /// 护盾腰带判定：defName 含 "SHIELD" 且位于 Belt 层。
        /// 双重校验避免误判护盾头盔等非腰带护盾附件，
        /// 统一 WeaponTraitScorer 与 SidearmAllocator 的判定逻辑。
        /// </summary>
        public static bool IsShieldBelt(Thing thing)
        {
            if (thing?.def == null) return false;
            if (thing.def.defName.IndexOf("SHIELD", StringComparison.OrdinalIgnoreCase) < 0) return false;
            // 必须位于 Belt 层，排除护盾头盔/护盾背心等非腰带护盾
            return thing.def.apparel?.layers != null
                && thing.def.apparel.layers.Contains(ApparelLayerDefOf.Belt);
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

        /// <summary>
        /// 检查 Pawn 是否已穿戴 belt 层附件（护盾腰带/消防背包等）。
        /// 统一 BeltAllocator.HasBelt 的遍历逻辑，避免重复实现。
        /// </summary>
        public static bool HasBeltLayerApparel(Pawn pawn)
        {
            if (pawn?.apparel?.WornApparel == null) return false;
            List<Apparel> worn = pawn.apparel.WornApparel;
            for (int i = 0; i < worn.Count; i++)
            {
                Apparel ap = worn[i];
                if (ap.def?.apparel?.layers == null) continue;
                if (ap.def.apparel.layers.Contains(ApparelLayerDefOf.Belt)) return true;
            }
            return false;
        }
    }
}