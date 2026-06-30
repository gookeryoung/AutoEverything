using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using AutoEverything.RoleEvaluation;
using AutoEverything.AutoEquipment;
using AutoEverything.AutoEquipment.Scoring;
using AutoEverything.UI;

namespace AutoEverything.Core
{
    public class AESettings : ModSettings
    {
        // 主开关
        public static bool enabled = true;
        public static bool autoWeapons = true;
        public static bool autoApparel = true;
        public static bool autoInventory = true;
        public static bool sidearms = true;
        public static bool autoWorkEnabled = true;  // AutoWork 自动工作分配主开关
        public static bool autoTierTag = true;       // 人员自动评级（周期触发 + 新增人员触发）
        public static bool autoGearReallocate = true;  // 自动装备重配主开关（轻量升级检查，周期触发）

        // 情境切换
        public static bool combatSwitch = true;      // 征召/取消征召时切换装备
        public static bool huntingWeapon = true;     // 狩猎工作时装备狩猎武器
        public static bool temperatureAware = true;  // 季节性服装切换
        public static bool jobAwareApparel = true;   // 按工作切换工作属性服装

        // 副武器
        public static bool autoMeleeSidearm = true;  // 受近战攻击时自动切出近战武器
        public static bool carryMedicine = true;     // 医生/战斗人员携带药品
        public static int medicineCount = 3;          // 携带数量

        // 性能
        public static int evaluateInterval = 500;    // 装备评估间隔（tick）

        // 阈值
        public static float upgradeThreshold = 0.15f; // 评分需提升 15% 才触发换装
        public static float tempDangerMargin = 5f;    // 超出舒适温度范围多少度才触发换装

        // 全局重配规则
        // 设计为玩家可调：不同玩家偏好不同——有的希望彻底重配，有的希望尊重锁定/征召
        public static bool reallocateDropWeapons = true;     // 重配前先放下当前武器，让无火小人的好武器可被双火小人拾取
        public static bool reallocateRespectDrafted = true;  // 跳过征召中的殖民者（不打断战斗）
        public static bool reallocateRespectLocked = true;  // 跳过已锁定的殖民者
        public static bool reallocateRespectBiocoded = true; // 跳过生物编码武器（个人绑定，无法转交）

        // 战斗价值公式可调权重（用于"高价值殖民者"判定）
        // 公式：战斗价值 = (射击等级×射击兴趣乘数 + 近战等级×近战兴趣乘数) × 技能权重 + Σ特质加分
        // 兴趣乘数：无火 1.0，单火 1.5，双火 2.0
        public static float cvSkillWeight = 1.0f;            // 技能等级整体权重
        public static float cvPassionNoneMult = 1.0f;        // 无火兴趣乘数
        public static float cvPassionMinorMult = 1.5f;       // 单火兴趣乘数
        public static float cvPassionMajorMult = 2.0f;       // 双火兴趣乘数

        // 特质加分（可调）：原生特质对战斗价值的额外修正
        // 设计意图：玩家可按自己理解调整特质价值
        public static float cvToughBonus = 30f;              // 坚韧 Tough：减伤 50%
        public static float cvTriggerHappyPenalty = -15f;    // 乱开枪 ShootingAccuracy degree=-1：精度大幅下降
        public static float cvCarefulShooterBonus = 15f;     // 冷枪手 ShootingAccuracy degree=+1：精度提升但冷却慢

        // 全局重配护甲分配
        // 设计为玩家可调：阈值与偏好规则可调
        public static bool reallocateApparel = true;            // 是否同时重配护甲（默认开启）
        public static float heavyArmorSharpThreshold = 0.4f;  // 护甲锐穿 ≥ 此值视为重甲
        public static float heavyArmorPenaltyForLight = -1000f; // Heavy 偏好角色对轻甲的减分（强制选重甲）
        public static float lightArmorPenaltyForHeavy = -1000f; // Light 偏好角色对重甲的减分（强制选轻甲）
        public static float heavyArmorMatchBonus = 500f;        // 角色偏好与护甲类型匹配时的奖励分（Heavy+heavy / Light+light），让匹配偏好显著胜过 Flexible

        // 自定义战斗评级识别码
        // 设计：玩家可为指定殖民者手动指定档次，跳过自动公式计算
        //   场景：玩家认为某个 S 档殖民者特别关键，强制锁为 S；
        //         或某 X 档小人有特殊价值，需提升到 A
        //   存档载体：List<string>，元素格式 "档次#Pawn名字"，如 "S#王五"
        //   运行时：解析为 Dictionary<名字, CombatTier> 供快速查询
        //   命中自定义评级的 Pawn 不再走 ComputeCombatValue 公式，直接采用指定档次
        public static List<string> customTierEntries = new List<string>();
        public static Dictionary<string, CombatTier> customTierMap = new Dictionary<string, CombatTier>();

        // 调试
        public static bool debugLogging = false;       // 详细日志开关

        // 殖民者栏默认排序方式：玩家在 Mod 选项里配置，
        // ITab "全局人物评级"按钮一键应用评级前缀并按此排序重排
        // 默认 ByTierThenValue：按评级 SABCDX 从左到右，最符合评级前缀的视觉直觉
        public static ColonistBarSortMode defaultSortMode = ColonistBarSortMode.ByTierThenValue;

        // ===================== 全局评级标签 =====================
        // 设计：玩家点击"全局人物评级"按钮后，自动给所有殖民者的 Nick 加上系统评级前缀，
        //   格式："S#王五"（系统档 S + # + 原 Nick）
        //   自定义评级时仍按系统档计算前缀（与 AEDebug.Label 不同，此处只显示系统档）
        // 用 thingIDNumber → 原 Nick 映射保存原名，便于恢复
        // 持久化：通过 tierTagOriginalEntries 存档，避免重启后丢失导致误剥离玩家手动改的 Nick
        private static readonly Dictionary<int, string> tierTagOriginals = new Dictionary<int, string>();
        // 存档载体：List<string> 格式 "thingIDNumber|原Nick"，加载后重建字典
        private static List<string> tierTagOriginalEntries;
        private const string TIER_TAG_PREFIX_SEPARATOR = "#";

        /// <summary>
        /// 给所有玩家殖民者（含食尸鬼）的 Nick 加上系统评级前缀，格式 "S#王五"。
        /// 已加过前缀的会先剥离再重加，保证评级最新。
        /// 跳过动物、机械族、奴隶、未成年。
        /// 注：食尸鬼也按相同规则评级，但不参与装备分配——玩家可一眼分辨其价值。
        /// </summary>
        public static int ApplyTierTagsToAllPawns()
        {
            int touched = 0;
            // 收集所有需要评级的 Pawn：殖民者 + 食尸鬼
            // 食尸鬼不属于 FreeColonists，需从 AllPawns 中过滤
            var pawns = new List<Pawn>();
            foreach (Pawn pawn in PawnsFinder.AllMaps_FreeColonists)
            {
                if (pawn != null) pawns.Add(pawn);
            }
            // 食尸鬼也加入评级范围（仅评级，不分配装备）
            foreach (Map map in Find.Maps)
            {
                if (map == null) continue;
                foreach (Pawn pawn in map.mapPawns.AllPawns)
                {
                    if (pawn == null) continue;
                    if (!DLCCompat.IsGhoul(pawn)) continue;
                    if (pawn.Faction == null || !pawn.Faction.IsPlayer) continue;
                    pawns.Add(pawn);
                }
            }

            foreach (Pawn pawn in pawns)
            {
                if (!PawnSuitabilityChecker.CanManageGear(pawn)) continue;
                // 注：食尸鬼不再排除——用户要求食尸鬼也评级

                NameTriple nt = pawn.Name as NameTriple;
                if (nt == null) continue;

                string currentNick = nt.Nick ?? string.Empty;
                // 剥离已有评级前缀，得到"纯净名"
                string cleanNick = TierTagHelper.Strip(currentNick);

                // 首次应用：保存原名到字典（若已存在则保留最早的）
                int pid = pawn.thingIDNumber;
                if (!tierTagOriginals.ContainsKey(pid))
                {
                    tierTagOriginals[pid] = cleanNick;
                }
                else
                {
                    // 已保存过原名，使用保存值作为"纯净名"
                    cleanNick = tierTagOriginals[pid];
                }

                // 计算当前系统评级
                CombatTier tier = CombatEvaluator.GetAutoCombatTier(pawn);
                string newNick = tier + TIER_TAG_PREFIX_SEPARATOR + cleanNick;

                if (newNick != currentNick)
                {
                    pawn.Name = new NameTriple(nt.First, newNick, nt.Last);
                    touched++;
                }
            }
            return touched;
        }

        /// <summary>
        /// 清除所有殖民者（含食尸鬼）Nick 上的评级前缀，恢复原名。
        /// 优先从 tierTagOriginals 取原名；若字典无（重启后），尝试从 Nick 解析剥离。
        /// </summary>
        public static int ClearTierTagsFromAllPawns()
        {
            int touched = 0;
            // 收集殖民者 + 食尸鬼，与 Apply 保持一致
            var pawns = new List<Pawn>();
            foreach (Pawn pawn in PawnsFinder.AllMaps_FreeColonists)
            {
                if (pawn != null) pawns.Add(pawn);
            }
            foreach (Map map in Find.Maps)
            {
                if (map == null) continue;
                foreach (Pawn pawn in map.mapPawns.AllPawns)
                {
                    if (pawn == null) continue;
                    if (!DLCCompat.IsGhoul(pawn)) continue;
                    if (pawn.Faction == null || !pawn.Faction.IsPlayer) continue;
                    pawns.Add(pawn);
                }
            }

            foreach (Pawn pawn in pawns)
            {
                NameTriple nt = pawn.Name as NameTriple;
                if (nt == null) continue;

                string currentNick = nt.Nick ?? string.Empty;
                if (!TierTagHelper.HasPrefix(currentNick)) continue;

                int pid = pawn.thingIDNumber;
                string cleanNick;
                if (tierTagOriginals.TryGetValue(pid, out string saved))
                {
                    cleanNick = saved;
                }
                else
                {
                    cleanNick = TierTagHelper.Strip(currentNick);
                }

                if (cleanNick != currentNick)
                {
                    pawn.Name = new NameTriple(nt.First, cleanNick, nt.Last);
                    touched++;
                }
            }
            tierTagOriginals.Clear();
            SyncTierTagOriginalEntries();
            return touched;
        }

        /// <summary>
        /// 应用评级前缀并按评级降序重排殖民者栏。
        /// 排序规则：先按 CombatTier 降序（S→A→B→C→D→X），同档内按 ComputeCombatValue 降序。
        /// S 档殖民者排在殖民者栏左侧，X 档（和平主义者等）排在最右。
        /// </summary>
        public static int ApplyTierTagsAndSortByValue()
        {
            int touched = ApplyTierTagsToAllPawns();
            ReorderColonistBar(ComparePawnByTierThenValueDesc);
            return touched;
        }

        /// <summary>
        /// 应用评级前缀并按角色分组重排殖民者栏。
        /// 角色顺序：Brawler → Shooter → Doctor → Worker → Pacifist → Hunter → Leader → Default。
        /// 同角色内按评级降序（S→A→B→C→D→X），同档内按战斗价值降序。
        /// </summary>
        public static int ApplyTierTagsAndSortByRole()
        {
            int touched = ApplyTierTagsToAllPawns();
            ReorderColonistBar(ComparePawnByRoleThenValueDesc);
            return touched;
        }

        /// <summary>
        /// 应用评级前缀并按 Mod 选项配置的默认排序重排殖民者栏。
        /// 玩家在 Mod 选项里配置 defaultSortMode，ITab "全局人物评级"按钮调用此方法。
        /// None 模式下仅应用前缀不重排。
        /// </summary>
        public static int ApplyTierTagsWithDefaultSort()
        {
            int touched = ApplyTierTagsToAllPawns();
            switch (defaultSortMode)
            {
                case ColonistBarSortMode.None:
                    // 不重排：保留殖民者栏原顺序
                    break;
                case ColonistBarSortMode.ByTierThenValue:
                    ReorderColonistBar(ComparePawnByTierThenValueDesc);
                    break;
                case ColonistBarSortMode.ByRoleThenTier:
                    ReorderColonistBar(ComparePawnByRoleThenValueDesc);
                    break;
                case ColonistBarSortMode.ByCombatValue:
                    ReorderColonistBar(ComparePawnByCombatValueOnlyDesc);
                    break;
            }
            return touched;
        }

        /// <summary>
        /// 重排殖民者栏：按比较器排序后写入 displayOrder，刷新殖民者栏。
        /// RimWorld 殖民者栏顺序由 Pawn.playerSettings.displayOrder 决定，
        /// 修改后调 Find.ColonistBar.MarkColonistsDirty() 刷新缓存。
        /// 注意：displayOrder 仅在同一 map/caravan group 内有效，跨 group 顺序由地图/旅商顺序决定。
        /// </summary>
        private static void ReorderColonistBar(System.Comparison<Pawn> comparison)
        {
            var pawns = new List<Pawn>();
            foreach (Pawn pawn in PawnsFinder.AllMaps_FreeColonists)
            {
                if (pawn != null && pawn.playerSettings != null)
                    pawns.Add(pawn);
            }

            // 预计算缓存：避免 Sort 比较器内 O(n log n) 次重复调用
            // GetCombatTier/ComputeCombatValue/DetectRole（均涉及技能/特质查询）
            sortTierCache.Clear();
            sortValueCache.Clear();
            sortRoleCache.Clear();
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                sortTierCache[p] = CombatEvaluator.GetCombatTier(p);
                sortValueCache[p] = CombatEvaluator.ComputeCombatValue(p);
                sortRoleCache[p] = GetRoleOrder(RoleDetector.DetectRole(p));
            }

            pawns.Sort(comparison);

            for (int i = 0; i < pawns.Count; i++)
            {
                pawns[i].playerSettings.displayOrder = i;
            }

            Find.ColonistBar.MarkColonistsDirty();
        }

        // 殖民者栏排序缓存：排序前预计算，避免 Sort 比较器内重复调用
        // GetCombatTier/ComputeCombatValue/DetectRole（均涉及技能/特质查询）
        private static readonly Dictionary<Pawn, CombatTier> sortTierCache = new Dictionary<Pawn, CombatTier>();
        private static readonly Dictionary<Pawn, float> sortValueCache = new Dictionary<Pawn, float>();
        private static readonly Dictionary<Pawn, int> sortRoleCache = new Dictionary<Pawn, int>();

        /// <summary>
        /// 评级降序比较器：先按 CombatTier 降序（S→A→B→C→D→X），
        /// 同档内再按 ComputeCombatValue 降序。
        /// 设计意图：和平主义者（X 档）即使技能高也排在最右，
        /// 避免高技能的 X 档殖民者挤占 S/A 档位置。
        /// </summary>
        private static int ComparePawnByTierThenValueDesc(Pawn a, Pawn b)
        {
            // CombatTier 枚举值：X=0, D=1, C=2, B=3, A=4, S=5，降序即 S 在前
            CombatTier ta = sortTierCache[a];
            CombatTier tb = sortTierCache[b];
            if (ta != tb) return tb.CompareTo(ta);
            return sortValueCache[b].CompareTo(sortValueCache[a]);
        }

        /// <summary>
        /// 仅按战斗价值降序比较器：不区分评级，纯按 ComputeCombatValue 排序。
        /// 注意：高技能的和平主义者（X 档）可能挤占 S/A 档前列位置，
        /// 仅适用于玩家明确希望"纯战斗价值优先"的场景。
        /// </summary>
        private static int ComparePawnByCombatValueOnlyDesc(Pawn a, Pawn b)
        {
            return sortValueCache[b].CompareTo(sortValueCache[a]);
        }

        /// <summary>
        /// 角色优先级 + 评级降序比较器。
        /// 角色顺序：Brawler(0) → Shooter(1) → Doctor(2) → Worker(3) → Pacifist(4) → Hunter(5) → Leader(6) → Default(99)。
        /// 同角色内按评级降序（S→A→B→C→D→X），同档内按战斗价值降序。
        /// </summary>
        private static int ComparePawnByRoleThenValueDesc(Pawn a, Pawn b)
        {
            int ra = sortRoleCache[a];
            int rb = sortRoleCache[b];
            if (ra != rb) return ra.CompareTo(rb);
            return ComparePawnByTierThenValueDesc(a, b);
        }

        /// <summary>
        /// 获取角色排序优先级：数字小的排前面。
        /// 顺序设计：前排战斗（Brawler/Shooter）→ 后勤（Doctor/Worker/Pacifist）→ 其他（Hunter/Leader）→ 默认。
        /// </summary>
        private static int GetRoleOrder(Role role)
        {
            switch (role)
            {
                case Role.Brawler:  return 0;
                case Role.Shooter:  return 1;
                case Role.Doctor:   return 2;
                case Role.Worker:   return 3;
                case Role.Pacifist: return 4;
                case Role.Hunter:   return 5;
                case Role.Leader:   return 6;
                default:            return 99;
            }
        }

        /// <summary>
        /// 获取排序模式的可读标签（用于设置界面按钮与 FloatMenu 选项）。
        /// </summary>
        private static string GetSortModeLabel(ColonistBarSortMode mode)
        {
            return ("AE_SortMode_" + mode).Translate();
        }

        /// <summary>
        /// 把 tierTagOriginals 字典同步到存档载体 tierTagOriginalEntries。
        /// 在 Apply/Clear 后调用，确保下次存档时持久化最新状态。
        /// </summary>
        private static void SyncTierTagOriginalEntries()
        {
            tierTagOriginalEntries.Clear();
            foreach (var kvp in tierTagOriginals)
            {
                // 格式 "thingIDNumber|原Nick"，原 Nick 不应含 | 字符（RimWorld Nick 通常不含）
                tierTagOriginalEntries.Add(kvp.Key + "|" + kvp.Value);
            }
        }

        /// <summary>
        /// 检查 Nick 是否已有评级前缀（格式：单字母 + #）。
        /// 公开供 AEDebug.Label 调用，避免 Nick 已被"全局人物评级"按钮改名后
        /// 再次拼接出 "S#S#王五" 双重前缀。
        /// </summary>
        public static bool HasTierTagPrefixOnLabel(string label)
        {
            return TierTagHelper.HasPrefix(label);
        }

        // 预设方案（重构后新增）
        // 由 GearPolicyEngine 维护，此处仅作存档载体
        // 实际值通过 GearPolicyEngine.SwitchPreset 设置

        // 设置窗口滚动位置：内容超出窗口高度时使用
        private static Vector2 settingsScrollPos = Vector2.zero;

        public override void ExposeData()
        {
            // Scribe Key 统一加 ae_ 前缀，符合项目规范
            // 兼容旧存档：加载时先读无前缀旧键，再读 ae_ 新键（若存在则覆盖）
            // 保存时只写 ae_ 新键，旧存档升级后自动迁移
            LookCompat(ref enabled, "enabled", true);
            LookCompat(ref autoWeapons, "autoWeapons", true);
            LookCompat(ref autoApparel, "autoApparel", true);
            LookCompat(ref autoInventory, "autoInventory", true);
            LookCompat(ref sidearms, "sidearms", true);
            LookCompat(ref combatSwitch, "combatSwitch", true);
            LookCompat(ref huntingWeapon, "huntingWeapon", true);
            LookCompat(ref temperatureAware, "temperatureAware", true);
            LookCompat(ref jobAwareApparel, "jobAwareApparel", true);
            LookCompat(ref autoMeleeSidearm, "autoMeleeSidearm", true);
            LookCompat(ref carryMedicine, "carryMedicine", true);
            LookCompat(ref medicineCount, "medicineCount", 3);
            LookCompat(ref evaluateInterval, "evaluateInterval", 500);
            LookCompat(ref upgradeThreshold, "upgradeThreshold", 0.15f);
            LookCompat(ref tempDangerMargin, "tempDangerMargin", 5f);
            LookCompat(ref reallocateDropWeapons, "reallocateDropWeapons", true);
            LookCompat(ref reallocateRespectDrafted, "reallocateRespectDrafted", true);
            LookCompat(ref reallocateRespectLocked, "reallocateRespectLocked", true);
            LookCompat(ref reallocateRespectBiocoded, "reallocateRespectBiocoded", true);
            LookCompat(ref cvSkillWeight, "cvSkillWeight", 1.0f);
            LookCompat(ref cvPassionNoneMult, "cvPassionNoneMult", 1.0f);
            LookCompat(ref cvPassionMinorMult, "cvPassionMinorMult", 1.5f);
            LookCompat(ref cvPassionMajorMult, "cvPassionMajorMult", 2.0f);
            LookCompat(ref cvToughBonus, "cvToughBonus", 30f);
            LookCompat(ref cvTriggerHappyPenalty, "cvTriggerHappyPenalty", -15f);
            LookCompat(ref cvCarefulShooterBonus, "cvCarefulShooterBonus", 15f);
            LookCompat(ref reallocateApparel, "reallocateApparel", true);
            LookCompat(ref heavyArmorSharpThreshold, "heavyArmorSharpThreshold", 0.4f);
            LookCompat(ref heavyArmorPenaltyForLight, "heavyArmorPenaltyForLight", -1000f);
            LookCompat(ref lightArmorPenaltyForHeavy, "lightArmorPenaltyForHeavy", -1000f);
            LookCompat(ref heavyArmorMatchBonus, "heavyArmorMatchBonus", 500f);
            LookCompat(ref debugLogging, "debugLogging", false);
            LookCompat(ref autoWorkEnabled, "autoWorkEnabled", true);
            LookCompat(ref autoTierTag, "autoTierTag", true);
            LookCompat(ref autoGearReallocate, "autoGearReallocate", true);
            // 殖民者栏默认排序方式
            Scribe_Values.Look(ref defaultSortMode, "ae_defaultSortMode", ColonistBarSortMode.ByTierThenValue);

            // 自定义评级：以 List<string> 作为存档载体（"档次#名字" 格式）
            // 存档加载后需重建运行时字典
            // 集合类型无法用 LookCompat 双读，直接用新键（旧存档会丢失自定义评级，需玩家重设）
            Scribe_Collections.Look(ref customTierEntries, "ae_customTierEntries", LookMode.Value);
            if (customTierEntries == null) customTierEntries = new List<string>();
            RebuildCustomTierMap();

            // 全局评级标签原名映射持久化：避免重启后丢失导致误剥离玩家手动改的 Nick
            Scribe_Collections.Look(ref tierTagOriginalEntries, "ae_tierTagOriginals", LookMode.Value);
            if (tierTagOriginalEntries == null) tierTagOriginalEntries = new List<string>();
            // 加载后重建运行时字典
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                tierTagOriginals.Clear();
                foreach (string entry in tierTagOriginalEntries)
                {
                    if (string.IsNullOrEmpty(entry)) continue;
                    int sep = entry.IndexOf('|');
                    if (sep <= 0 || sep >= entry.Length - 1) continue;
                    if (!int.TryParse(entry.Substring(0, sep), out int pid)) continue;
                    string origNick = entry.Substring(sep + 1);
                    tierTagOriginals[pid] = origNick;
                }
            }

            // 预设方案与监测开关由 GearPolicyEngine/DebugMonitor 持久化
            // 此处委托其存档
            GearPolicyEngine.ExposeData();
            DebugMonitor.ExposeData();

            base.ExposeData();
        }

        /// <summary>
        /// 兼容性 Scribe 读取：
        /// - 加载模式：先读无前缀旧键（兼容旧存档），再读 ae_ 新键（若存在则覆盖）
        /// - 保存模式：只写 ae_ 新键，实现自动迁移
        /// </summary>
        private static void LookCompat(ref bool value, string key, bool defaultValue)
        {
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                Scribe_Values.Look(ref value, key, defaultValue);
            Scribe_Values.Look(ref value, "ae_" + key, value);
        }

        private static void LookCompat(ref float value, string key, float defaultValue)
        {
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                Scribe_Values.Look(ref value, key, defaultValue);
            Scribe_Values.Look(ref value, "ae_" + key, value);
        }

        private static void LookCompat(ref int value, string key, int defaultValue)
        {
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                Scribe_Values.Look(ref value, key, defaultValue);
            Scribe_Values.Look(ref value, "ae_" + key, value);
        }

        /// <summary>
        /// 重建运行时字典：将 "档次#名字" 列表解析为 Dictionary。
        /// 在 ExposeData 之后与每次写入后调用，保持运行时数据一致。
        /// </summary>
        public static void RebuildCustomTierMap()
        {
            customTierMap.Clear();
            if (customTierEntries == null) return;
            for (int i = 0; i < customTierEntries.Count; i++)
            {
                string entry = customTierEntries[i];
                if (string.IsNullOrEmpty(entry)) continue;

                int sep = entry.IndexOf('#');
                if (sep <= 0 || sep >= entry.Length - 1) continue;

                string tierStr = entry.Substring(0, sep);
                string name = entry.Substring(sep + 1);
                if (System.Enum.TryParse(tierStr, out CombatTier tier))
                {
                    // 同一名字后写覆盖前写
                    customTierMap[name] = tier;
                }
            }
        }

        /// <summary>
        /// 设置/更新某 Pawn 的自定义评级。
        /// 写入会立即更新存档载体与运行时字典。
        /// </summary>
        public static void SetCustomTier(string name, CombatTier tier)
        {
            if (string.IsNullOrEmpty(name)) return;

            // 移除旧条目
            for (int i = customTierEntries.Count - 1; i >= 0; i--)
            {
                int sep = customTierEntries[i].IndexOf('#');
                if (sep > 0 && sep < customTierEntries[i].Length - 1
                    && customTierEntries[i].Substring(sep + 1) == name)
                {
                    customTierEntries.RemoveAt(i);
                }
            }
            customTierEntries.Add(tier + "#" + name);
            customTierMap[name] = tier;
        }

        /// <summary>
        /// 清除某 Pawn 的自定义评级，恢复自动判定。
        /// </summary>
        public static void ClearCustomTier(string name)
        {
            if (string.IsNullOrEmpty(name)) return;

            for (int i = customTierEntries.Count - 1; i >= 0; i--)
            {
                int sep = customTierEntries[i].IndexOf('#');
                if (sep > 0 && sep < customTierEntries[i].Length - 1
                    && customTierEntries[i].Substring(sep + 1) == name)
                {
                    customTierEntries.RemoveAt(i);
                }
            }
            customTierMap.Remove(name);
        }

        /// <summary>
        /// 查询某 Pawn 是否有自定义评级。
        /// 命中时返回 true 并赋值 tier。
        /// </summary>
        public static bool TryGetCustomTier(string name, out CombatTier tier)
        {
            if (string.IsNullOrEmpty(name))
            {
                tier = CombatTier.X;
                return false;
            }
            return customTierMap.TryGetValue(name, out tier);
        }

        public static void DrawSettings(Rect inRect)
        {
            // 双列布局：内容压缩到约 460f 高
            // 保留 ScrollView 作为极小窗口的安全兜底
            float contentHeight = 540f;  // 右列新增排序选项区，预留更多高度
            Rect scrollRect = new Rect(inRect.x, inRect.y, inRect.width, inRect.height);
            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, contentHeight);

            Widgets.BeginScrollView(scrollRect, ref settingsScrollPos, viewRect);

            // 分左右两列：左列放主要开关，右列放副武器/阈值/调试
            float colWidth = (viewRect.width - 12f) * 0.5f;
            Rect leftCol = new Rect(0f, 0f, colWidth, contentHeight);
            Rect rightCol = new Rect(colWidth + 12f, 0f, colWidth, contentHeight);

            // ===================== 左列 =====================
            Listing_Standard l = new Listing_Standard();
            l.Begin(leftCol);

            l.CheckboxLabeled("AE_Enabled".Translate(), ref enabled);
            if (!enabled) { l.End(); Widgets.EndScrollView(); return; }

            l.GapLine();
            l.Label("AE_AutoSystems".Translate());
            l.CheckboxLabeled("AE_AutoWeapons".Translate(), ref autoWeapons);
            l.CheckboxLabeled("AE_AutoApparel".Translate(), ref autoApparel);
            l.CheckboxLabeled("AE_AutoInventory".Translate(), ref autoInventory);
            l.CheckboxLabeled("AE_Sidearms".Translate(), ref sidearms);
            l.CheckboxLabeled("AE_AutoWork".Translate(), ref autoWorkEnabled);

            l.GapLine();
            l.Label("AE_Context".Translate());
            l.CheckboxLabeled("AE_CombatSwitch".Translate(), ref combatSwitch);
            l.CheckboxLabeled("AE_HuntingWeapon".Translate(), ref huntingWeapon);
            l.CheckboxLabeled("AE_TemperatureAware".Translate(), ref temperatureAware);
            l.CheckboxLabeled("AE_JobAwareApparel".Translate(), ref jobAwareApparel);

            // 预设方案选择（重构新增）
            l.GapLine();
            l.Label("AE_Preset".Translate() + ": " + ("AE_Preset_" + GearPolicyEngine.ActivePreset).Translate());
            Rect presetRect = l.GetRect(30f);
            float presetBtnWidth = (presetRect.width - 8f) * 0.5f;
            if (Widgets.ButtonText(new Rect(presetRect.x, presetRect.y, presetBtnWidth, 30f), "AE_Preset_Cycle".Translate()))
            {
                // 循环切换预设：Standard → Aggressive → Economic → Hunting → Standard
                int next = ((int)GearPolicyEngine.ActivePreset + 1) % 4;
                GearPolicyEngine.SwitchPreset((GearPreset)next);
            }
            if (Widgets.ButtonText(new Rect(presetRect.x + presetBtnWidth + 8f, presetRect.y, presetBtnWidth, 30f), "AE_Preset_Details".Translate()))
            {
                Find.WindowStack.Add(new PresetDetailsWindow());
            }

            l.End();

            // ===================== 右列 =====================
            Listing_Standard r = new Listing_Standard();
            r.Begin(rightCol);

            r.Label("AE_SidearmSettings".Translate());
            r.CheckboxLabeled("AE_AutoMelee".Translate(), ref autoMeleeSidearm);
            r.CheckboxLabeled("AE_CarryMedicine".Translate(), ref carryMedicine);
            if (carryMedicine)
            {
                r.Label("AE_MedicineCount".Translate() + ": " + medicineCount);
                medicineCount = (int)r.Slider(medicineCount, 1, 10);
            }

            r.GapLine();
            r.Label("AE_UpgradeThreshold".Translate() + ": " + (upgradeThreshold * 100f).ToString("F0") + "%");
            upgradeThreshold = r.Slider(upgradeThreshold, 0.05f, 0.50f);

            r.GapLine();
            r.CheckboxLabeled("AE_DebugLogging".Translate(), ref debugLogging, "AE_DebugLogging_Desc".Translate());

            // 殖民者栏默认排序方式：点击按钮弹 FloatMenu 选择
            // 设计：玩家配置好默认排序后，ITab "全局人物评级"按钮一键应用
            r.GapLine();
            r.Label("AE_DefaultSortMode".Translate());
            Rect sortBtnRect = r.GetRect(28f);
            if (Widgets.ButtonText(sortBtnRect, GetSortModeLabel(defaultSortMode)))
            {
                var sortOptions = new List<FloatMenuOption>
                {
                    new FloatMenuOption(GetSortModeLabel(ColonistBarSortMode.None),
                        () => defaultSortMode = ColonistBarSortMode.None),
                    new FloatMenuOption(GetSortModeLabel(ColonistBarSortMode.ByTierThenValue),
                        () => defaultSortMode = ColonistBarSortMode.ByTierThenValue),
                    new FloatMenuOption(GetSortModeLabel(ColonistBarSortMode.ByRoleThenTier),
                        () => defaultSortMode = ColonistBarSortMode.ByRoleThenTier),
                    new FloatMenuOption(GetSortModeLabel(ColonistBarSortMode.ByCombatValue),
                        () => defaultSortMode = ColonistBarSortMode.ByCombatValue)
                };
                Find.WindowStack.Add(new FloatMenu(sortOptions));
            }
            TooltipHandler.TipRegion(sortBtnRect, "AE_TT_DefaultSortMode".Translate());

            // 监测开关（重构新增）
            r.GapLine();
            r.Label("AE_Monitor".Translate());
            r.CheckboxLabeled("AE_Monitor_Enabled".Translate(), ref DebugMonitor.monitorEnabled);
            if (DebugMonitor.monitorEnabled)
            {
                r.CheckboxLabeled("AE_Monitor_SwapEvents".Translate(), ref DebugMonitor.monitorSwapEvents);
                r.CheckboxLabeled("AE_Monitor_WeaponScore".Translate(), ref DebugMonitor.monitorWeaponScore);
                r.CheckboxLabeled("AE_Monitor_ApparelScore".Translate(), ref DebugMonitor.monitorApparelScore);
                r.CheckboxLabeled("AE_Monitor_Breakdown".Translate(), ref DebugMonitor.monitorBreakdown);
                r.CheckboxLabeled("AE_Monitor_Comparison".Translate(), ref DebugMonitor.monitorComparison);
            }

            // 调试工具：仅保留一键换装，食尸鬼清理已由 CompTick 自动处理
            r.GapLine();
            r.Label("AE_DebugTools".Translate());

            Rect btnRect = r.GetRect(30f);
            if (Widgets.ButtonText(btnRect, "AE_DebugReload".Translate()))
            {
                // 立即触发所有玩家阵营 Pawn 的全装备评估
                int triggered = CompGearManager.ReloadAllColonists();
                Messages.Message(
                    "AE_DebugReloadResultSimple".Translate(triggered),
                    MessageTypeDefOf.TaskCompletion);
            }

            r.End();

            Widgets.EndScrollView();
        }
    }
}
