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
            return ComputeArmorScoreCore(sharp, blunt, heat, AESettings.geArmorWeight);
        }

        /// <summary>
        /// 护甲分纯逻辑核心：三项平均 × 权重。
        /// 抽出便于单元测试，不依赖 Apparel 实例。
        /// 用 0.3333f 而非 /3f：避免整数除法风险，与历史实现一致。
        /// </summary>
        internal static float ComputeArmorScoreCore(float sharp, float blunt, float heat, float weight)
        {
            // 三项平均：避免单项极值（如纯防弹衣 Sharp 高 Blunt 低）压倒其他维度
            float avg = (sharp + blunt + heat) * 0.3333f;
            return avg * weight;
        }

        /// <summary>
        /// 定位契合分：根据角色定位与护甲偏好给 apparel 加分或减分。
        /// - Heavy（前排 Brawler）：高护甲 apparel 加分，低护甲减分
        /// - Light（工人/医生/Pacifist）：低护甲 apparel 加分（保持移动效率），重甲减分
        /// - Flexible（后排 Shooter/Hunter/Leader）：按护甲值线性加分，无偏置
        ///
        /// 头盔层（Overhead）特殊规则：Light 偏好降级为 Flexible 评分。
        /// 根因：头盔核心价值是护甲，mass 普遍 0.3kg 左右，移动效率差异微乎其微；
        ///       Light 公式 (1-armorSum)*1.5 - armorSum*0.5 在 armorSum=0.2~0.3 时
        ///       让低护甲头盔反而得分更高（简易头盔胜过斥候头盔），与头盔价值相悖。
        /// </summary>
        private static float ComputeLayerMatchScore(Apparel apparel, Role role, ArmorPreference armorPref)
        {
            // 用 apparel 的"护甲总量"作为重甲/轻甲的判据（Sharp+Blunt）
            float sharp = apparel.GetStatValue(StatDefOf.ArmorRating_Sharp);
            float blunt = apparel.GetStatValue(StatDefOf.ArmorRating_Blunt);
            ArmorPreference effectivePref = ResolveEffectivePref(IsHeadwear(apparel), armorPref);
            return ComputeLayerMatchScoreCore(sharp, blunt, effectivePref,
                AESettings.geHeavyArmorMatchWeight,
                AESettings.geLightArmorMatchWeight,
                AESettings.geLightArmorAvoidWeight,
                AESettings.geFlexibleArmorMatchWeight);
        }

        /// <summary>
        /// 判断 apparel 是否属于头盔层（Overhead）。
        /// 用于 Light 偏好降级规则：头盔对 Worker 也按护甲线性加分。
        /// </summary>
        private static bool IsHeadwear(Apparel apparel)
        {
            ApparelProperties props = apparel?.def?.apparel;
            if (props == null || props.layers == null) return false;
            for (int i = 0; i < props.layers.Count; i++)
            {
                if (props.layers[i] == ApparelLayerDefOf.Overhead) return true;
            }
            return false;
        }

        /// <summary>
        /// 解析有效护甲偏好：头盔层对 Light 偏好降级为 Flexible。
        /// 纯逻辑方法，便于单元测试，不依赖 Apparel 实例。
        ///
        /// 算法：isHeadwear && basePref == Light → Flexible；否则原样返回。
        /// Heavy/Flexible 不受影响（Heavy 仍强制重甲契合，Flexible 仍线性加分）。
        /// </summary>
        internal static ArmorPreference ResolveEffectivePref(bool isHeadwear, ArmorPreference basePref)
        {
            return (isHeadwear && basePref == ArmorPreference.Light) ? ArmorPreference.Flexible : basePref;
        }

        /// <summary>
        /// 定位契合分纯逻辑核心：按护甲偏好分支计算。
        /// 抽出便于单元测试，不依赖 Apparel 实例。
        ///
        /// 算法：
        /// - Heavy:  armorSum × geHeavyMatchW（线性加分，护甲越高越好）
        /// - Light:  (1 - min(armorSum,1)) × geLightMatchW - armorSum × geLightAvoidW
        ///           （低护甲加分 + 高护甲减分，工人保移动效率）
        /// - Flexible: armorSum × geFlexibleMatchW（线性加分，无偏置）
        /// </summary>
        internal static float ComputeLayerMatchScoreCore(
            float sharp, float blunt, ArmorPreference armorPref,
            float heavyMatchW, float lightMatchW, float lightAvoidW, float flexibleMatchW)
        {
            float armorSum = sharp + blunt;

            switch (armorPref)
            {
                case ArmorPreference.Heavy:
                    return armorSum * heavyMatchW;
                case ArmorPreference.Light:
                    return (1.0f - System.Math.Min(armorSum, 1.0f)) * lightMatchW
                         - armorSum * lightAvoidW;
                default: // Flexible
                    return armorSum * flexibleMatchW;
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

            return ComputeInsulationScoreCore(ambient, comfort.min, comfort.max,
                insulationCold, insulationHeat, AESettings.geInsulationWeight, tempMargin);
        }

        /// <summary>
        /// 保暖隔热分纯逻辑核心：按环境温度分支计算。
        /// 抽出便于单元测试，不依赖 Pawn/Apparel 实例。
        ///
        /// 算法（tempMargin 为危险余量，与 GearContext 一致）：
        /// - ambient &lt; comfortMin - tempMargin: 寒冷 → insulationCold × weight
        /// - ambient &gt; comfortMax + tempMargin: 炎热 → insulationHeat × weight
        /// - 其他: 舒适 → 0
        /// </summary>
        internal static float ComputeInsulationScoreCore(
            float ambient, float comfortMin, float comfortMax,
            float insulationCold, float insulationHeat, float weight, float tempMargin)
        {
            if (ambient < comfortMin - tempMargin)
            {
                return insulationCold * weight;
            }
            if (ambient > comfortMax + tempMargin)
            {
                return insulationHeat * weight;
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
            float mass = apparel.GetStatValue(StatDefOf.Mass);
            return ComputeMovementPenaltyCore(mass, role,
                AESettings.geWorkerMovePenaltyWeight,
                AESettings.geBackRowMovePenaltyWeight,
                AESettings.geFrontRowMovePenaltyWeight);
        }

        /// <summary>
        /// 移动减损纯逻辑核心：按角色定位选择权重，penalty = mass × weight。
        /// 抽出便于单元测试，不依赖 Apparel 实例。
        ///
        /// 角色映射：
        /// - Worker/Doctor/Pacifist → workerW（移动影响工作效率，惩罚最大）
        /// - Shooter/Hunter → backRowW（移动影响走位，中等惩罚）
        /// - 其他（Brawler/Leader/Default）→ frontRowW（前排容忍度高，惩罚最小）
        /// </summary>
        internal static float ComputeMovementPenaltyCore(
            float mass, Role role,
            float workerW, float backRowW, float frontRowW)
        {
            switch (role)
            {
                case Role.Worker:
                case Role.Doctor:
                case Role.Pacifist:
                    return mass * workerW;
                case Role.Shooter:
                case Role.Hunter:
                    return mass * backRowW;
                default:
                    return mass * frontRowW;
            }
        }
    }
}
