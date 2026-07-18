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
        public static bool autoMarkPawn = true;           // 高价值非殖民者星标标记（S+ 追加 ★，事件触发 + 每帧 Postfix 绘制）
        // 注：autoFoodPolicyEnabled / autoDrugPolicyEnabled 已移除（AutoFood/AutoDrug 模块与其他 MOD 冲突）

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
            // 单列布局：内容较少，无需双列
            float contentHeight = 510f;
            Rect scrollRect = new Rect(inRect.x, inRect.y, inRect.width, inRect.height);
            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, contentHeight);

            Widgets.BeginScrollView(scrollRect, ref settingsScrollPos, viewRect);

            Listing_Standard l = new Listing_Standard();
            l.Begin(viewRect);

            l.CheckboxLabeled("AE_Enabled".Translate(), ref enabled);
            if (!enabled) { l.End(); Widgets.EndScrollView(); return; }

            l.GapLine();
            l.Label("AE_AutoSystems".Translate());
            l.CheckboxLabeled("AE_AutoWork".Translate(), ref autoWorkEnabled);
            l.CheckboxLabeled("AE_AutoTierTag".Translate(), ref autoTierTag);
            l.CheckboxLabeled("AE_AutoMarkPawn".Translate(), ref autoMarkPawn);

            // 战斗价值公式权重
            l.GapLine();
            l.Label("AE_CombatValueWeights".Translate());
            l.Label("AE_cvSkillWeight".Translate() + ": " + cvSkillWeight.ToString("F2"));
            cvSkillWeight = l.Slider(cvSkillWeight, 0.1f, 3.0f);
            l.Label("AE_cvPassionNoneMult".Translate() + ": " + cvPassionNoneMult.ToString("F2"));
            cvPassionNoneMult = l.Slider(cvPassionNoneMult, 0.1f, 3.0f);
            l.Label("AE_cvPassionMinorMult".Translate() + ": " + cvPassionMinorMult.ToString("F2"));
            cvPassionMinorMult = l.Slider(cvPassionMinorMult, 0.1f, 3.0f);
            l.Label("AE_cvPassionMajorMult".Translate() + ": " + cvPassionMajorMult.ToString("F2"));
            cvPassionMajorMult = l.Slider(cvPassionMajorMult, 0.1f, 3.0f);
            l.Label("AE_cvToughBonus".Translate() + ": " + cvToughBonus.ToString("F0"));
            cvToughBonus = l.Slider(cvToughBonus, 0f, 100f);
            l.Label("AE_cvTriggerHappyPenalty".Translate() + ": " + cvTriggerHappyPenalty.ToString("F0"));
            cvTriggerHappyPenalty = l.Slider(cvTriggerHappyPenalty, -50f, 0f);
            l.Label("AE_cvCarefulShooterBonus".Translate() + ": " + cvCarefulShooterBonus.ToString("F0"));
            cvCarefulShooterBonus = l.Slider(cvCarefulShooterBonus, 0f, 50f);

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
    }
}
