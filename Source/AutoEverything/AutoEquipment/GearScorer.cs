using RimWorld;
using Verse;
using AutoEverything.Core;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.AutoEquipment
{
    /// <summary>
    /// 装备评分器：计算 (Pawn × Apparel) 组合的契合度分数。
    ///
    /// 评分公式（5 项加权求和，权重可由玩家在 Mod 选项调整）：
    ///   score = armorScore          // 护甲值（Sharp+Blunt+Heat 归一化）
    ///         + layerMatchScore     // 定位契合（前排重甲+，工人轻甲+，后排均衡）
    ///         + cultureScore        // 文化/心情影响（CultureChecker）
    ///         + insulationScore     // 保暖/隔热契合当前环境
    ///         - movementPenalty     // 移动速度减损（工人/后排更敏感）
    ///
    /// 设计要点：
    /// - 命中自定义评级或高评级的 Pawn 优先选优质装备（由 GearAllocator 按 CombatTier 降序排分配）
    /// - 定位契合：Brawler→Heavy 加分、Worker→Light 加分、Shooter→Flexible 加分
    /// - X 档（禁止暴力）：GearAllocator 直接跳过，不进入评分
    /// </summary>
    internal static class GearScorer
    {
        /// <summary>
        /// 计算 apparel 对 pawn 的综合契合分。
        /// 高分 = 更适合该 pawn。
        /// </summary>
        public static float ComputeScore(Pawn pawn, Apparel apparel, Role role, ArmorPreference armorPref)
        {
            if (pawn == null || apparel == null) return float.MinValue;

            float score = 0f;

            // 1. 护甲值：Sharp + Blunt + Heat 归一化
            score += ComputeArmorScore(apparel);

            // 2. 定位契合：根据护甲偏好重甲/轻甲/自由加分
            score += ComputeLayerMatchScore(apparel, role, armorPref);

            // 3. 文化/心情影响
            score += CultureChecker.GetCultureScore(pawn, apparel);

            // 4. 保暖/隔热契合（极端温度时考虑）
            score += ComputeInsulationScore(pawn, apparel);

            // 5. 移动速度减损（工人与后排更敏感）
            score -= ComputeMovementPenalty(apparel, role);

            return score;
        }

        /// <summary>
        /// 护甲值得分：Sharp + Blunt + Heat 三项平均，乘以整体权重。
        /// 设计：护甲是装备核心价值，权重最高（默认 1.0）。
        /// </summary>
        private static float ComputeArmorScore(Apparel apparel)
        {
            float sharp = apparel.GetStatValue(StatDefOf.ArmorRating_Sharp);
            float blunt = apparel.GetStatValue(StatDefOf.ArmorRating_Blunt);
            float heat = apparel.GetStatValue(StatDefOf.ArmorRating_Heat);
            // 三项平均：避免单项极值（如纯防弹衣 Sharp 高 Blunt 低）压倒其他维度
            float avg = (sharp + blunt + heat) * 0.3333f;
            return avg * AESettings.geArmorWeight;
        }

        /// <summary>
        /// 定位契合分：根据角色定位与护甲偏好给 apparel 加分或减分。
        /// - Heavy（前排 Brawler）：高护甲 apparel 加分，低护甲减分
        /// - Light（工人/医生/Pacifist）：低护甲 apparel 加分（保持移动效率），重甲减分
        /// - Flexible（后排 Shooter/Hunter/Leader）：按护甲值线性加分，无偏置
        /// </summary>
        private static float ComputeLayerMatchScore(Apparel apparel, Role role, ArmorPreference armorPref)
        {
            // 用 apparel 的"护甲总量"作为重甲/轻甲的判据（Sharp+Blunt）
            float sharp = apparel.GetStatValue(StatDefOf.ArmorRating_Sharp);
            float blunt = apparel.GetStatValue(StatDefOf.ArmorRating_Blunt);
            float armorSum = sharp + blunt;

            switch (armorPref)
            {
                case ArmorPreference.Heavy:
                    // 前排：护甲越高越好，线性加分
                    return armorSum * AESettings.geHeavyArmorMatchWeight;
                case ArmorPreference.Light:
                    // 工人：低护甲加分（保持工作效率），高护甲减分
                    // armorSum 通常 0~2.0，越低加分越多
                    return (1.0f - System.Math.Min(armorSum, 1.0f)) * AESettings.geLightArmorMatchWeight
                         - armorSum * AESettings.geLightArmorAvoidWeight;
                default: // Flexible
                    // 后排：按护甲值线性加分，无偏置
                    return armorSum * AESettings.geFlexibleArmorMatchWeight;
            }
        }

        /// <summary>
        /// 保暖/隔热契合分：根据 pawn 当前环境温度给 apparel 的保暖/隔热值加分。
        /// - 寒冷环境（AmbientTemperature < ComfortableTemperatureRange.min）：保暖值加分
        /// - 炎热环境（AmbientTemperature > ComfortableTemperatureRange.max）：隔热值加分
        /// - 舒适环境：不加分
        /// </summary>
        private static float ComputeInsulationScore(Pawn pawn, Apparel apparel)
        {
            if (pawn.Map == null) return 0f;

            float ambient = pawn.AmbientTemperature;
            FloatRange comfort = pawn.ComfortableTemperatureRange();
            float tempMargin = 5f; // 与 GearContext 一致的危险余量

            float insulationCold = apparel.GetStatValue(StatDefOf.Insulation_Cold);
            float insulationHeat = apparel.GetStatValue(StatDefOf.Insulation_Heat);

            if (ambient < comfort.min - tempMargin)
            {
                // 寒冷：保暖值加分
                return insulationCold * AESettings.geInsulationWeight;
            }
            if (ambient > comfort.max + tempMargin)
            {
                // 炎热：隔热值加分
                return insulationHeat * AESettings.geInsulationWeight;
            }
            return 0f;
        }

        /// <summary>
        /// 移动速度减损惩罚：用 apparel 的 Mass（重量）作为代理指标，重 apparel 对工人/后排惩罚更大。
        /// 设计：RimWorld 1.6 中 apparel 没有直接的 MoveSpeedFactor stat，但重甲的 Mass 通常更大
        ///       （轻甲 0.2~0.5kg、重甲 2~5kg），用 Mass 作为减损指标近似合理。
        /// 工人对移动速度敏感（搬运/建造效率），前排战士容忍度高。
        /// </summary>
        private static float ComputeMovementPenalty(Apparel apparel, Role role)
        {
            // Mass 默认 0.1~5.0 kg，归一化为减损比例
            // penalty = mass * weight，重甲 mass=4 → penalty=4*weight
            float mass = apparel.GetStatValue(StatDefOf.Mass);

            switch (role)
            {
                case Role.Worker:
                case Role.Doctor:
                case Role.Pacifist:
                    // 工人/医疗：移动速度影响工作效率，惩罚放大
                    return mass * AESettings.geWorkerMovePenaltyWeight;
                case Role.Shooter:
                case Role.Hunter:
                    // 后排：移动速度影响走位，中等惩罚
                    return mass * AESettings.geBackRowMovePenaltyWeight;
                default:
                    // 前排/领袖：容忍度高，轻微惩罚
                    return mass * AESettings.geFrontRowMovePenaltyWeight;
            }
        }
    }
}
