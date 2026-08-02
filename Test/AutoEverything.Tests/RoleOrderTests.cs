using System;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.Tests
{
    /// <summary>
    /// RoleDetector.GetRoleOrder 角色排序优先级的单元测试。
    /// 表驱动覆盖全部 8 个 Role 枚举值，验证排序优先级映射契约：
    /// Brawler(0) → Shooter(1) → Doctor(2) → Worker(3) → Pacifist(4)
    /// → Hunter(5) → Leader(6) → Default(99)。
    /// 设计意图：前排战斗在前，后勤居中，其他角色在后，未识别角色沉底。
    /// 注：方法原位于 AESettings.GetRoleOrder（private），因 AESettings 继承 ModSettings
    /// 测试项目无法直接引用，已迁移至 RoleDetector 便于测试。
    /// </summary>
    public static class RoleOrderTests
    {
        public static int RunAll()
        {
            int failures = 0;
            int total = 0;

            // ── 按优先级升序逐一验证（数字小排前面）──────────────────
            Check(Role.Brawler, 0, "Brawler → 0（前排近战最前）", ref failures, ref total);
            Check(Role.Shooter, 1, "Shooter → 1（前排远程）", ref failures, ref total);
            Check(Role.Doctor, 2, "Doctor → 2（后勤医疗）", ref failures, ref total);
            Check(Role.Worker, 3, "Worker → 3（后勤通用）", ref failures, ref total);
            Check(Role.Pacifist, 4, "Pacifist → 4（后勤非战斗）", ref failures, ref total);
            Check(Role.Hunter, 5, "Hunter → 5（其他）", ref failures, ref total);
            Check(Role.Leader, 6, "Leader → 6（其他）", ref failures, ref total);
            Check(Role.Default, 99, "Default → 99（未识别沉底）", ref failures, ref total);

            Console.WriteLine($"[RoleOrderTests] {total - failures}/{total} passed");
            return failures;
        }

        private static void Check(Role role, int expected, string label,
            ref int failures, ref int total)
        {
            total++;
            int actual = RoleDetector.GetRoleOrder(role);
            if (actual != expected)
            {
                Console.WriteLine($"  FAIL GetRoleOrder: {label}: expected {expected}, got {actual}");
                failures++;
            }
        }
    }
}
