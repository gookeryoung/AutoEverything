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
        public bool IsSpecial;      // 带“战斗相关”WeaponTraitDef 的独特/人格武器
        public float TraitValue;    // 仅战斗特性的 Σ marketValueOffset（装饰特性不计）
        public float CombatScore;   // 按角色加权的战斗评分
        public int QualityRank;     // QualityCategory 序号，无品质按 Normal
        public int TechRank;        // TechLevel 枚举值，用于避免新石器匕首/弓压过枪械
    }

    /// <summary>
    /// 装备优先级比较器（纯函数）。
    ///
    /// 用户明确要求：
    /// 1. 特化武器 &gt; 一般武器（特化 = 带战斗相关特性的独特/人格武器）；
    /// 2. 同为特化武器时，特性好 &gt; 特性差，特性好坏按特性影响的价格（marketValueOffset）计算；
    /// 3. 同为一般武器时，科技等级高者优先，再比较战斗评分，品质只作近似兜底。
    ///
    /// 科技等级优先是为了避免“传奇短弓/匕首压过普通枪械”：品质已经通过
    /// StatDef 计入战斗评分，再把它提到主排序会造成重复加权。
    /// 市价不参与排序与换装阈值：翡翠/黄金等高价材质只抬售价、不代表战斗力。
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
                // 一般武器先比科技等级：工业枪械不应被新石器匕首/弓压过
                int tech = b.TechRank.CompareTo(a.TechRank);
                if (tech != 0) return tech;
            }

            int combat = b.CombatScore.CompareTo(a.CombatScore);
            if (combat != 0) return combat;

            int quality = b.QualityRank.CompareTo(a.QualityRank);
            if (quality != 0) return quality;

            // 最后用 defName 保证排序稳定，避免同分候选项在不同帧来回切换
            return string.CompareOrdinal(a.DefName, b.DefName);
        }

        /// <summary>
        /// 武器是否值得换装。判断口径与 <see cref="CompareWeapon"/> 保持一致：
        /// - 特化与一般互斥时，只允许升级为特化；
        /// - 同为目标特化时，特性价值更高直接换，价值相同再按战斗评分过阈值；
        /// - 一般武器科技更高直接换、科技更低不换，同科技才要求战斗评分达到最小提升比例；
        /// - 当前没有武器时，任意有效候选都可换。
        /// </summary>
        public static bool ShouldSwapWeapon(WeaponScoreInput current, WeaponScoreInput candidate,
            float minImprovementPct)
        {
            if (candidate.CombatScore <= 0f && !candidate.IsSpecial) return false;

            if (candidate.IsSpecial != current.IsSpecial)
                return candidate.IsSpecial;

            if (candidate.IsSpecial)
            {
                if (candidate.TraitValue != current.TraitValue)
                    return candidate.TraitValue > current.TraitValue;
                return ShouldSwap(current.CombatScore, candidate.CombatScore, minImprovementPct);
            }

            if (candidate.TechRank != current.TechRank)
                return candidate.TechRank > current.TechRank;
            return ShouldSwap(current.CombatScore, candidate.CombatScore, minImprovementPct);
        }

        /// <summary>
        /// 是否值得换装：候选分数必须超过当前分数的最小提升比例。
        /// 当前没有分数时，只要候选分数为正就换。
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

        /// <summary>
        /// 任务临时成员（难民/逃兵等 quest lodger）是否跳过自动装备。
        /// 他们离开殖民地时会带走身上的装备，因此默认不允许；
        /// 玩家显式开启后才与正式殖民者同样处理。
        /// </summary>
        public static bool ShouldSkipQuestLodger(bool isQuestLodger, bool allowQuestLodgers)
        {
            return isQuestLodger && !allowQuestLodgers;
        }
    }
}
