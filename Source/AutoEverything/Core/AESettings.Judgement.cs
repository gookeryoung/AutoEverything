using Verse;

namespace AutoEverything.Core
{
    /// <summary>
    /// AESettings 的判定参数部分（partial）。
    ///
    /// 集中存放四类自动化（人员自动 / 工作自动 / 高价值自动 / 自动携带）的判定阈值，
    /// 供底部【自动】主面板修改，并由计算层直接读取：
    /// - CombatEvaluator 读取评级判定阈值
    /// - WorkAllocator 读取工作分配阈值
    /// - PawnMarker 读取高价值判定档位与扫描范围
    /// - CarryPolicy 读取携带数量
    /// - AutoExecutor 读取触发周期与战斗过滤开关
    ///
    /// 默认值与重构前的硬编码常量完全一致，旧存档未写入新键时行为不变。
    /// </summary>
    public partial class AESettings
    {
        // ════════════════════════════════
        // 人员自动评级判定
        // ════════════════════════════════

        // A 档：至少 N 个双火，且双火+单火合计至少 M
        public static int tierAMajorMin = 2;
        public static int tierAPassionTotal = 3;

        // B 档：至少 N 个双火，且双火+单火合计至少 M
        public static int tierBMajorMin = 1;
        public static int tierBPassionTotal = 3;

        // 工作狂+神经质组合升档所需的专业工作双火数（S 档为组合即得，无需双火）
        public static int tierSSWorkMajors = 2;
        public static int tierSSSWorkMajors = 3;

        // 特殊天赋特质（博闻强识/开心果/极致体能/痴迷虚空/神秘学者/怪诞不经）直接 S
        public static bool tierSpecialTalentToS = true;

        // 沉鱼落雁 + 社交双火直接 S
        public static bool tierBeautySocialToS = true;

        // 配偶评级豁免：配偶达到指定档位时，本人至少同档
        public static bool spouseExemptionEnabled = true;
        public static CombatTier spouseExemptionTier = CombatTier.S;

        // ════════════════════════════════
        // 高价值自动标记判定
        // ════════════════════════════════

        // 高价值阈值：评级 >= 该档位的人类单位被判定为高价值（同时决定 S 档血清携带）
        public static CombatTier markHighValueTier = CombatTier.S;

        // 发现高价值单位时是否弹消息通知
        public static bool markNotifyMessage = true;

        // 扫描范围：true = 所有派系人类单位；false = 仅玩家阵营（殖民者/奴隶/囚犯）
        public static bool markIncludeNonPlayer = true;

        // ════════════════════════════════
        // 工作自动配置判定
        // ════════════════════════════════

        // 每人最多承担的核心工作数（priority<=2 的专业工作），超出者只给保底优先级
        public static int maxCoreWorkCount = 3;

        // 技能等级保底：相关技能 >= 高级阈值时优先级不低于 3，>= 中级阈值时不低于 2
        public static int skillFloorHighLevel = 8;
        public static int skillFloorMidLevel = 12;

        // 研究人员搬运/清洁降级为 4，避免打断研究
        public static bool researchServiceDowngrade = true;

        // 工作重配周期（tick），默认 3600 ≈ 1 分钟
        public static int workIntervalTicks = 3600;

        // 战斗期间跳过工作重配与自动携带（避免取消医疗 Job / 离开战位）
        public static bool skipAutoInCombat = true;

        // ════════════════════════════════
        // 自动携带判定
        // ════════════════════════════════

        // 食物携带数量
        public static int carryFoodCount = 3;

        // 血清携带数量（仅高价值档位携带）
        public static int carrySerumCount = 1;

        // 携带周期（tick），默认 6000 ≈ 100 秒
        public static int carryIntervalTicks = 6000;

        /// <summary>
        /// 读取/写入判定参数。由 AESettings.ExposeData 调用。
        /// Scribe Key 统一 ae_ 前缀，遵循项目存档规范。
        /// </summary>
        private static void ExposeJudgementData()
        {
            // 人员自动评级
            LookCompat(ref tierAMajorMin, "tierAMajorMin", 2);
            LookCompat(ref tierAPassionTotal, "tierAPassionTotal", 3);
            LookCompat(ref tierBMajorMin, "tierBMajorMin", 1);
            LookCompat(ref tierBPassionTotal, "tierBPassionTotal", 3);
            LookCompat(ref tierSSWorkMajors, "tierSSWorkMajors", 2);
            LookCompat(ref tierSSSWorkMajors, "tierSSSWorkMajors", 3);
            LookCompat(ref tierSpecialTalentToS, "tierSpecialTalentToS", true);
            LookCompat(ref tierBeautySocialToS, "tierBeautySocialToS", true);
            LookCompat(ref spouseExemptionEnabled, "spouseExemptionEnabled", true);
            Scribe_Values.Look(ref spouseExemptionTier, "ae_spouseExemptionTier", CombatTier.S);

            // 高价值自动标记
            Scribe_Values.Look(ref markHighValueTier, "ae_markHighValueTier", CombatTier.S);
            LookCompat(ref markNotifyMessage, "markNotifyMessage", true);
            LookCompat(ref markIncludeNonPlayer, "markIncludeNonPlayer", true);

            // 工作自动配置
            LookCompat(ref maxCoreWorkCount, "maxCoreWorkCount", 3);
            LookCompat(ref skillFloorHighLevel, "skillFloorHighLevel", 8);
            LookCompat(ref skillFloorMidLevel, "skillFloorMidLevel", 12);
            LookCompat(ref researchServiceDowngrade, "researchServiceDowngrade", true);
            LookCompat(ref workIntervalTicks, "workIntervalTicks", 3600);
            LookCompat(ref skipAutoInCombat, "skipAutoInCombat", true);

            // 自动携带
            LookCompat(ref carryFoodCount, "carryFoodCount", 3);
            LookCompat(ref carrySerumCount, "carrySerumCount", 1);
            LookCompat(ref carryIntervalTicks, "carryIntervalTicks", 6000);
        }

        /// <summary>
        /// int 版兼容 Scribe 读取：与 bool/float 版语义一致（加载先读旧键再读 ae_ 新键，保存只写新键）。
        /// </summary>
        private static void LookCompat(ref int value, string key, int defaultValue)
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                Scribe_Values.Look(ref value, "ae_" + key, defaultValue);
            }
            else if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                Scribe_Values.Look(ref value, key, defaultValue);
                Scribe_Values.Look(ref value, "ae_" + key, value);
            }
        }
    }
}
