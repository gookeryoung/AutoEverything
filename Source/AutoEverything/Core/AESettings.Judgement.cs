using System.Collections.Generic;
using Verse;
using AutoEverything.RoleEvaluation;

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
        // 殖民者栏五类角色分组判定（前排/射手/工人/贸易/其他）
        // ════════════════════════════════

        // 前排：近战技能达到该值且高于射击
        public static int groupFrontSkillMin = 8;

        // 射手：射击技能达到该值且高于近战
        public static int groupShooterSkillMin = 8;

        // 贸易：社交技能达到该值且不低于射击/近战
        public static int groupTradeSkillMin = 8;

        // 工人：射击与近战均低于该值
        public static int groupWorkerMaxCombatSkill = 5;

        // 兴趣优先：近战有火且射击无火 → 前排；射击有火且近战无火 → 射手
        public static bool groupPassionPriority = true;

        // 格斗者特质直接归入前排
        public static bool groupBrawlerTraitFront = true;

        // 被指派狩猎（Hunting 优先级 1）归入射手
        public static bool groupHunterAsShooter = true;

        // ── 强制判定条件（勾选后符合条件的殖民者直接归入对应角色，优先于技能/兴趣判定）──
        public static bool groupFrontGhoul = true;                  // 食尸鬼 → 前排
        public static bool groupFrontMeleeSpecialist = true;        // 格斗专家职位 → 前排
        public static bool groupShooterSpecialist = true;           // 射击专家职位 → 射手
        public static bool groupWorkerPacifist = true;              // 无法施暴 → 工人
        public static bool groupWorkerProductionSpecialist = true;  // 生产专家职位 → 工人
        public static bool groupTradeMoralist = true;               // 道德家职位 → 贸易

        // 五类分组在殖民者栏中的显示顺序（存 RoleGroup 的 int 值，顺序即可视顺序）。
        // 玩家可在殖民者栏分组框上右键拖拽调整，重新分组时按该顺序排列人员。
        public static List<int> roleGroupOrder = RoleDetector.DefaultRoleGroupOrder();

        /// <summary>
        /// 取得角色分组的可视序号：自定义顺序优先，缺失时回退枚举顺序。
        /// Apply 分组与"按角色+评级"排序都必须使用本方法，才能保证两者一致。
        /// </summary>
        public static int GetRoleGroupOrder(RoleGroup group)
        {
            return RoleDetector.GetRoleGroupOrderIn(roleGroupOrder, (int)group);
        }

        /// <summary>
        /// 把分组 from 移到分组 to 所在位置（右键拖拽释放时调用），
        /// 然后立即写盘并按新顺序重排殖民者栏。
        /// </summary>
        public static void MoveRoleGroup(RoleGroup from, RoleGroup to)
        {
            if (roleGroupOrder == null) roleGroupOrder = RoleDetector.DefaultRoleGroupOrder();
            // 顺序数组操作抽到纯逻辑层，便于单元测试
            if (!RoleDetector.MoveRoleGroupInOrder(roleGroupOrder, (int)from, (int)to)) return;

            AutoEverythingMod.settings?.Write();
            OnGroupByRoleChanged();
        }

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

            // 殖民者栏五类角色分组判定
            LookCompat(ref groupFrontSkillMin, "groupFrontSkillMin", 8);
            LookCompat(ref groupShooterSkillMin, "groupShooterSkillMin", 8);
            LookCompat(ref groupTradeSkillMin, "groupTradeSkillMin", 8);
            LookCompat(ref groupWorkerMaxCombatSkill, "groupWorkerMaxCombatSkill", 5);
            LookCompat(ref groupPassionPriority, "groupPassionPriority", true);
            LookCompat(ref groupBrawlerTraitFront, "groupBrawlerTraitFront", true);
            LookCompat(ref groupHunterAsShooter, "groupHunterAsShooter", true);
            LookCompat(ref groupFrontGhoul, "groupFrontGhoul", true);
            LookCompat(ref groupFrontMeleeSpecialist, "groupFrontMeleeSpecialist", true);
            LookCompat(ref groupShooterSpecialist, "groupShooterSpecialist", true);
            LookCompat(ref groupWorkerPacifist, "groupWorkerPacifist", true);
            LookCompat(ref groupWorkerProductionSpecialist, "groupWorkerProductionSpecialist", true);
            LookCompat(ref groupTradeMoralist, "groupTradeMoralist", true);

            // 分组显示顺序：List<int>，加载后校验必须是 0-4 的完整排列
            Scribe_Collections.Look(ref roleGroupOrder, "ae_roleGroupOrder", LookMode.Value);
            if (!RoleDetector.IsValidRoleGroupOrder(roleGroupOrder))
            {
                roleGroupOrder = RoleDetector.DefaultRoleGroupOrder();
            }

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
