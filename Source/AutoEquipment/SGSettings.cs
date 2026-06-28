using UnityEngine;
using Verse;

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
            base.ExposeData();
        }

        public static void DrawSettings(Rect inRect)
        {
            Listing_Standard l = new Listing_Standard();
            l.Begin(inRect);

            l.CheckboxLabeled("AE_Enabled".Translate(), ref enabled);
            if (!enabled) { l.End(); return; }

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

            l.GapLine();
            l.Label("AE_SidearmSettings".Translate());
            l.CheckboxLabeled("AE_AutoMelee".Translate(), ref autoMeleeSidearm);
            l.CheckboxLabeled("AE_CarryMedicine".Translate(), ref carryMedicine);
            if (carryMedicine)
            {
                l.Label("AE_MedicineCount".Translate() + ": " + medicineCount);
                medicineCount = (int)l.Slider(medicineCount, 1, 10);
            }

            l.GapLine();
            l.Label("AE_UpgradeThreshold".Translate() + ": " + (upgradeThreshold * 100f).ToString("F0") + "%");
            upgradeThreshold = l.Slider(upgradeThreshold, 0.05f, 0.50f);

            l.GapLine();
            l.CheckboxLabeled("AE_DebugLogging".Translate(), ref debugLogging, "AE_DebugLogging_Desc".Translate());

            l.End();
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

        public override string SettingsCategory() => "Auto Equipment";
    }
}
