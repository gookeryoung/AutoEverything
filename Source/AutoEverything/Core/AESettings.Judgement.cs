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

        // SSS 组合升档所需的专业工作双火数（S 档=任一顶级特质+1 双火，SS 档=组合即得，均无需配置）
        public static int tierSSSWorkMajors = 3;

        // S/SS/SSS 三大维度逐条规则开关（默认全开）：
        // 面板"评级判定规则表"每格勾选框对应一个字段，取消勾选即不启用该条规则，
        // 其余规则与 A/B 档阈值判定不受影响。
        // 维度1：乱开枪系列
        public static bool tierRuleTriggerHappySSS = true;
        public static bool tierRuleTriggerHappySS = true;
        public static bool tierRuleTriggerHappyS = true;
        // 维度2：坚韧格斗系列
        public static bool tierRuleToughMeleeSSS = true;
        public static bool tierRuleToughMeleeSS = true;
        public static bool tierRuleToughMeleeS = true;
        // 维度3：工作狂+神经质系列
        public static bool tierRuleWorkaholicSSS = true;
        public static bool tierRuleWorkaholicSS = true;
        public static bool tierRuleWorkaholicS = true;

        // 特殊天赋特质（博闻强识/开心果/极致体能/痴迷虚空/神秘学者/怪诞不经）直接 S
        public static bool tierSpecialTalentToS = true;

        // 沉鱼落雁 + 社交双火直接 S
        public static bool tierBeautySocialToS = true;

        // 配偶评级豁免：配偶达到指定档位时，本人至少同档
        public static bool spouseExemptionEnabled = true;
        public static CombatTier spouseExemptionTier = CombatTier.S;

        // ════════════════════════════════
        // 殖民者栏五类角色分组判定（前排/射手/工人/贸易/其他）
        // 纯白名单制：勾选且命中才归组，不做技能/兴趣兜底推断
        // ════════════════════════════════

        // 格斗者特质直接归入前排
        public static bool groupBrawlerTraitFront = true;

        // ── 强制判定条件（勾选后符合条件的殖民者直接归入对应角色，优先于技能/兴趣判定）──
        public static bool groupFrontGhoul = true;                  // 食尸鬼 → 前排
        public static bool groupFrontMeleeSpecialist = true;        // 格斗专家职位 → 前排
        public static bool groupShooterSpecialist = true;           // 射击专家职位 → 射手
        public static bool groupWorkerPacifist = true;              // 无法施暴 → 工人
        public static bool groupWorkerProductionSpecialist = true;  // 生产专家职位 → 工人
        public static bool groupTradeMoralist = true;               // 道德家职位 → 贸易

        // 射手补充条件：乱开枪特性（ShootingAccuracy degree=-1）
        public static bool groupTriggerHappyShooter = true;

        // 工人补充条件：工作狂特质（Industriousness degree>=1）、神经质特质（Neurotic degree>=0）、
        // 生产（手工）兴趣双火
        public static bool groupIndustriousWorker = true;
        public static bool groupNeuroticWorker = true;
        public static bool groupCraftingPassionWorker = true;

        // 贸易补充条件：俊俏/沉鱼落雁特质（Beauty degree>=1）、社交兴趣双火
        public static bool groupBeautyTrade = true;
        public static bool groupSocialPassionTrade = true;

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

        /// <summary>
        /// 交换顺序数组中相邻两项（设置页"上移/下移"按钮），然后写盘并重排殖民者栏。
        /// 纯逻辑在 RoleDetector.SwapRoleGroupInOrder，保证与右键拖拽一致的合法性约束。
        /// </summary>
        public static void SwapRoleGroupOrder(int indexA, int indexB)
        {
            if (roleGroupOrder == null) roleGroupOrder = RoleDetector.DefaultRoleGroupOrder();
            if (!RoleDetector.SwapRoleGroupInOrder(roleGroupOrder, indexA, indexB)) return;

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

        // 扫描范围（互斥二选一）：true = 仅非玩家阵营（敌对/中立/盟友/野生人类）；
        // false = 仅玩家阵营（殖民者/奴隶/囚犯）
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

        // 食物总携带数量（按优先级选择具体食物类型，见 CarryPolicy.FoodDefNames）
        public static int carryFoodCount = 3;

        // 血清是否仅高价值档位携带（档位阈值复用 markHighValueTier）
        public static bool carrySerumHighValueOnly = true;

        // 药品/血清逐项携带数量：存档载体 "defName#count"，运行时映射见 carryItemMap
        public static List<string> carryItemEntries = CarryItemCatalog.CreateDefaultEntries();
        public static Dictionary<string, int> carryItemMap = new Dictionary<string, int>();

        // 携带周期（tick），默认 6000 ≈ 100 秒
        public static int carryIntervalTicks = 6000;

        /// <summary>
        /// 查询某物品的配置携带数量。优先取玩家配置，缺失时回落目录默认值。
        /// </summary>
        public static int GetCarryItemCount(string defName)
        {
            if (string.IsNullOrEmpty(defName)) return 0;
            int count;
            if (carryItemMap != null && carryItemMap.TryGetValue(defName, out count)) return count;
            return CarryItemCatalog.GetDefaultCount(defName);
        }

        /// <summary>
        /// 设置某物品的配置携带数量（0 表示不携带），同步更新存档载体与运行时映射。
        /// 调用方负责触发 ModSettings.Write()。
        /// </summary>
        public static void SetCarryItemCount(string defName, int count)
        {
            if (string.IsNullOrEmpty(defName)) return;
            if (count < 0) count = 0;

            if (carryItemMap == null) carryItemMap = new Dictionary<string, int>();
            carryItemMap[defName] = count;

            if (carryItemEntries == null) carryItemEntries = new List<string>();
            string entry = defName + "#" + count;
            for (int i = 0; i < carryItemEntries.Count; i++)
            {
                string current = carryItemEntries[i];
                int sep = current.IndexOf('#');
                if (sep > 0 && current.Substring(0, sep) == defName)
                {
                    carryItemEntries[i] = entry;
                    return;
                }
            }
            carryItemEntries.Add(entry);
        }

        /// <summary>恢复目录默认携带数量（食物数量与携带周期不受影响）。</summary>
        public static void ResetCarryItemsToDefault()
        {
            carryItemEntries = CarryItemCatalog.CreateDefaultEntries();
            RebuildCarryItemMap();
        }

        /// <summary>把存档载体 "defName#count" 解析为运行时映射。</summary>
        public static void RebuildCarryItemMap()
        {
            if (carryItemMap == null) carryItemMap = new Dictionary<string, int>();
            else carryItemMap.Clear();

            if (carryItemEntries == null) return;
            for (int i = 0; i < carryItemEntries.Count; i++)
            {
                string entry = carryItemEntries[i];
                if (string.IsNullOrEmpty(entry)) continue;
                int sep = entry.IndexOf('#');
                if (sep <= 0 || sep >= entry.Length - 1) continue;
                if (!int.TryParse(entry.Substring(sep + 1), out int count)) continue;
                carryItemMap[entry.Substring(0, sep)] = count < 0 ? 0 : count;
            }
        }

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
            LookCompat(ref tierSSSWorkMajors, "tierSSSWorkMajors", 3);
            LookCompat(ref tierRuleTriggerHappySSS, "tierRuleTriggerHappySSS", true);
            LookCompat(ref tierRuleTriggerHappySS, "tierRuleTriggerHappySS", true);
            LookCompat(ref tierRuleTriggerHappyS, "tierRuleTriggerHappyS", true);
            LookCompat(ref tierRuleToughMeleeSSS, "tierRuleToughMeleeSSS", true);
            LookCompat(ref tierRuleToughMeleeSS, "tierRuleToughMeleeSS", true);
            LookCompat(ref tierRuleToughMeleeS, "tierRuleToughMeleeS", true);
            LookCompat(ref tierRuleWorkaholicSSS, "tierRuleWorkaholicSSS", true);
            LookCompat(ref tierRuleWorkaholicSS, "tierRuleWorkaholicSS", true);
            LookCompat(ref tierRuleWorkaholicS, "tierRuleWorkaholicS", true);
            LookCompat(ref tierSpecialTalentToS, "tierSpecialTalentToS", true);
            LookCompat(ref tierBeautySocialToS, "tierBeautySocialToS", true);
            LookCompat(ref spouseExemptionEnabled, "spouseExemptionEnabled", true);
            Scribe_Values.Look(ref spouseExemptionTier, "ae_spouseExemptionTier", CombatTier.S);

            // 殖民者栏五类角色分组判定
            LookCompat(ref groupBrawlerTraitFront, "groupBrawlerTraitFront", true);
            LookCompat(ref groupFrontGhoul, "groupFrontGhoul", true);
            LookCompat(ref groupFrontMeleeSpecialist, "groupFrontMeleeSpecialist", true);
            LookCompat(ref groupShooterSpecialist, "groupShooterSpecialist", true);
            LookCompat(ref groupWorkerPacifist, "groupWorkerPacifist", true);
            LookCompat(ref groupWorkerProductionSpecialist, "groupWorkerProductionSpecialist", true);
            LookCompat(ref groupTradeMoralist, "groupTradeMoralist", true);
            LookCompat(ref groupTriggerHappyShooter, "groupTriggerHappyShooter", true);
            LookCompat(ref groupIndustriousWorker, "groupIndustriousWorker", true);
            LookCompat(ref groupNeuroticWorker, "groupNeuroticWorker", true);
            LookCompat(ref groupCraftingPassionWorker, "groupCraftingPassionWorker", true);
            LookCompat(ref groupBeautyTrade, "groupBeautyTrade", true);
            LookCompat(ref groupSocialPassionTrade, "groupSocialPassionTrade", true);

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
            LookCompat(ref carrySerumHighValueOnly, "carrySerumHighValueOnly", true);
            LookCompat(ref carryIntervalTicks, "carryIntervalTicks", 6000);
            // 逐项携带数量：集合无法双读旧键，直接用新键；旧存档缺失时回落目录默认值
            Scribe_Collections.Look(ref carryItemEntries, "ae_carryItemEntries", LookMode.Value);
            if (carryItemEntries == null) carryItemEntries = CarryItemCatalog.CreateDefaultEntries();
            RebuildCarryItemMap();
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
