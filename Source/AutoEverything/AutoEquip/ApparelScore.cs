using System;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.AutoEquip
{
    /// <summary>服装档案：决定评分权重与绝缘取向。</summary>
    public enum ApparelProfile
    {
        Work = 0,
        Combat = 1,
        Cold = 2,
        Hot = 3
    }

    /// <summary>玩家可选的评分预设（只调权重取向，不暴露单个权重）。</summary>
    public enum ApparelPreset
    {
        Balanced = 0,
        Heavy = 1,
        Light = 2
    }

    /// <summary>
    /// 服装评分权重（纯数据）。默认值保证每个档案下五项权重之和为 1，
    /// 便于把分数看成"满分 100 的百分制"。
    /// </summary>
    public struct ApparelWeights
    {
        public float Armor;
        public float Comfort;
        public float Mobility;
        public float Work;
        public float Durability;

        public static ApparelWeights For(ApparelPreset preset, ApparelProfile profile)
        {
            ApparelWeights w;
            switch (profile)
            {
                case ApparelProfile.Combat:
                    w = new ApparelWeights { Armor = 0.70f, Comfort = 0.05f, Mobility = 0.20f, Work = 0f, Durability = 0.05f };
                    break;
                case ApparelProfile.Cold:
                case ApparelProfile.Hot:
                    w = new ApparelWeights { Armor = 0.35f, Comfort = 0.45f, Mobility = 0.15f, Work = 0f, Durability = 0.05f };
                    break;
                default:
                    w = new ApparelWeights { Armor = 0.40f, Comfort = 0.10f, Mobility = 0.25f, Work = 0.20f, Durability = 0.05f };
                    break;
            }

            if (preset == ApparelPreset.Heavy)
            {
                w.Armor += 0.15f;
                w.Comfort -= 0.05f;
                w.Mobility -= 0.05f;
                w.Work -= 0.05f;
            }
            else if (preset == ApparelPreset.Light)
            {
                w.Armor -= 0.15f;
                w.Comfort += 0.05f;
                w.Mobility += 0.10f;
                w.Work += 0.10f;
            }

            w.Armor = Math.Max(0f, w.Armor);
            w.Comfort = Math.Max(0f, w.Comfort);
            w.Mobility = Math.Max(0f, w.Mobility);
            w.Work = Math.Max(0f, w.Work);
            w.Durability = Math.Max(0f, w.Durability);

            // 归一化到 1，保证预设改动只调取向、不改变总分尺度（便于沿用同一百分比阈值）。
            float sum = w.Armor + w.Comfort + w.Mobility + w.Work + w.Durability;
            if (sum > 0.0001f)
            {
                w.Armor /= sum;
                w.Comfort /= sum;
                w.Mobility /= sum;
                w.Work /= sum;
                w.Durability /= sum;
            }
            return w;
        }
    }

    /// <summary>
    /// 整套服装的评分输入（纯数据，不依赖 RimWorld，便于单元测试）。
    /// 护甲值已按「部位覆盖率 × 各层串行结算」折算成期望剩余伤害比例：
    /// 0 表示完全挡住，1 表示完全没有护甲。
    /// </summary>
    public struct ApparelSetInput
    {
        public ApparelProfile Profile;
        public float SharpSurvival;
        public float BluntSurvival;
        public float HeatSurvival;
        public float InsulationCold;
        public float InsulationHeat;
        public float MoveSpeedOffset;
        public float WorkSpeedOffset;
        public float Durability;
        public int ConflictDropCount;
        public float NeedCold;
        public float NeedHeat;
    }

    /// <summary>
    /// 服装评分内核（纯函数）。
    ///
    /// 与原版结算口径对齐（B0 反编译结论）：
    /// - 护甲按命中部位逐层串行结算，单层期望剩余伤害见 <see cref="LayerSurvival"/>；
    /// - 覆盖率用 <c>BodyPartRecord.coverageAbs</c> 加权，由运行时层折算进三个 Survival 值；
    /// - 品质已体现在护甲/绝缘数值与 MaxHitPoints 上，这里只保留耐久比例，不再额外加品质分。
    /// </summary>
    public static class ApparelScoreCore
    {
        public const float MaxArmorRating = 2f;
        public const float ColdInsulationScale = 40f;
        public const float HeatInsulationScale = 25f;
        public const float PenaltyScale = 0.3f;
        public const float DropPenalty = 3f;

        /// <summary>
        /// 单层护甲的期望剩余伤害比例（穿透按 0 计）。
        /// a = clamp(rating, 0, 2)：a&lt;=1 时 1-0.75a；a&gt;1 时 0.5-0.25a。
        /// </summary>
        public static float LayerSurvival(float armorRating)
        {
            float a = armorRating;
            if (a <= 0f) return 1f;
            if (a > MaxArmorRating) a = MaxArmorRating;
            if (a <= 1f) return 1f - 0.75f * a;
            return 0.5f - 0.25f * a;
        }

        /// <summary>护甲分量 0..1，1 = 完全免疫伤害。</summary>
        public static float ArmorComponent(ApparelSetInput set, RoleGroup role)
        {
            float sharpWeight;
            float bluntWeight;
            float heatWeight;
            switch (role)
            {
                case RoleGroup.Front:
                    sharpWeight = 1.0f;
                    bluntWeight = 0.7f;
                    heatWeight = 0.3f;
                    break;
                case RoleGroup.Shooter:
                    sharpWeight = 0.6f;
                    bluntWeight = 0.4f;
                    heatWeight = 0.3f;
                    break;
                case RoleGroup.Worker:
                    sharpWeight = 0.4f;
                    bluntWeight = 0.25f;
                    heatWeight = 0.2f;
                    break;
                case RoleGroup.Trade:
                    sharpWeight = 0.45f;
                    bluntWeight = 0.3f;
                    heatWeight = 0.2f;
                    break;
                default:
                    sharpWeight = 0.5f;
                    bluntWeight = 0.35f;
                    heatWeight = 0.25f;
                    break;
            }

            float total = sharpWeight + bluntWeight + heatWeight;
            float remaining = (Clamp01(set.SharpSurvival) * sharpWeight
                + Clamp01(set.BluntSurvival) * bluntWeight
                + Clamp01(set.HeatSurvival) * heatWeight) / total;
            return Clamp01(1f - remaining);
        }

        /// <summary>
        /// 温度舒适分量 0..1。
        /// 严寒/酷暑档案只看对应绝缘；工作/战斗档案只在环境偏离舒适区间时按缺口加权，
        /// 温和天气下绝缘不参与评分（避免"越厚越好"）。
        /// </summary>
        public static float ComfortComponent(ApparelSetInput set)
        {
            float coldFit = Clamp01(set.InsulationCold / ColdInsulationScale);
            float heatFit = Clamp01(set.InsulationHeat / HeatInsulationScale);

            switch (set.Profile)
            {
                case ApparelProfile.Cold:
                    return coldFit;
                case ApparelProfile.Hot:
                    return heatFit;
                default:
                    float needCold = Clamp01(set.NeedCold);
                    float needHeat = Clamp01(set.NeedHeat);
                    float need = needCold + needHeat;
                    if (need <= 0.0001f) return 0f;
                    return (coldFit * needCold + heatFit * needHeat) / need;
            }
        }

        public static float MobilityComponent(ApparelSetInput set)
        {
            return Clamp01(-set.MoveSpeedOffset / PenaltyScale);
        }

        public static float WorkComponent(ApparelSetInput set)
        {
            return Clamp01(-set.WorkSpeedOffset / PenaltyScale);
        }

        /// <summary>逐项分量（0..1），用于日志解释"为什么换"。</summary>
        public static void Breakdown(ApparelSetInput set, ApparelWeights weights, RoleGroup role,
            out float armor, out float comfort, out float mobility, out float work, out float durability)
        {
            armor = ArmorComponent(set, role);
            comfort = ComfortComponent(set);
            mobility = MobilityComponent(set);
            work = WorkComponent(set);
            durability = Clamp01(set.Durability);
        }

        /// <summary>百分制总分：五项加权后 ×100，再扣冲突丢弃成本。</summary>
        public static float Evaluate(ApparelSetInput set, ApparelWeights weights, RoleGroup role)
        {
            float armor;
            float comfort;
            float mobility;
            float work;
            float durability;
            Breakdown(set, weights, role, out armor, out comfort, out mobility, out work, out durability);

            float score = 100f * (weights.Armor * armor
                + weights.Comfort * comfort
                - weights.Mobility * mobility
                - weights.Work * work
                + weights.Durability * durability);
            score -= DropPenalty * Math.Max(0, set.ConflictDropCount);
            return score;
        }

        /// <summary>
        /// 由舒适温度区间与当前环境温度推算"缺多少冷/热绝缘"（0..1）。
        /// 偏离 20 度即视为满需求。
        /// </summary>
        public static void TemperatureNeed(float comfortMin, float comfortMax, float ambient,
            out float needCold, out float needHeat)
        {
            needCold = Clamp01((comfortMin - ambient) / 20f);
            needHeat = Clamp01((ambient - comfortMax) / 20f);
        }

        public static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
