namespace AutoEquipment.Scoring
{
    /// <summary>
    /// 评分权重数据结构：玩家可通过设置界面调整的部分权重。
    /// 特质加分（如格斗者-9000、嗜血+30）为硬编码，不数据化——
    /// 因为这些是特质的设计语义，不应让玩家随意修改。
    /// </summary>
    public struct GearWeights
    {
        // ===================== 武器权重 =====================

        /// <summary>技能每级权重（基础）</summary>
        public float w_skill;

        /// <summary>单火兴趣乘数</summary>
        public float w_passionMinor;

        /// <summary>双火兴趣乘数</summary>
        public float w_passionMajor;

        /// <summary>近战 DPS 权重</summary>
        public float w_dps;

        /// <summary>远程伤害倍率权重</summary>
        public float w_dmg;

        /// <summary>远程冷却权重（倒数）</summary>
        public float w_cooldown;

        /// <summary>射程权重</summary>
        public float w_range;

        /// <summary>品质权重</summary>
        public float w_quality;

        // ===================== 防具权重 =====================

        /// <summary>护甲权重</summary>
        public float w_armor;

        /// <summary>保温权重</summary>
        public float w_insulation;

        /// <summary>移速权重</summary>
        public float w_movespeed;

        /// <summary>工作速度权重</summary>
        public float w_workspeed;

        // ===================== 阈值 =====================

        /// <summary>升级阈值：评分提升比例需超过此值才换装</summary>
        public float upgradeThreshold;

        /// <summary>
        /// 获取标准预设方案的默认权重。
        /// </summary>
        public static GearWeights Standard => new GearWeights
        {
            w_skill = 4.0f,
            w_passionMinor = 1.5f,
            w_passionMajor = 2.0f,
            w_dps = 5.0f,
            w_dmg = 30.0f,
            w_cooldown = 10.0f,
            w_range = 1.5f,
            w_quality = 10.0f,
            w_armor = 200.0f,
            w_insulation = 10.0f,
            w_movespeed = 20.0f,
            w_workspeed = 60.0f,
            upgradeThreshold = 0.15f
        };

        /// <summary>激战方案：护甲与 DPS 优先，移速次要</summary>
        public static GearWeights Aggressive => new GearWeights
        {
            w_skill = 5.0f,
            w_passionMinor = 1.5f,
            w_passionMajor = 2.5f,
            w_dps = 8.0f,
            w_dmg = 40.0f,
            w_cooldown = 12.0f,
            w_range = 0.5f,
            w_quality = 8.0f,
            w_armor = 300.0f,
            w_insulation = 5.0f,
            w_movespeed = 5.0f,
            w_workspeed = 40.0f,
            upgradeThreshold = 0.10f
        };

        /// <summary>经济方案：耐久与品质优先，避免浪费</summary>
        public static GearWeights Economic => new GearWeights
        {
            w_skill = 3.0f,
            w_passionMinor = 1.5f,
            w_passionMajor = 1.5f,
            w_dps = 3.0f,
            w_dmg = 20.0f,
            w_cooldown = 8.0f,
            w_range = 1.0f,
            w_quality = 20.0f,
            w_armor = 150.0f,
            w_insulation = 10.0f,
            w_movespeed = 15.0f,
            w_workspeed = 70.0f,
            upgradeThreshold = 0.25f
        };

        /// <summary>狩猎方案：远程射程与精准优先</summary>
        public static GearWeights Hunting => new GearWeights
        {
            w_skill = 4.0f,
            w_passionMinor = 1.5f,
            w_passionMajor = 2.0f,
            w_dps = 4.0f,
            w_dmg = 25.0f,
            w_cooldown = 15.0f,
            w_range = 4.0f,
            w_quality = 10.0f,
            w_armor = 100.0f,
            w_insulation = 10.0f,
            w_movespeed = 30.0f,
            w_workspeed = 40.0f,
            upgradeThreshold = 0.20f
        };
    }
}
