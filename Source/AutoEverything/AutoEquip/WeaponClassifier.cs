using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace AutoEverything.AutoEquip
{
    public enum WeaponKind
    {
        None,
        Melee,
        Ranged,
        Thrown
    }

    /// <summary>
    /// 武器分类与武器特性识别。
    /// 分类按 ThingDef 原生字段与 weaponTags/thingCategories 判断，不写死具体 DefName，
    /// 因此 MOD 武器（包括新增手雷）能自动进入对应类别。
    /// </summary>
    public static class WeaponClassifier
    {
        private const int TraitErrorSalt = 0xA910;

        public static WeaponKind Classify(ThingDef def)
        {
            if (def == null || !def.IsWeapon) return WeaponKind.None;
            if (IsGrenade(def)) return WeaponKind.Thrown;
            if (def.IsMeleeWeapon) return WeaponKind.Melee;
            if (def.IsRangedWeapon) return WeaponKind.Ranged;
            return WeaponKind.None;
        }

        /// <summary>
        /// 投掷物判定：优先看 thingCategories 是否包含 Grenades，
        /// 再看 weaponTags 是否以 Grenade 开头（原版手雷与多数 MOD 手雷都用该前缀）。
        /// </summary>
        public static bool IsGrenade(ThingDef def)
        {
            if (def == null) return false;

            List<ThingCategoryDef> categories = def.thingCategories;
            if (categories != null)
            {
                for (int i = 0; i < categories.Count; i++)
                {
                    if (categories[i] != null && string.Equals(categories[i].defName, "Grenades", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            List<string> tags = def.weaponTags;
            if (tags != null)
            {
                for (int i = 0; i < tags.Count; i++)
                {
                    string tag = tags[i];
                    if (!string.IsNullOrEmpty(tag) && tag.StartsWith("Grenade", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 读取“战斗特性”价值：只统计带战斗属性影响（statOffsets/statFactors/abilities）的特性。
        /// 装饰特性（镶金/镶玉等纯美观与售价）不计入，
        /// 避免高价但无战斗力的花架子武器被优先选择。
        /// 覆盖 Odyssey 独特武器与 Royalty 人格武器两条实现链。
        /// </summary>
        public static float GetCombatTraitValue(ThingWithComps weapon)
        {
            if (weapon == null) return 0f;

            float value = 0f;
            try
            {
                if (ModsConfig.OdysseyActive)
                {
                    CompUniqueWeapon unique = weapon.TryGetComp<CompUniqueWeapon>();
                    if (unique != null) AccumulateCombatValue(unique.TraitsListForReading, ref value);
                }

                if (ModsConfig.RoyaltyActive)
                {
                    CompBladelinkWeapon blade = weapon.TryGetComp<CompBladelinkWeapon>();
                    if (blade != null) AccumulateCombatValue(blade.TraitsListForReading, ref value);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] 武器特性读取失败: " + ex.Message,
                    (weapon.thingIDNumber ^ TraitErrorSalt));
                return 0f;
            }

            return value;
        }

        private static void AccumulateCombatValue(List<WeaponTraitDef> traits, ref float value)
        {
            if (traits == null) return;
            for (int i = 0; i < traits.Count; i++)
            {
                WeaponTraitDef trait = traits[i];
                if (trait == null || !IsCombatTrait(trait)) continue;
                value += trait.marketValueOffset;
            }
        }

        /// <summary>
        /// 特性是否影响战斗：带属性加成/惩罚即算。
        /// 纯装饰特性（无任何属性影响，仅改售价/美观）返回 false，
        /// 因此镶金/镶玉类特性不构成“特化优先”的依据。
        /// </summary>
        public static bool IsCombatTrait(WeaponTraitDef trait)
        {
            if (trait == null) return false;
            if (trait.statOffsets != null && trait.statOffsets.Count > 0) return true;
            if (trait.statFactors != null && trait.statFactors.Count > 0) return true;
            return false;
        }

        /// <summary>
        /// 拼接武器特性名称与价值，供调试日志说明“为什么这件特化武器更好”。
        /// 战斗特性标注价格，装饰特性标注“(装饰)”，便于核对优先级来源。
        /// </summary>
        public static string DescribeTraits(ThingWithComps weapon)
        {
            if (weapon == null) return string.Empty;

            StringBuilder sb = new StringBuilder();
            try
            {
                if (ModsConfig.OdysseyActive)
                {
                    CompUniqueWeapon unique = weapon.TryGetComp<CompUniqueWeapon>();
                    if (unique != null) AppendTraitLabels(unique.TraitsListForReading, sb);
                }

                if (ModsConfig.RoyaltyActive)
                {
                    CompBladelinkWeapon blade = weapon.TryGetComp<CompBladelinkWeapon>();
                    if (blade != null) AppendTraitLabels(blade.TraitsListForReading, sb);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] 武器特性名称读取失败: " + ex.Message,
                    (weapon.thingIDNumber ^ (TraitErrorSalt + 4)));
            }
            return sb.ToString();
        }

        private static void AppendTraitLabels(List<WeaponTraitDef> traits, StringBuilder sb)
        {
            if (traits == null) return;
            for (int i = 0; i < traits.Count; i++)
            {
                WeaponTraitDef trait = traits[i];
                if (trait == null) continue;
                if (sb.Length > 0) sb.Append('/');
                sb.Append(string.IsNullOrEmpty(trait.label) ? trait.defName : trait.label);
                if (IsCombatTrait(trait))
                    sb.Append('(').Append(trait.marketValueOffset.ToString("+0.#;-0.#")).Append(')');
                else
                    sb.Append("(装饰)");
            }
        }

        /// <summary>武器是否已绑定/生物编码给其他 Pawn，不能拿走。</summary>
        public static bool IsLockedToOtherPawn(ThingWithComps weapon, Pawn pawn)
        {
            if (weapon == null) return false;
            try
            {
                CompBiocodable biocodable = weapon.TryGetComp<CompBiocodable>();
                if (biocodable != null && biocodable.Biocoded && biocodable.CodedPawn != null && biocodable.CodedPawn != pawn)
                    return true;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] 武器生物编码检查失败: " + ex.Message,
                    (weapon.thingIDNumber ^ (TraitErrorSalt + 1)));
                return true;
            }
            return false;
        }

        /// <summary>当前主武器是否是已绑定给该 Pawn 的人格武器：绑定后不应尝试替换。</summary>
        public static bool IsBondedPersona(ThingWithComps weapon, Pawn pawn)
        {
            if (weapon == null || pawn == null) return false;
            if (!ModsConfig.RoyaltyActive) return false;
            try
            {
                CompBladelinkWeapon blade = weapon.TryGetComp<CompBladelinkWeapon>();
                return blade != null && blade.Biocoded && blade.CodedPawn == pawn;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] 人格武器绑定检查失败: " + ex.Message,
                    (weapon.thingIDNumber ^ (TraitErrorSalt + 2)));
                return true;
            }
        }

        /// <summary>人格武器带“嫉妒”特性时，会拒绝其他武器；已绑定时直接跳过该 Pawn 的换装。</summary>
        public static bool HasJealousTrait(ThingWithComps weapon)
        {
            if (weapon == null || !ModsConfig.RoyaltyActive) return false;
            try
            {
                CompBladelinkWeapon blade = weapon.TryGetComp<CompBladelinkWeapon>();
                if (blade == null || blade.TraitsListForReading == null) return false;
                List<WeaponTraitDef> traits = blade.TraitsListForReading;
                for (int i = 0; i < traits.Count; i++)
                {
                    if (traits[i] == null) continue;
                    if (string.Equals(traits[i].defName, "Jealous", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] 人格武器特性检查失败: " + ex.Message,
                    (weapon.thingIDNumber ^ (TraitErrorSalt + 3)));
            }
            return false;
        }
    }
}
