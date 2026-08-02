using System;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.Tests
{
    /// <summary>
    /// RoleDetector 角色护甲偏好与后排判定的单元测试。
    /// 表驱动覆盖全部 8 个 Role 枚举值，验证 GetArmorPreference 与 IsBackRow 的映射契约：
    /// - Brawler：Heavy（前排，非后排）
    /// - Shooter/Hunter/Leader：Flexible（后排）
    /// - Worker/Doctor/Pacifist/Default：Light（非后排）
    /// </summary>
    public static class RoleDetectorTests
    {
        public static int RunAll()
        {
            int failures = 0;
            int total = 0;

            // ── GetArmorPreference：表驱动覆盖全部 Role ─────────────
            CheckArmor(Role.Default, ArmorPreference.Light, "Default → Light", ref failures, ref total);
            CheckArmor(Role.Shooter, ArmorPreference.Flexible, "Shooter → Flexible", ref failures, ref total);
            CheckArmor(Role.Brawler, ArmorPreference.Heavy, "Brawler → Heavy", ref failures, ref total);
            CheckArmor(Role.Doctor, ArmorPreference.Light, "Doctor → Light", ref failures, ref total);
            CheckArmor(Role.Hunter, ArmorPreference.Flexible, "Hunter → Flexible", ref failures, ref total);
            CheckArmor(Role.Worker, ArmorPreference.Light, "Worker → Light", ref failures, ref total);
            CheckArmor(Role.Pacifist, ArmorPreference.Light, "Pacifist → Light", ref failures, ref total);
            CheckArmor(Role.Leader, ArmorPreference.Flexible, "Leader → Flexible", ref failures, ref total);

            // ── IsBackRow：表驱动覆盖全部 Role ─────────────────────
            // 后排定义 = GetArmorPreference == Flexible，与 GetArmorPreference 测试一致
            CheckBackRow(Role.Default, false, "Default → 非后排", ref failures, ref total);
            CheckBackRow(Role.Shooter, true, "Shooter → 后排", ref failures, ref total);
            CheckBackRow(Role.Brawler, false, "Brawler → 非后排", ref failures, ref total);
            CheckBackRow(Role.Doctor, false, "Doctor → 非后排", ref failures, ref total);
            CheckBackRow(Role.Hunter, true, "Hunter → 后排", ref failures, ref total);
            CheckBackRow(Role.Worker, false, "Worker → 非后排", ref failures, ref total);
            CheckBackRow(Role.Pacifist, false, "Pacifist → 非后排", ref failures, ref total);
            CheckBackRow(Role.Leader, true, "Leader → 后排", ref failures, ref total);

            Console.WriteLine($"[RoleDetectorTests] {total - failures}/{total} passed");
            return failures;
        }

        private static void CheckArmor(Role role, ArmorPreference expected, string label,
            ref int failures, ref int total)
        {
            total++;
            ArmorPreference actual = RoleDetector.GetArmorPreference(role);
            if (actual != expected)
            {
                Console.WriteLine($"  FAIL GetArmorPreference: {label}: expected {expected}, got {actual}");
                failures++;
            }
        }

        private static void CheckBackRow(Role role, bool expected, string label,
            ref int failures, ref int total)
        {
            total++;
            bool actual = RoleDetector.IsBackRow(role);
            if (actual != expected)
            {
                Console.WriteLine($"  FAIL IsBackRow: {label}: expected {expected}, got {actual}");
                failures++;
            }
        }
    }
}
