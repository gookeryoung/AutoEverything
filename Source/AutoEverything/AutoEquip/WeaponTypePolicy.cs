using System;
using System.Collections.Generic;

namespace AutoEverything.AutoEquip
{
    /// <summary>
    /// 一把武器的“类型键”：具体武器类别与武器标签。
    /// 只存字符串，由运行时从 ThingDef 提取，便于纯逻辑测试。
    /// </summary>
    public sealed class WeaponTypeKeys
    {
        public readonly List<string> Classes = new List<string>();
        public readonly List<string> Tags = new List<string>();

        public static readonly WeaponTypeKeys Empty = new WeaponTypeKeys();
    }

    /// <summary>
    /// “同类型武器”判定（纯函数，不依赖 RimWorld 运行时）。
    ///
    /// 用 1.6 的武器类别（WeaponClassDef.defName）判断两把武器定位是否重复：
    /// 泵动霰弹枪与链式霰弹枪都属于 ShortShots，因此不能一把做主武器、另一把做副武器。
    /// 泛类别（Ranged/Melee/Neolithic/Ultratech）覆盖面过广，不参与判定，
    /// 否则所有远程武器都会被当成同一类型。
    /// MOD 武器缺 weaponClasses 时回退到 weaponTags，并排除同样过泛的标签。
    /// </summary>
    public static class WeaponTypePolicy
    {
        private static readonly string[] GenericClasses =
        {
            "Melee", "Ranged", "Neolithic", "Ultratech"
        };

        /// <summary>过泛的标签：只代表枪械大类或科技等级，不能作为“同类型”依据。</summary>
        private static readonly string[] GenericTags =
        {
            "Gun", "Weapon", "Ranged", "Melee", "Neolithic",
            "SimpleGun", "IndustrialGunAdvanced", "SpacerGun", "UltratechGun"
        };

        /// <summary>两组武器是否属于同一类型（存在共同的具体类别，或类别缺失时存在共同的具体标签）。</summary>
        public static bool IsSameType(IList<string> classesA, IList<string> tagsA,
            IList<string> classesB, IList<string> tagsB)
        {
            if (HasSharedSpecific(classesA, classesB, true)) return true;

            // 双方都有具体类别时以类别为准：类别不同即视为不同类型，不再用标签兜底
            if (HasSpecific(classesA, true) && HasSpecific(classesB, true)) return false;

            return HasSharedSpecific(tagsA, tagsB, false);
        }

        private static bool HasSharedSpecific(IList<string> a, IList<string> b, bool isClass)
        {
            if (a == null || b == null) return false;
            for (int i = 0; i < a.Count; i++)
            {
                string left = a[i];
                if (string.IsNullOrEmpty(left)) continue;
                if (isClass ? IsGenericClass(left) : IsGenericTag(left)) continue;

                for (int j = 0; j < b.Count; j++)
                {
                    if (string.Equals(left, b[j], StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        private static bool HasSpecific(IList<string> values, bool isClass)
        {
            if (values == null) return false;
            for (int i = 0; i < values.Count; i++)
            {
                string value = values[i];
                if (string.IsNullOrEmpty(value)) continue;
                if (isClass ? IsGenericClass(value) : IsGenericTag(value)) continue;
                return true;
            }
            return false;
        }

        public static bool IsGenericClass(string name)
        {
            return ContainsIgnoreCase(GenericClasses, name);
        }

        public static bool IsGenericTag(string name)
        {
            return ContainsIgnoreCase(GenericTags, name);
        }

        private static bool ContainsIgnoreCase(string[] values, string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < values.Length; i++)
            {
                if (string.Equals(values[i], name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
