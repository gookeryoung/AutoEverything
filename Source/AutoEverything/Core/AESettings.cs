using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.Core
{
    /// <summary>
    /// 主 Mod 设置：开关、评级阈值、自定义评级、Mod 选项窗口。
    /// 评级标签相关逻辑（Nick 前缀应用/清除、殖民者栏重排）见 <see cref="AESettings.TierTag.cs"/>。
    /// </summary>
    public partial class AESettings : ModSettings
    {
        // 主开关
        public static bool enabled = true;
        public static bool autoWorkEnabled = true;       // AutoWork 自动工作分配主开关
        public static bool autoTierTag = true;           // 人员自动评级（周期触发 + 新增人员触发）
        public static bool autoMarkPawn = true;           // 高价值自动标记（S+ 人类单位扫描通知 + 殖民者栏角色定位图标：前排/远程/手工/贸易，事件触发 + ITab 切换全局重扫描）
        // 自动携带：默认关闭——携带是侵入性功能（会派发 HaulJob 打断当前工作），玩家主动开启更安全
        public static bool autoCarryEnabled = false;      // AutoCarry 自动携带（食物 x3 + 药品按政策 takeToInventory + S 档血清，周期 6000 tick + 战斗过滤 + 医疗守卫）

        // 用药方案自动配置：默认关闭——会修改殖民者的 DrugPolicy 分配（覆盖玩家手动设置），玩家主动开启更安全
        public static bool autoDrugPolicyEnabled = false; // AutoDrugPolicy 用药方案（AE-S/AE-AB/AE-CDX 三套政策按评级自动分配）

        // 自定义战斗评级识别码
        // 设计：玩家可为指定殖民者手动指定档次，跳过自动公式计算
        //   场景：玩家认为某个 S 档殖民者特别关键，强制锁为 S；
        //         或某 X 档小人有特殊价值，需提升到 A
        //   存档载体：List<string>，元素格式 "档次#Pawn名字"，如 "S#王五"
        //   运行时：解析为 Dictionary<名字, CombatTier> 供快速查询
        //   命中自定义评级的 Pawn 跳过自动评级逻辑，直接采用指定档次
        public static List<string> customTierEntries = new List<string>();
        public static Dictionary<string, CombatTier> customTierMap = new Dictionary<string, CombatTier>();

        // 调试
        public static bool debugLogging = false;       // 详细日志开关

        // 殖民者栏默认排序方式：玩家在 Mod 选项里配置，
        // ITab "全局人物评级"按钮一键应用评级前缀并按此排序重排
        // 默认 ByTierThenValue：按评级 SABCDX 从左到右，最符合评级前缀的视觉直觉
        public static ColonistBarSortMode defaultSortMode = ColonistBarSortMode.ByTierThenValue;

        // 殖民者栏按角色分组：把头像按角色定位（格斗者/射手/医生/工人…）分成独立分组，
        // 复用原生分组框/组内拖拽，便于统一管理。默认关闭——改变殖民者栏视觉布局，玩家主动开启
        public static bool groupColonistBarByRole = false;

        // 殖民者栏头像标识图标（分组标记页可调）：
        // 显示开关控制是否绘制；条件字段为白名单——勾选且命中任一条件即显示该图标
        public static bool showRoleIconTough = true;
        public static bool showRoleIconRanged = true;
        public static bool showRoleIconWorker = true;
        public static bool showRoleIconTrader = true;

        public static bool iconRangedShootingSpecialist = true;
        public static bool iconRangedTriggerHappy = true;
        public static bool iconRangedShootingMajor = true;
        public static bool iconRangedShootingMinor = true;

        public static bool iconWorkerPacifist = true;
        public static bool iconWorkerProductionSpecialist = true;
        public static bool iconWorkerIndustrious = true;
        public static bool iconWorkerNeurotic = true;
        public static bool iconWorkerCraftingMajor = true;

        public static bool iconTraderMoralist = true;
        public static bool iconTraderBeauty = true;
        public static bool iconTraderSocialMajor = true;
        public static bool iconTraderSocialLevel8 = true;

        // 设置窗口滚动位置：内容超出窗口高度时使用
        private static Vector2 settingsScrollPos = Vector2.zero;

        public override void ExposeData()
        {
            // Scribe Key 统一加 ae_ 前缀，符合项目规范
            // 兼容旧存档：加载时先读无前缀旧键，再读 ae_ 新键（若存在则覆盖）
            // 保存时只写 ae_ 新键，旧存档升级后自动迁移
            LookCompat(ref enabled, "enabled", true);
            LookCompat(ref debugLogging, "debugLogging", false);
            LookCompat(ref autoWorkEnabled, "autoWorkEnabled", true);
            LookCompat(ref autoTierTag, "autoTierTag", true);
            LookCompat(ref autoMarkPawn, "autoMarkPawn", true);
            LookCompat(ref autoCarryEnabled, "autoCarryEnabled", false);
            LookCompat(ref autoDrugPolicyEnabled, "autoDrugPolicyEnabled", false);

            // 殖民者栏默认排序方式
            Scribe_Values.Look(ref defaultSortMode, "ae_defaultSortMode", ColonistBarSortMode.ByTierThenValue);
            // 旧存档可能保存了已移除的"按战斗价值"模式，统一迁移为按评级排序
            if (defaultSortMode == ColonistBarSortMode.ByCombatValue)
                defaultSortMode = ColonistBarSortMode.ByTierThenValue;

            // 殖民者栏按角色分组
            Scribe_Values.Look(ref groupColonistBarByRole, "ae_groupColonistBarByRole", false);

            // 殖民者栏头像标识图标：显示开关 + 白名单条件
            LookCompat(ref showRoleIconTough, "showRoleIconTough", true);
            LookCompat(ref showRoleIconRanged, "showRoleIconRanged", true);
            LookCompat(ref showRoleIconWorker, "showRoleIconWorker", true);
            LookCompat(ref showRoleIconTrader, "showRoleIconTrader", true);
            LookCompat(ref iconRangedShootingSpecialist, "iconRangedShootingSpecialist", true);
            LookCompat(ref iconRangedTriggerHappy, "iconRangedTriggerHappy", true);
            LookCompat(ref iconRangedShootingMajor, "iconRangedShootingMajor", true);
            LookCompat(ref iconRangedShootingMinor, "iconRangedShootingMinor", true);
            LookCompat(ref iconWorkerPacifist, "iconWorkerPacifist", true);
            LookCompat(ref iconWorkerProductionSpecialist, "iconWorkerProductionSpecialist", true);
            LookCompat(ref iconWorkerIndustrious, "iconWorkerIndustrious", true);
            LookCompat(ref iconWorkerNeurotic, "iconWorkerNeurotic", true);
            LookCompat(ref iconWorkerCraftingMajor, "iconWorkerCraftingMajor", true);
            LookCompat(ref iconTraderMoralist, "iconTraderMoralist", true);
            LookCompat(ref iconTraderBeauty, "iconTraderBeauty", true);
            LookCompat(ref iconTraderSocialMajor, "iconTraderSocialMajor", true);
            LookCompat(ref iconTraderSocialLevel8, "iconTraderSocialLevel8", true);

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

            // 判定参数（底部【自动】面板可调）：独立 partial 维护，见 AESettings.Judgement.cs
            ExposeJudgementData();

            base.ExposeData();
        }

        /// <summary>
        /// 兼容性 Scribe 读取：
        /// - 加载模式：先读无前缀旧键（兼容旧存档），再读 ae_ 新键（若存在则覆盖，缺失时保持旧键结果）
        /// - 保存模式：只写 ae_ 新键，实现自动迁移
        /// 关键：保存时必须传真实默认值——Scribe_Values 在 value == defaultValue 时会跳过写盘，
        /// 若把 value 自身当默认值传入，所有标量键将永远不会写入（曾导致开关重启丢失）
        /// </summary>
        private static void LookCompat(ref bool value, string key, bool defaultValue)
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                // 等于默认值时省略键；加载时键缺失自然回落默认值，语义自洽
                Scribe_Values.Look(ref value, "ae_" + key, defaultValue);
            }
            else if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                Scribe_Values.Look(ref value, key, defaultValue);
                Scribe_Values.Look(ref value, "ae_" + key, value);
            }
        }

        private static void LookCompat(ref float value, string key, float defaultValue)
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
            // 紧凑布局：单行 label+value + 单行 slider 的双行模式
            // 估算内容高度：基础段 + cv 权重 7 项 + 排序按钮段 + AutoCarry 勾选框 1 项
            float contentHeight = 772f;
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
            l.CheckboxLabeled("AE_AutoCarry".Translate(), ref autoCarryEnabled);
            l.CheckboxLabeled("AE_AutoDrugPolicy".Translate(), ref autoDrugPolicyEnabled);

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
                        () => defaultSortMode = ColonistBarSortMode.ByRoleThenTier)
                };
                Find.WindowStack.Add(new FloatMenu(sortOptions));
            }
            TooltipHandler.TipRegion(sortBtnRect, "AE_TT_DefaultSortMode".Translate());

            // 按角色分组及判定阈值已迁移到游戏内底部【自动】→ 人员自动面板

            l.End();

            Widgets.EndScrollView();
        }

        /// <summary>
        /// "殖民者栏按角色分组"开关变化回调：
        /// - 开启：先按角色+评级重排 displayOrder（同角色相邻是分组连续的前提），
        ///   ReorderColonistBar 内部已调 MarkColonistsDirty 触发重建分组
        /// - 关闭：仅 MarkColonistsDirty，原生重建后保留原生地图/远行队分组
        /// </summary>
        public static void OnGroupByRoleChanged()
        {
            if (Current.ProgramState != ProgramState.Playing || Find.ColonistBar == null) return;

            if (groupColonistBarByRole)
            {
                ReorderColonistBar(ComparePawnByRoleThenTierDesc);
            }
            else
            {
                Find.ColonistBar.MarkColonistsDirty();
            }
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
