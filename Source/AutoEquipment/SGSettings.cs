using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using AutoEquipment.Scoring;

namespace AutoEquipment
{
    public class AESettings : ModSettings
    {
        // 主开关
        public static bool enabled = true;
        public static bool autoWeapons = true;
        public static bool autoApparel = true;
        public static bool autoInventory = true;
        public static bool sidearms = true;

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

        // 调试
        public static bool debugLogging = false;       // 详细日志开关

        // 预设方案（重构后新增）
        // 由 GearPolicyEngine 维护，此处仅作存档载体
        // 实际值通过 GearPolicyEngine.SwitchPreset 设置

        // 设置窗口滚动位置：内容超出窗口高度时使用
        private static Vector2 settingsScrollPos = Vector2.zero;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref enabled, "enabled", true);
            Scribe_Values.Look(ref autoWeapons, "autoWeapons", true);
            Scribe_Values.Look(ref autoApparel, "autoApparel", true);
            Scribe_Values.Look(ref autoInventory, "autoInventory", true);
            Scribe_Values.Look(ref sidearms, "sidearms", true);
            Scribe_Values.Look(ref combatSwitch, "combatSwitch", true);
            Scribe_Values.Look(ref huntingWeapon, "huntingWeapon", true);
            Scribe_Values.Look(ref temperatureAware, "temperatureAware", true);
            Scribe_Values.Look(ref jobAwareApparel, "jobAwareApparel", true);
            Scribe_Values.Look(ref autoMeleeSidearm, "autoMeleeSidearm", true);
            Scribe_Values.Look(ref carryMedicine, "carryMedicine", true);
            Scribe_Values.Look(ref medicineCount, "medicineCount", 3);
            Scribe_Values.Look(ref evaluateInterval, "evaluateInterval", 500);
            Scribe_Values.Look(ref upgradeThreshold, "upgradeThreshold", 0.15f);
            Scribe_Values.Look(ref tempDangerMargin, "tempDangerMargin", 5f);
            Scribe_Values.Look(ref debugLogging, "debugLogging", false);

            // 预设方案与监测开关由 GearPolicyEngine/DebugMonitor 持久化
            // 此处委托其存档
            GearPolicyEngine.ExposeData();
            DebugMonitor.ExposeData();

            base.ExposeData();
        }

        public static void DrawSettings(Rect inRect)
        {
            // 双列布局：内容压缩到约 460f 高
            // 保留 ScrollView 作为极小窗口的安全兜底
            float contentHeight = 460f;
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

    public class AutoEquipmentMod : Mod
    {
        public static AESettings settings;

        public AutoEquipmentMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<AESettings>();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            AESettings.DrawSettings(inRect);
        }

        public override string SettingsCategory() => "AE_SettingsCategory".Translate();
    }

    /// <summary>
    /// 预设方案详情窗口：显示当前预设权重并允许微调。
    /// </summary>
    public class PresetDetailsWindow : Window
    {
        private Vector2 scrollPos = Vector2.zero;

        public override Vector2 InitialSize => new Vector2(420f, 480f);

        public PresetDetailsWindow()
        {
            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            forcePause = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Listing_Standard l = new Listing_Standard();
            l.Begin(inRect);

            Text.Font = GameFont.Medium;
            l.Label("AE_Preset_Details".Translate());
            Text.Font = GameFont.Small;
            l.Gap();

            // 显示当前预设
            l.Label("AE_Preset".Translate() + ": " + ("AE_Preset_" + GearPolicyEngine.ActivePreset).Translate());
            l.GapLine();

            // 显示该预设的默认权重
            GearWeights defaultW = GearPolicyEngine.ActivePreset.GetDefaultWeights();
            l.Label("AE_Preset_DefaultWeights".Translate());
            l.Label($"  {("AE_Weight_Skill".Translate())}: {defaultW.w_skill:F1}");
            l.Label($"  {("AE_Weight_DPS".Translate())}: {defaultW.w_dps:F1}");
            l.Label($"  {("AE_Weight_Range".Translate())}: {defaultW.w_range:F1}");
            l.Label($"  {("AE_Weight_Quality".Translate())}: {defaultW.w_quality:F1}");
            l.Label($"  {("AE_Weight_Armor".Translate())}: {defaultW.w_armor:F1}");
            l.Label($"  {("AE_Weight_Insulation".Translate())}: {defaultW.w_insulation:F1}");
            l.Label($"  {("AE_Weight_MoveSpeed".Translate())}: {defaultW.w_movespeed:F1}");
            l.Label($"  {("AE_Weight_WorkSpeed".Translate())}: {defaultW.w_workspeed:F1}");

            l.End();
        }
    }
}
