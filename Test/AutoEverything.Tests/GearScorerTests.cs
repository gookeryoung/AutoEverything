using System;
using AutoEverything.AutoEquipment;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.Tests
{
    /// <summary>
    /// GearScorer 4 个纯逻辑核心的单元测试。
    ///
    /// 覆盖范围：
    /// 1. ComputeArmorScoreCore — 三项归一化 × 权重，含零值/单项极值/权重为 0/负值防御
    /// 2. ComputeLayerMatchScoreCore — Heavy/Light/Flexible 三种偏好的加减分契约
    ///    验证 Light 的 armorSum 截断到 1.0（避免高护甲反而加分）+ 双向（低加高减）
    /// 3. ComputeInsulationScoreCore — 寒冷/炎热/舒适三档边界（含 tempMargin 边界）
    /// 4. ComputeMovementPenaltyCore — Worker/Doctor/Pacifist 共享 workerW，
    ///    Shooter/Hunter 共享 backRowW，其他用 frontRowW
    ///
    /// 设计原则：测试不依赖 RimWorld 运行时（无 Apparel/Pawn 实例），
    /// 仅调用 internal static 纯逻辑方法，构造数值输入即可验证。
    ///
    /// 业务契约（与 README.md "评分公式" 章节同步）：
    /// - armorScore = (Sharp+Blunt+Heat) × 0.3333 × geArmorWeight
    /// - layerMatchScore：
    ///   Heavy  = armorSum × geHeavyArmorMatchWeight
    ///   Light  = (1 - min(armorSum,1)) × geLightArmorMatchWeight - armorSum × geLightArmorAvoidWeight
    ///   Flex   = armorSum × geFlexibleArmorMatchWeight
    /// - insulationScore：寒冷用 Cold、炎热用 Heat、舒适为 0
    /// - movementPenalty = mass × 角色敏感度权重
    /// </summary>
    public static class GearScorerTests
    {
        // float 比较误差容忍：0.0001f 足以区分公式分支错误
        private const float Epsilon = 0.0001f;

        public static int RunAll()
        {
            int failures = 0;
            int total = 0;

            failures += RunArmorScoreTests(ref total);
            failures += RunLayerMatchTests(ref total);
            failures += RunInsulationTests(ref total);
            failures += RunMovementPenaltyTests(ref total);

            return failures;
        }

        // ════════════════════════════════════════════════════════════
        // 1. ComputeArmorScoreCore：三项归一化 × 权重
        // ════════════════════════════════════════════════════════════

        private static int RunArmorScoreTests(ref int total)
        {
            int failures = 0;

            // 全 0：0 分
            CheckArmor(0f, 0f, 0f, 1.0f, 0f, "all zero → 0", ref failures, ref total);

            // 全 1.0 + weight=1.0：(1+1+1)*0.3333 = 0.9999
            CheckArmor(1f, 1f, 1f, 1.0f, 0.9999f, "all 1.0 weight=1.0 → 0.9999", ref failures, ref total);

            // 单项极值：sharp=3.0, blunt=0, heat=0, weight=1.0 → 3*0.3333 = 0.9999
            // 设计意图：避免单项极值压倒其他维度（平均化）
            CheckArmor(3f, 0f, 0f, 1.0f, 0.9999f, "sharp=3 only → 0.9999 (averaged)", ref failures, ref total);

            // 权重为 0：即使护甲高也得 0 分（玩家可关闭护甲评分）
            CheckArmor(2f, 2f, 2f, 0f, 0f, "weight=0 → 0 (disabled)", ref failures, ref total);

            // 权重为 2.0：放大 2 倍
            CheckArmor(1f, 1f, 1f, 2.0f, 1.9998f, "weight=2.0 → 2x", ref failures, ref total);

            // 真实场景：防弹衣 sharp=0.5, blunt=0.2, heat=0.1, weight=1.0
            // (0.5+0.2+0.1)*0.3333 = 0.26664
            CheckArmor(0.5f, 0.2f, 0.1f, 1.0f, 0.26664f, "real vest → 0.26664", ref failures, ref total);

            // 负值防御：理论不应发生，但纯逻辑不应崩溃
            // (-1+0+0)*0.3333 = -0.3333
            CheckArmor(-1f, 0f, 0f, 1.0f, -0.3333f, "negative sharp → -0.3333 (defensive)", ref failures, ref total);

            Console.WriteLine($"[GearScorerTests/ArmorScore] {total - failures}/{total} passed");
            return failures;
        }

        // ════════════════════════════════════════════════════════════
        // 2. ComputeLayerMatchScoreCore：Heavy/Light/Flexible 三种偏好
        // ════════════════════════════════════════════════════════════

        private static int RunLayerMatchTests(ref int total)
        {
            int failures = 0;

            // 默认权重（与 AESettings 默认值一致）
            const float heavyW = 2.0f;
            const float lightMatchW = 1.5f;
            const float lightAvoidW = 0.5f;
            const float flexW = 1.0f;

            // ── Heavy 偏好：线性加分，护甲越高越好 ──────────────────
            CheckLayer(1.5f, 0f, ArmorPreference.Heavy, heavyW, lightMatchW, lightAvoidW, flexW,
                1.5f * heavyW, "Heavy + armorSum=1.5 → 3.0 (linear)", ref failures, ref total);

            CheckLayer(0f, 0f, ArmorPreference.Heavy, heavyW, lightMatchW, lightAvoidW, flexW,
                0f, "Heavy + armorSum=0 → 0 (no armor no bonus)", ref failures, ref total);

            CheckLayer(1.0f, 1.0f, ArmorPreference.Heavy, heavyW, lightMatchW, lightAvoidW, flexW,
                2.0f * heavyW, "Heavy + armorSum=2.0 → 4.0", ref failures, ref total);

            // ── Light 偏好：低护甲加分 + 高护甲减分（双向）──────────
            // armorSum=0 → (1-0)*1.5 - 0*0.5 = 1.5（无护甲最加分）
            CheckLayer(0f, 0f, ArmorPreference.Light, heavyW, lightMatchW, lightAvoidW, flexW,
                1.5f, "Light + armorSum=0 → 1.5 (max bonus)", ref failures, ref total);

            // armorSum=0.5 → (1-0.5)*1.5 - 0.5*0.5 = 0.75 - 0.25 = 0.5
            CheckLayer(0.5f, 0f, ArmorPreference.Light, heavyW, lightMatchW, lightAvoidW, flexW,
                0.5f, "Light + armorSum=0.5 → 0.5", ref failures, ref total);

            // armorSum=1.0 → (1-1)*1.5 - 1*0.5 = -0.5（满阈值时变为减分）
            CheckLayer(1.0f, 0f, ArmorPreference.Light, heavyW, lightMatchW, lightAvoidW, flexW,
                -0.5f, "Light + armorSum=1.0 → -0.5 (penalty)", ref failures, ref total);

            // armorSum=2.0 → (1-min(2,1))*1.5 - 2*0.5 = 0 - 1.0 = -1.0
            // 关键：armorSum 截断到 1.0，避免高护甲反而加分
            CheckLayer(1.5f, 0.5f, ArmorPreference.Light, heavyW, lightMatchW, lightAvoidW, flexW,
                -1.0f, "Light + armorSum=2.0 → -1.0 (armorSum capped at 1.0)", ref failures, ref total);

            // ── Flexible 偏好：线性加分，无偏置 ─────────────────────
            CheckLayer(1.0f, 0f, ArmorPreference.Flexible, heavyW, lightMatchW, lightAvoidW, flexW,
                1.0f * flexW, "Flexible + armorSum=1.0 → 1.0", ref failures, ref total);

            CheckLayer(0f, 0f, ArmorPreference.Flexible, heavyW, lightMatchW, lightAvoidW, flexW,
                0f, "Flexible + armorSum=0 → 0", ref failures, ref total);

            CheckLayer(0.8f, 0.7f, ArmorPreference.Flexible, heavyW, lightMatchW, lightAvoidW, flexW,
                1.5f * flexW, "Flexible + armorSum=1.5 → 1.5", ref failures, ref total);

            // ── 权重为 0：所有偏好都返回 0（玩家可关闭契合分）────────
            CheckLayer(1.0f, 1.0f, ArmorPreference.Heavy, 0f, 0f, 0f, 0f,
                0f, "all weights=0 + Heavy → 0", ref failures, ref total);
            CheckLayer(1.0f, 1.0f, ArmorPreference.Light, 0f, 0f, 0f, 0f,
                0f, "all weights=0 + Light → 0", ref failures, ref total);
            CheckLayer(1.0f, 1.0f, ArmorPreference.Flexible, 0f, 0f, 0f, 0f,
                0f, "all weights=0 + Flexible → 0", ref failures, ref total);

            Console.WriteLine($"[GearScorerTests/LayerMatch] {total - failures}/{total} passed");
            return failures;
        }

        // ════════════════════════════════════════════════════════════
        // 3. ComputeInsulationScoreCore：寒冷/炎热/舒适三档
        // ════════════════════════════════════════════════════════════

        private static int RunInsulationTests(ref int total)
        {
            int failures = 0;

            // 标准舒适区：15~25℃，tempMargin=5 → 寒冷阈值 10℃，炎热阈值 30℃
            const float comfortMin = 15f;
            const float comfortMax = 25f;
            const float margin = 5f;
            const float weight = 1.0f;

            // ── 寒冷：ambient < comfortMin - margin = 10 ────────────
            CheckInsulation(0f, comfortMin, comfortMax, 2.0f, 1.0f, weight, margin,
                2.0f, "ambient=0 < 10 → cold bonus", ref failures, ref total);

            CheckInsulation(9.9f, comfortMin, comfortMax, 2.0f, 1.0f, weight, margin,
                2.0f, "ambient=9.9 just below threshold → cold bonus", ref failures, ref total);

            // ── 寒冷边界：ambient = comfortMin - margin = 10 ─────────
            // 注意：条件是 strict < (不是 <=)，所以 ambient=10 不算寒冷
            CheckInsulation(10f, comfortMin, comfortMax, 2.0f, 1.0f, weight, margin,
                0f, "ambient=10 = threshold → NOT cold (strict <)", ref failures, ref total);

            // ── 炎热：ambient > comfortMax + margin = 30 ────────────
            CheckInsulation(40f, comfortMin, comfortMax, 2.0f, 1.0f, weight, margin,
                1.0f, "ambient=40 > 30 → heat bonus", ref failures, ref total);

            CheckInsulation(30.1f, comfortMin, comfortMax, 2.0f, 1.0f, weight, margin,
                1.0f, "ambient=30.1 just above threshold → heat bonus", ref failures, ref total);

            // ── 炎热边界：ambient = comfortMax + margin = 30 ─────────
            // 注意：条件是 strict > (不是 >=)，所以 ambient=30 不算炎热
            CheckInsulation(30f, comfortMin, comfortMax, 2.0f, 1.0f, weight, margin,
                0f, "ambient=30 = threshold → NOT heat (strict >)", ref failures, ref total);

            // ── 舒适区：10 < ambient < 30 ───────────────────────────
            CheckInsulation(20f, comfortMin, comfortMax, 2.0f, 1.0f, weight, margin,
                0f, "ambient=20 in comfort → 0", ref failures, ref total);

            CheckInsulation(15f, comfortMin, comfortMax, 2.0f, 1.0f, weight, margin,
                0f, "ambient=15 (comfortMin) → 0 (within margin)", ref failures, ref total);

            CheckInsulation(25f, comfortMin, comfortMax, 2.0f, 1.0f, weight, margin,
                0f, "ambient=25 (comfortMax) → 0 (within margin)", ref failures, ref total);

            // ── tempMargin=0：边界紧贴舒适区 ────────────────────────
            CheckInsulation(14.9f, 15f, 25f, 2.0f, 1.0f, weight, 0f,
                2.0f, "margin=0 + ambient=14.9 < 15 → cold bonus", ref failures, ref total);
            CheckInsulation(15f, 15f, 25f, 2.0f, 1.0f, weight, 0f,
                0f, "margin=0 + ambient=15 = comfortMin → 0 (strict <)", ref failures, ref total);

            // ── 权重为 0：即使极端温度也得 0 分 ─────────────────────
            CheckInsulation(-50f, comfortMin, comfortMax, 5.0f, 5.0f, 0f, margin,
                0f, "weight=0 + extreme cold → 0", ref failures, ref total);
            CheckInsulation(100f, comfortMin, comfortMax, 5.0f, 5.0f, 0f, margin,
                0f, "weight=0 + extreme heat → 0", ref failures, ref total);

            // ── 真实场景：寒冷环境，winter coat insulationCold=10, weight=1.0 ──
            CheckInsulation(-10f, comfortMin, comfortMax, 10f, 0f, weight, margin,
                10f, "real cold scenario → 10", ref failures, ref total);

            Console.WriteLine($"[GearScorerTests/Insulation] {total - failures}/{total} passed");
            return failures;
        }

        // ════════════════════════════════════════════════════════════
        // 4. ComputeMovementPenaltyCore：角色 → 权重映射
        // ════════════════════════════════════════════════════════════

        private static int RunMovementPenaltyTests(ref int total)
        {
            int failures = 0;

            // 默认权重（与 AESettings 默认值一致）
            const float workerW = 3.0f;
            const float backRowW = 2.0f;
            const float frontRowW = 0.5f;
            const float mass = 2.0f;

            // ── Worker/Doctor/Pacifist 共享 workerW（移动影响工作效率）──
            CheckMove(mass, Role.Worker, workerW, backRowW, frontRowW,
                mass * workerW, "Worker → workerW", ref failures, ref total);
            CheckMove(mass, Role.Doctor, workerW, backRowW, frontRowW,
                mass * workerW, "Doctor → workerW (shared)", ref failures, ref total);
            CheckMove(mass, Role.Pacifist, workerW, backRowW, frontRowW,
                mass * workerW, "Pacifist → workerW (shared)", ref failures, ref total);

            // ── Shooter/Hunter 共享 backRowW（移动影响走位）──────────
            CheckMove(mass, Role.Shooter, workerW, backRowW, frontRowW,
                mass * backRowW, "Shooter → backRowW", ref failures, ref total);
            CheckMove(mass, Role.Hunter, workerW, backRowW, frontRowW,
                mass * backRowW, "Hunter → backRowW (shared)", ref failures, ref total);

            // ── 其他（Brawler/Leader/Default）用 frontRowW（前排容忍度高）──
            CheckMove(mass, Role.Brawler, workerW, backRowW, frontRowW,
                mass * frontRowW, "Brawler → frontRowW", ref failures, ref total);
            CheckMove(mass, Role.Leader, workerW, backRowW, frontRowW,
                mass * frontRowW, "Leader → frontRowW", ref failures, ref total);
            CheckMove(mass, Role.Default, workerW, backRowW, frontRowW,
                mass * frontRowW, "Default → frontRowW", ref failures, ref total);

            // ── 真实场景：重甲 mass=4.0 ─────────────────────────────
            // Worker 穿重甲：4*3 = 12（强惩罚，工人不该穿重甲）
            // Brawler 穿重甲：4*0.5 = 2（轻惩罚，前排适合）
            CheckMove(4.0f, Role.Worker, workerW, backRowW, frontRowW,
                12.0f, "Worker + heavy armor (mass=4) → 12 (strong penalty)", ref failures, ref total);
            CheckMove(4.0f, Role.Brawler, workerW, backRowW, frontRowW,
                2.0f, "Brawler + heavy armor (mass=4) → 2 (mild penalty)", ref failures, ref total);

            // ── 边界：mass=0 → 0 ────────────────────────────────────
            CheckMove(0f, Role.Worker, workerW, backRowW, frontRowW,
                0f, "mass=0 → 0", ref failures, ref total);
            CheckMove(0f, Role.Brawler, workerW, backRowW, frontRowW,
                0f, "mass=0 + Brawler → 0", ref failures, ref total);

            // ── 边界：权重为 0 → 0（玩家可关闭移动减损）──────────────
            CheckMove(mass, Role.Worker, 0f, 0f, 0f,
                0f, "all weights=0 → 0", ref failures, ref total);

            // ── 真实轻甲 mass=0.3 ───────────────────────────────────
            // Worker 穿轻甲：0.3*3 = 0.9
            CheckMove(0.3f, Role.Worker, workerW, backRowW, frontRowW,
                0.9f, "Worker + light armor (mass=0.3) → 0.9", ref failures, ref total);

            Console.WriteLine($"[GearScorerTests/MovementPenalty] {total - failures}/{total} passed");
            return failures;
        }

        // ════════════════════════════════════════════════════════════
        // 辅助方法
        // ════════════════════════════════════════════════════════════

        private static void CheckArmor(float sharp, float blunt, float heat, float weight,
            float expected, string label, ref int failures, ref int total)
        {
            total++;
            float actual = GearScorer.ComputeArmorScoreCore(sharp, blunt, heat, weight);
            if (System.Math.Abs(actual - expected) > Epsilon)
            {
                Console.WriteLine($"  FAIL: {label}: expected {expected}, got {actual}");
                failures++;
            }
        }

        private static void CheckLayer(float sharp, float blunt, ArmorPreference pref,
            float heavyW, float lightMatchW, float lightAvoidW, float flexW,
            float expected, string label, ref int failures, ref int total)
        {
            total++;
            float actual = GearScorer.ComputeLayerMatchScoreCore(sharp, blunt, pref,
                heavyW, lightMatchW, lightAvoidW, flexW);
            if (System.Math.Abs(actual - expected) > Epsilon)
            {
                Console.WriteLine($"  FAIL: {label}: expected {expected}, got {actual}");
                failures++;
            }
        }

        private static void CheckInsulation(float ambient, float comfortMin, float comfortMax,
            float insulationCold, float insulationHeat, float weight, float tempMargin,
            float expected, string label, ref int failures, ref int total)
        {
            total++;
            float actual = GearScorer.ComputeInsulationScoreCore(ambient, comfortMin, comfortMax,
                insulationCold, insulationHeat, weight, tempMargin);
            if (System.Math.Abs(actual - expected) > Epsilon)
            {
                Console.WriteLine($"  FAIL: {label}: expected {expected}, got {actual}");
                failures++;
            }
        }

        private static void CheckMove(float mass, Role role,
            float workerW, float backRowW, float frontRowW,
            float expected, string label, ref int failures, ref int total)
        {
            total++;
            float actual = GearScorer.ComputeMovementPenaltyCore(mass, role,
                workerW, backRowW, frontRowW);
            if (System.Math.Abs(actual - expected) > Epsilon)
            {
                Console.WriteLine($"  FAIL: {label}: expected {expected}, got {actual}");
                failures++;
            }
        }
    }
}
