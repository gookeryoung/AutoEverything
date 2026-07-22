using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.Core
{
    /// <summary>
    /// 主 Mod 设置：开关、战斗价值权重、自定义评级、Mod 选项窗口。
    /// 评级标签相关逻辑（Nick 前缀应用/清除、殖民者栏重排）见 <see cref="AESettings.TierTag.cs"/>。
    /// </summary>
    public partial class AESettings : ModSettings
    {
        // 主开关
        public static bool enabled = true;
        public static bool autoWorkEnabled = true;       // AutoWork 自动工作分配主开关
        public static bool autoTierTag = true;           // 人员自动评级（周期触发 + 新增人员触发）
        public static bool autoMarkPawn = true;           // 高价值自动标记（S+ 人类单位扫描通知 + 殖民者栏角色定位图标：前排/远程/手工/贸易，事件触发 + ITab 切换全局重扫描）
        public static bool autoEquipmentEnabled = false;  // AutoEquipment 自动装备分配（事件驱动 + ITab 切换全局重分配，默认关闭避免误扒装）

        // 战斗价值公式可调权重（用于评级排序与高价值标记判定）
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

        // AutoEquipment 自动装备评分权重（可调）
        // 评分公式：score = armorScore + layerMatchScore + insulationScore - movementPenalty
        //   armorScore = (Sharp+Blunt+Heat)/3 × geArmorWeight
        //   layerMatchScore：按 ArmorPreference 重甲/轻甲/自由分别加权
        //   insulationScore：极端温度下保暖/隔热值 × geInsulationWeight
        //   movementPenalty = (apparel Mass / 5.0) × 角色敏感度权重（归一化 mass 到 0~1，与 armorScore 量级匹配）
        public static float geArmorWeight = 1.0f;                    // 护甲值得分权重
        public static float geHeavyArmorMatchWeight = 2.0f;          // 前排（Heavy）重甲契合权重
        public static float geLightArmorMatchWeight = 1.5f;          // 工人（Light）轻甲契合权重
        public static float geLightArmorAvoidWeight = 0.5f;          // 工人（Light）重甲避讳权重
        public static float geFlexibleArmorMatchWeight = 1.0f;       // 后排（Flexible）自由契合权重
        public static float geInsulationWeight = 1.0f;               // 保暖/隔热契合权重
        public static float geWorkerMovePenaltyWeight = 3.0f;        // 工人移动减损敏感度
        public static float geBackRowMovePenaltyWeight = 2.0f;       // 后排移动减损敏感度
        public static float geFrontRowMovePenaltyWeight = 0.5f;      // 前排移动减损敏感度
        public static float geReplaceThreshold = 0.06f;              // 替换阈值：新 apparel 分数比已穿戴高此值才替换（默认 0.06：同层装备护甲差通常 0.05~0.15，0.5 会阻断细微升级；0.06 让 Worker 头盔层降级后差值 ~0.097 也能触发换装）
        public static float geHeavyArmorThreshold = 1.0f;            // 重甲判定阈值：apparel 的 (Sharp+Blunt) ≥ 此值视为重甲，用于顺延名额计算
        public static bool geAutoUnforbidApparel = false;           // 自动取消装备禁止标记：开启后候选收集时自动取消 Forbidden 标记，让系统可选用被禁用的装备（默认关闭，尊重玩家 Forbid 意图）

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

        // 设置窗口滚动位置：内容超出窗口高度时使用
        private static Vector2 settingsScrollPos = Vector2.zero;

        public override void ExposeData()
        {
            // Scribe Key 统一加 ae_ 前缀，符合项目规范
            // 兼容旧存档：加载时先读无前缀旧键，再读 ae_ 新键（若存在则覆盖）
            // 保存时只写 ae_ 新键，旧存档升级后自动迁移
            LookCompat(ref enabled, "enabled", true);
            LookCompat(ref cvSkillWeight, "cvSkillWeight", 1.0f);
            LookCompat(ref cvPassionNoneMult, "cvPassionNoneMult", 1.0f);
            LookCompat(ref cvPassionMinorMult, "cvPassionMinorMult", 1.5f);
            LookCompat(ref cvPassionMajorMult, "cvPassionMajorMult", 2.0f);
            LookCompat(ref cvToughBonus, "cvToughBonus", 30f);
            LookCompat(ref cvTriggerHappyPenalty, "cvTriggerHappyPenalty", -15f);
            LookCompat(ref cvCarefulShooterBonus, "cvCarefulShooterBonus", 15f);
            LookCompat(ref debugLogging, "debugLogging", false);
            LookCompat(ref autoWorkEnabled, "autoWorkEnabled", true);
            LookCompat(ref autoTierTag, "autoTierTag", true);
            LookCompat(ref autoMarkPawn, "autoMarkPawn", true);
            LookCompat(ref autoEquipmentEnabled, "autoEquipmentEnabled", false);

            // AutoEquipment 评分权重持久化（ge_ 前缀）
            LookCompat(ref geArmorWeight, "geArmorWeight", 1.0f);
            LookCompat(ref geHeavyArmorMatchWeight, "geHeavyArmorMatchWeight", 2.0f);
            LookCompat(ref geLightArmorMatchWeight, "geLightArmorMatchWeight", 1.5f);
            LookCompat(ref geLightArmorAvoidWeight, "geLightArmorAvoidWeight", 0.5f);
            LookCompat(ref geFlexibleArmorMatchWeight, "geFlexibleArmorMatchWeight", 1.0f);
            LookCompat(ref geInsulationWeight, "geInsulationWeight", 1.0f);
            LookCompat(ref geWorkerMovePenaltyWeight, "geWorkerMovePenaltyWeight", 3.0f);
            LookCompat(ref geBackRowMovePenaltyWeight, "geBackRowMovePenaltyWeight", 2.0f);
            LookCompat(ref geFrontRowMovePenaltyWeight, "geFrontRowMovePenaltyWeight", 0.5f);
            LookCompat(ref geReplaceThreshold, "geReplaceThreshold", 0.06f);
            LookCompat(ref geHeavyArmorThreshold, "geHeavyArmorThreshold", 1.0f);
            LookCompat(ref geAutoUnforbidApparel, "geAutoUnforbidApparel", false);

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
            // 紧凑布局：装备评分权重较多，单行 label+value + 单行 slider 的双行模式
            // 估算内容高度：基础段 + cv 权重 7 项 + ge 权重 13 项 + 排序按钮段
            float contentHeight = 1240f;
            Rect scrollRect = new Rect(inRect.x, inRect.y, inRect.width, inRect.height);
            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, contentHeight);

            Widgets.BeginScrollView(scrollRect, ref settingsScrollPos, viewRect);

            Listing_Standard l = new Listing_Standard();
            l.Begin(viewRect);

            l.CheckboxLabeled("AE_Enabled".Translate(), ref enabled);
            if (!enabled) { l.End(); Widgets.EndScrollView(); return; }

            // 自动系统段
            l.GapLine();
            l.Label("AE_AutoSystems".Translate());
            l.CheckboxLabeled("AE_AutoWork".Translate(), ref autoWorkEnabled);
            l.CheckboxLabeled("AE_AutoTierTag".Translate(), ref autoTierTag);
            l.CheckboxLabeled("AE_AutoMarkPawn".Translate(), ref autoMarkPawn);
            l.CheckboxLabeled("AE_AutoEquipment".Translate(), ref autoEquipmentEnabled);

            // 战斗价值公式权重
            l.GapLine();
            l.Label("AE_CombatValueWeights".Translate());
            DrawCompactSlider(l, "AE_cvSkillWeight".Translate(), ref cvSkillWeight, 0.1f, 3.0f);
            DrawCompactSlider(l, "AE_cvPassionNoneMult".Translate(), ref cvPassionNoneMult, 0.1f, 3.0f);
            DrawCompactSlider(l, "AE_cvPassionMinorMult".Translate(), ref cvPassionMinorMult, 0.1f, 3.0f);
            DrawCompactSlider(l, "AE_cvPassionMajorMult".Translate(), ref cvPassionMajorMult, 0.1f, 3.0f);
            DrawCompactSlider(l, "AE_cvToughBonus".Translate(), ref cvToughBonus, 0f, 100f, "F0");
            DrawCompactSlider(l, "AE_cvTriggerHappyPenalty".Translate(), ref cvTriggerHappyPenalty, -50f, 0f, "F0");
            DrawCompactSlider(l, "AE_cvCarefulShooterBonus".Translate(), ref cvCarefulShooterBonus, 0f, 50f, "F0");

            // 装备评分权重
            l.GapLine();
            l.Label("AE_GearWeights".Translate());
            DrawCompactSlider(l, "AE_geArmorWeight".Translate(), ref geArmorWeight, 0.1f, 5.0f);
            DrawCompactSlider(l, "AE_geHeavyArmorMatchWeight".Translate(), ref geHeavyArmorMatchWeight, 0f, 5.0f);
            DrawCompactSlider(l, "AE_geLightArmorMatchWeight".Translate(), ref geLightArmorMatchWeight, 0f, 5.0f);
            DrawCompactSlider(l, "AE_geLightArmorAvoidWeight".Translate(), ref geLightArmorAvoidWeight, 0f, 3.0f);
            DrawCompactSlider(l, "AE_geFlexibleArmorMatchWeight".Translate(), ref geFlexibleArmorMatchWeight, 0f, 5.0f);
            DrawCompactSlider(l, "AE_geInsulationWeight".Translate(), ref geInsulationWeight, 0f, 5.0f);
            DrawCompactSlider(l, "AE_geWorkerMovePenaltyWeight".Translate(), ref geWorkerMovePenaltyWeight, 0f, 10f);
            DrawCompactSlider(l, "AE_geBackRowMovePenaltyWeight".Translate(), ref geBackRowMovePenaltyWeight, 0f, 10f);
            DrawCompactSlider(l, "AE_geFrontRowMovePenaltyWeight".Translate(), ref geFrontRowMovePenaltyWeight, 0f, 5f);
            DrawCompactSlider(l, "AE_geReplaceThreshold".Translate(), ref geReplaceThreshold, 0f, 5f, "F2");
            DrawCompactSlider(l, "AE_geHeavyArmorThreshold".Translate(), ref geHeavyArmorThreshold, 0f, 3f, "F2");
            l.CheckboxLabeled("AE_geAutoUnforbidApparel".Translate(), ref geAutoUnforbidApparel, "AE_geAutoUnforbidApparel_Desc".Translate());

            // 调试
            l.GapLine();
            l.CheckboxLabeled("AE_DebugLogging".Translate(), ref debugLogging, "AE_DebugLogging_Desc".Translate());

            // 殖民者栏默认排序方式：点击按钮弹 FloatMenu 选择
            l.GapLine();
            l.Label("AE_DefaultSortMode".Translate());
            Rect sortBtnRect = l.GetRect(28f);
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

            l.End();

            Widgets.EndScrollView();
        }

        /// <summary>
        /// 紧凑滑块：单行显示 "标签: 数值"，下一行紧跟 slider。
        /// 比默认 l.Label + l.Slider 双行模式节省约 30% 垂直空间。
        /// 关闭 WordWrap 避免中文标签换行。
        /// </summary>
        private static void DrawCompactSlider(Listing_Standard l, string label, ref float value, float min, float max, string format = "F2")
        {
            // 标签行：22f
            Rect labelRect = l.GetRect(22f);
            string text = label + ": " + value.ToString(format);
            bool prevWrap = Text.WordWrap;
            Text.WordWrap = false;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = ColorLabelGray;
            Widgets.Label(labelRect, text);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = prevWrap;

            // 滑块行：20f
            Rect sliderRect = l.GetRect(20f);
            value = Widgets.HorizontalSlider(sliderRect, value, min, max);
        }

        // 标签灰色常量（与 ITab 一致），用于紧凑滑块标签
        private static readonly Color ColorLabelGray = new Color(0.70f, 0.70f, 0.70f);
    }
}
