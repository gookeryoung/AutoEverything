using System;
using System.Collections.Generic;
using AutoEverything.AutoEquip;

namespace AutoEverything.Tests
{
    /// <summary>
    /// 自动装备纯逻辑测试。
    /// 不依赖 RimWorld 运行时：只验证比较器、换装阈值与白名单解析，
    /// 保证排序规则（特化 &gt; 一般、特性价格、品质）可独立回归。
    /// </summary>
    public static class EquipPolicyTests
    {
        public static int RunAll()
        {
            int failures = 0;
            int total = 0;

            failures += RunWeaponCompareTests(ref total);
            failures += RunSwapTests(ref total);
            failures += RunRuleTests(ref total);
            failures += RunWeaponTypeTests(ref total);

            return failures;
        }

        private static int RunWeaponCompareTests(ref int total)
        {
            int failures = 0;

            // 特化武器即使战斗分更低，也优先于一般武器
            WeaponScoreInput general = Make(false, 0f, 999f, 6, "GunA");
            WeaponScoreInput special = Make(true, 1f, 1f, 0, "UniqueA");
            CheckCompare(special, general, true, "特化 > 一般（无视战斗分）", ref failures, ref total);

            // 同为特化：战斗特性价值高者优先
            WeaponScoreInput specialLow = Make(true, 10f, 500f, 2, "UniqueA");
            WeaponScoreInput specialHigh = Make(true, 30f, 100f, 2, "UniqueA");
            CheckCompare(specialHigh, specialLow, true, "特化同武器：特性价值高者优先", ref failures, ref total);

            // 特性价值相同时比较战斗分
            WeaponScoreInput specialCombatLow = Make(true, 10f, 100f, 2, "UniqueA");
            WeaponScoreInput specialCombatHigh = Make(true, 10f, 200f, 2, "UniqueB");
            CheckCompare(specialCombatHigh, specialCombatLow, true, "特化同特性价：战斗分高者优先", ref failures, ref total);

            // 普通武器：品质优先于战斗分
            WeaponScoreInput normalGood = Make(false, 0f, 10f, 3, "GunA");
            WeaponScoreInput normalLegendary = Make(false, 0f, 1f, 6, "GunA");
            CheckCompare(normalLegendary, normalGood, true, "普通武器：品质高者优先", ref failures, ref total);

            // 普通武器同品质：战斗分高者优先
            WeaponScoreInput normalLow = Make(false, 0f, 10f, 2, "GunA");
            WeaponScoreInput normalHigh = Make(false, 0f, 20f, 2, "GunA");
            CheckCompare(normalHigh, normalLow, true, "普通武器同品质：战斗分高者优先", ref failures, ref total);

            // 完全同分：按 defName 稳定排序
            WeaponScoreInput sameA = Make(false, 0f, 10f, 2, "GunA");
            WeaponScoreInput sameB = Make(false, 0f, 10f, 2, "GunB");
            CheckCompare(sameA, sameB, true, "同分按 defName 稳定排序", ref failures, ref total);

            // 可比分数：特化基数远高于一般
            CheckBool(EquipPolicy.ComparableScore(special) > EquipPolicy.ComparableScore(general), true,
                "可比分数：特化 > 一般", ref failures, ref total);

            // 可比分数不含市价：战斗分与品质完全相同的武器分数必须相等
            WeaponScoreInput valueA = Make(false, 0f, 10f, 2, "GunA");
            WeaponScoreInput valueB = Make(false, 0f, 10f, 2, "GunB");
            CheckBool(Math.Abs(EquipPolicy.ComparableScore(valueA) - EquipPolicy.ComparableScore(valueB)) < 0.0001f,
                true, "可比分数与市价无关", ref failures, ref total);

            Console.WriteLine($"[EquipPolicyTests/Compare] {total - failures}/{total} passed");
            return failures;
        }

        private static int RunSwapTests(ref int total)
        {
            int failures = 0;

            CheckBool(EquipPolicy.ShouldSwap(100f, 120f, 10f), true,
                "提升 20% > 阈值 10% → 换装", ref failures, ref total);
            CheckBool(EquipPolicy.ShouldSwap(100f, 105f, 10f), false,
                "提升 5% < 阈值 10% → 不换装", ref failures, ref total);
            CheckBool(EquipPolicy.ShouldSwap(100f, 111f, 10f), true,
                "刚好超过阈值 → 换装", ref failures, ref total);
            CheckBool(EquipPolicy.ShouldSwap(0f, 1f, 10f), true,
                "当前无装备 → 直接换", ref failures, ref total);
            CheckBool(EquipPolicy.ShouldSwap(100f, 0f, 0f), false,
                "候选分数为 0 → 不换", ref failures, ref total);
            CheckBool(EquipPolicy.ShouldSwap(100f, 101f, 0f), true,
                "阈值 0 时任意提升都换", ref failures, ref total);

            CheckBool(EquipPolicy.IsBetterApparel(50f, 40f, 10f), true,
                "服装提升 25% → 换", ref failures, ref total);
            CheckBool(EquipPolicy.IsBetterApparel(42f, 40f, 10f), false,
                "服装提升 5% → 不换", ref failures, ref total);

            Console.WriteLine($"[EquipPolicyTests/Swap] {total - failures}/{total} passed");
            return failures;
        }

        private static int RunRuleTests(ref int total)
        {
            int failures = 0;

            List<string> defaults = EquipRules.CreateDefaultEntries();
            CheckInt(defaults.Count, EquipRules.RoleCount, "默认白名单条目数=5", ref failures, ref total);

            EquipRoleRule front = EquipRules.GetRule(defaults, 0);
            CheckBool(front.Melee && !front.Ranged, true, "前排默认近战", ref failures, ref total);

            EquipRoleRule shooter = EquipRules.GetRule(defaults, 1);
            CheckBool(shooter.Ranged && shooter.Thrown, true, "射手默认远程+投掷", ref failures, ref total);
            CheckInt(shooter.Sidearms, 2, "射手默认副武器=2", ref failures, ref total);

            EquipRoleRule other = EquipRules.GetRule(defaults, 4);
            CheckInt(other.Sidearms, 1, "其他默认副武器=1", ref failures, ref total);

            // 序列化往返
            EquipRoleRule custom = new EquipRoleRule();
            custom.Melee = true;
            custom.Ranged = true;
            custom.Thrown = true;
            custom.Sidearms = 3;
            string serialized = EquipRules.Serialize(3, custom);
            int parsedRole;
            EquipRoleRule parsed;
            CheckBool(EquipRules.TryParse(serialized, out parsedRole, out parsed), true,
                "序列化可解析", ref failures, ref total);
            CheckInt(parsedRole, 3, "解析角色=3", ref failures, ref total);
            CheckBool(parsed.Melee && parsed.Ranged && parsed.Thrown, true,
                "解析三项白名单", ref failures, ref total);
            CheckInt(parsed.Sidearms, 3, "解析副武器=3", ref failures, ref total);

            // 损坏条目回退默认
            List<string> broken = new List<string> { "bad", "1#1#0#0#9" };
            EquipRoleRule normalizedShooter = EquipRules.GetRule(broken, 1);
            CheckInt(normalizedShooter.Sidearms, EquipRules.MaxSidearms,
                "非法副武器数量被钳制到上限", ref failures, ref total);
            CheckBool(EquipRules.GetRule(broken, 0).Melee, true,
                "缺失角色回退默认值", ref failures, ref total);

            List<string> normalized = EquipRules.Normalize(broken);
            CheckInt(normalized.Count, EquipRules.RoleCount, "Normalize 补齐五类角色", ref failures, ref total);

            // SetRule 覆盖已有条目而非追加
            EquipRules.SetRule(defaults, 0, custom);
            CheckInt(defaults.Count, EquipRules.RoleCount, "SetRule 不新增重复条目", ref failures, ref total);
            EquipRoleRule updated = EquipRules.GetRule(defaults, 0);
            CheckInt(updated.Sidearms, 3, "SetRule 生效", ref failures, ref total);

            CheckBool(EquipRules.ClampSidearms(-5) == 0 && EquipRules.ClampSidearms(99) == 3, true,
                "副武器数量钳制 0-3", ref failures, ref total);

            Console.WriteLine($"[EquipPolicyTests/Rules] {total - failures}/{total} passed");
            return failures;
        }

        private static int RunWeaponTypeTests(ref int total)
        {
            int failures = 0;

            // 泵动霰弹枪与链式霰弹枪同为 ShortShots → 同类，不能一主一副
            CheckBool(WeaponTypePolicy.IsSameType(
                new List<string> { "ShortShots" }, null,
                new List<string> { "ShortShots" }, null), true,
                "同类别 ShortShots 视为同类型", ref failures, ref total);

            // 霰弹枪 vs 突击步枪（LongShots）→ 不同类
            CheckBool(WeaponTypePolicy.IsSameType(
                new List<string> { "ShortShots" }, null,
                new List<string> { "LongShots" }, null), false,
                "ShortShots 与 LongShots 不同类", ref failures, ref total);

            // 只有泛类别时不能把不同武器当成同类
            CheckBool(WeaponTypePolicy.IsSameType(
                new List<string> { "Ranged" }, null,
                new List<string> { "ShortShots" }, null), false,
                "泛类别 Ranged 不参与同类判定", ref failures, ref total);

            // 双方都有具体类别时以类别为准，标签相同也不算同类
            CheckBool(WeaponTypePolicy.IsSameType(
                new List<string> { "ShortShots" }, new List<string> { "IndustrialGunAdvanced" },
                new List<string> { "LongShots" }, new List<string> { "IndustrialGunAdvanced" }), false,
                "类别不同时不再用标签兜底", ref failures, ref total);

            // 类别缺失时回退到武器标签
            CheckBool(WeaponTypePolicy.IsSameType(
                null, new List<string> { "ModShotgun" },
                null, new List<string> { "ModShotgun" }), true,
                "缺类别时按具体标签判定同类", ref failures, ref total);

            // 泛标签不能作为同类依据
            CheckBool(WeaponTypePolicy.IsSameType(
                null, new List<string> { "IndustrialGunAdvanced" },
                null, new List<string> { "IndustrialGunAdvanced" }), false,
                "泛标签不参与同类判定", ref failures, ref total);

            // 空数据不应误判
            CheckBool(WeaponTypePolicy.IsSameType(null, null, null, null), false,
                "无类别无标签不视为同类", ref failures, ref total);

            Console.WriteLine($"[EquipPolicyTests/WeaponType] {total - failures}/{total} passed");
            return failures;
        }

        private static WeaponScoreInput Make(bool isSpecial, float traitValue, float combat,
            int quality, string defName)
        {
            WeaponScoreInput input = new WeaponScoreInput();
            input.IsSpecial = isSpecial;
            input.TraitValue = traitValue;
            input.CombatScore = combat;
            input.QualityRank = quality;
            input.DefName = defName;
            return input;
        }

        private static void CheckCompare(WeaponScoreInput better, WeaponScoreInput worse,
            bool expectBetter, string label, ref int failures, ref int total)
        {
            int result = EquipPolicy.CompareWeapon(better, worse);
            bool ok = expectBetter ? result < 0 : result > 0;
            CheckBool(ok, true, label + "（实际 " + result + "）", ref failures, ref total);
        }

        private static void CheckInt(int actual, int expected, string label,
            ref int failures, ref int total)
        {
            total++;
            if (actual != expected)
            {
                Console.WriteLine("  FAIL: " + label + ": expected " + expected + ", got " + actual);
                failures++;
            }
        }

        private static void CheckBool(bool actual, bool expected, string label,
            ref int failures, ref int total)
        {
            total++;
            if (actual != expected)
            {
                Console.WriteLine("  FAIL: " + label + ": expected " + expected + ", got " + actual);
                failures++;
            }
        }
    }
}
