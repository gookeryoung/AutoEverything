namespace AutoEquipment.Scoring
{
    /// <summary>
    /// 全局装备预设方案枚举。
    /// 每个方案对应一组 GearWeights 默认值，玩家可在设置界面切换。
    /// </summary>
    public enum GearPreset
    {
        /// <summary>标准：平衡护甲/DPS/工作</summary>
        Standard,

        /// <summary>激战：护甲与 DPS 优先，移速次要</summary>
        Aggressive,

        /// <summary>经济：耐久与品质优先，避免浪费高级材料</summary>
        Economic,

        /// <summary>狩猎：远程射程与精准优先</summary>
        Hunting
    }

    /// <summary>
    /// 预设方案工具类：提供方案与权重的映射。
    /// </summary>
    public static class GearPresetExtensions
    {
        /// <summary>
        /// 获取预设方案对应的默认权重。
        /// </summary>
        public static GearWeights GetDefaultWeights(this GearPreset preset)
        {
            switch (preset)
            {
                case GearPreset.Aggressive: return GearWeights.Aggressive;
                case GearPreset.Economic: return GearWeights.Economic;
                case GearPreset.Hunting: return GearWeights.Hunting;
                default: return GearWeights.Standard;
            }
        }
    }
}
