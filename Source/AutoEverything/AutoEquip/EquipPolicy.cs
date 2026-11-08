using System;

namespace AutoEverything.AutoEquip
{
    /// <summary>
    /// 武器评分输入（纯数据，便于单元测试）。
    /// 数值由运行时 <see cref="WeaponScorer"/> 计算，比较规则集中在本类。
    /// </summary>
    public struct WeaponScoreInput
    {
        public string DefName;
        public bool IsSpecial;      // 带 WeaponTraitDef 特性的独特/人格武器
        public float TraitValue;    // Σ weaponTrait.marketValueOffset
        public float CombatScore;   // 按角色加权的战斗评分
        public int QualityRank;     // QualityCategory 序号，无品质按 Normal
        public float MarketValue;   // 含品质与特性的市价
    }

    /// <summary>
    /// 装备优先级比较器（纯函数）。
    ///
    /// 用户明确要求：
    /// 1. 特化武器 &gt; 一般武器；
    /// 2. 同为特化武器时，特性好 &gt; 特性差，特性好坏按特性影响的价格（marketValueOffset）计算；
    /// 3. 同为一般武器时，品质优先，再比较战斗评分。
    ///
    /// 用分层比较而非加权总分，保证排序结果可解释、不会因权重调参而漂移。
    /// </summary>
    public static class EquipPolicy
    {
        /// <summary>返回 &lt;0 表示 a 优于 b；&gt;0 表示 b 优于 a；0 表示完全同分。</summary>
        public static int CompareWeapon(WeaponScoreInput a, WeaponScoreInput b)
        {
            if (a.IsSpecial != b.IsSpecial)
                return a.IsSpecial ? -1 : 1;

            if (a.IsSpecial)
            {
                int trait = b.TraitValue.CompareTo(a.TraitValue);
                if (trait != 0) return trait;
            }
            else
            {
                int quality = b.QualityRank.CompareTo(a.QualityRank);
                if (quality != 0) return quality;
            }

            int combat = b.CombatScore.CompareTo(a.CombatScore);
            if (combat != 0) return combat;

            int market = b.MarketValue.CompareTo(a.MarketValue);
            if (market != 0) return market;

            // 最后用 defName 保证排序稳定，避免同分候选项在不同帧来回切换
            return string.CompareOrdinal(a.DefName, b.DefName);
        }

        /// <summary>
        /// 用于换装阈值的可比分数：特化武器加一个远大于普通武器的基数，
        /// 其余分量按重要性叠加。只用于“是否值得换”，不参与精确排序。
        /// </summary>
        public static float ComparableScore(WeaponScoreInput value)
        {
            float score = value.CombatScore + value.QualityRank * 10f + value.MarketValue * 0.01f;
            if (value.IsSpecial) score += 1000000f + value.TraitValue * 100f;
            return score;
        }

        /// <summary>
        /// 是否值得换装：候选可比分数必须超过当前分数的最小提升比例。
        /// 当前没有武器时，只要候选分数为正就换。
        /// </summary>
        public static bool ShouldSwap(float currentScore, float candidateScore, float minImprovementPct)
        {
            if (candidateScore <= 0f) return false;
            if (currentScore <= 0f) return true;
            float required = currentScore * (1f + Math.Max(0f, minImprovementPct) / 100f);
            return candidateScore > required;
        }

        /// <summary>服装评分比较：分数高者优先，同分保留当前衣物。</summary>
        public static bool IsBetterApparel(float candidateScore, float currentScore, float minImprovementPct)
        {
            return ShouldSwap(currentScore, candidateScore, minImprovementPct);
        }
    }
}
