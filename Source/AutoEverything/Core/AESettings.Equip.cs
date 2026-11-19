using System;
using System.Collections.Generic;
using AutoEverything.AutoEquip;
using RimWorld;
using Verse;

namespace AutoEverything.Core
{
    /// <summary>
    /// 自动装备相关配置。独立 partial 维护，避免主设置文件继续膨胀。
    /// 默认关闭：自动换装会派发 Job，必须先由玩家显式开启。
    /// </summary>
    public partial class AESettings
    {
        public static bool autoEquipEnabled = false;
        public static bool autoEquipArmor = true;
        // 难民/逃兵等任务临时成员离开殖民地时会带走身上的装备，默认不为他们自动装备
        public static bool equipAllowQuestLodgers = false;
        public static ApparelPreset equipArmorPreset = ApparelPreset.Balanced;
        public static int equipMinApparelDurabilityPct = 25;
        public static int equipArmorMaxDistance = 40;
        public static bool equipCombatProfileEnabled = false;
        public static bool equipArmorNudistException = false;
        public static int equipIntervalTicks = 6000;
        public static QualityCategory equipMinQuality = QualityCategory.Awful;
        public static int equipMinImprovementPct = 10;
        public static int equipMaxThrownKinds = 2;

        // 白名单以字符串列表持久化，格式见 EquipRules.Serialize
        public static List<string> equipRoleEntries = EquipRules.CreateDefaultEntries();

        private static void ExposeEquipData()
        {
            LookCompat(ref autoEquipEnabled, "ae_autoEquipEnabled", false);
            LookCompat(ref autoEquipArmor, "ae_autoEquipArmor", true);
            LookCompat(ref equipAllowQuestLodgers, "ae_equipAllowQuestLodgers", false);

            Scribe_Values.Look(ref equipArmorPreset, "ae_equipArmorPreset", ApparelPreset.Balanced);
            Scribe_Values.Look(ref equipMinApparelDurabilityPct, "ae_equipMinApparelDurabilityPct", 25);
            Scribe_Values.Look(ref equipArmorMaxDistance, "ae_equipArmorMaxDistance", 40);
            Scribe_Values.Look(ref equipCombatProfileEnabled, "ae_equipCombatProfileEnabled", false);
            Scribe_Values.Look(ref equipArmorNudistException, "ae_equipArmorNudistException", false);
            Scribe_Values.Look(ref equipIntervalTicks, "ae_equipIntervalTicks", 6000);
            Scribe_Values.Look(ref equipMinQuality, "ae_equipMinQuality", QualityCategory.Awful);
            Scribe_Values.Look(ref equipMinImprovementPct, "ae_equipMinImprovementPct", 10);
            Scribe_Values.Look(ref equipMaxThrownKinds, "ae_equipMaxThrownKinds", 2);

            Scribe_Collections.Look(ref equipRoleEntries, "ae_equipRoleEntries", LookMode.Value);
            equipRoleEntries = EquipRules.Normalize(equipRoleEntries);

            equipIntervalTicks = ClampEquipInterval(equipIntervalTicks);
            equipMinImprovementPct = Math.Max(0, Math.Min(50, equipMinImprovementPct));
            equipMaxThrownKinds = Math.Max(0, Math.Min(4, equipMaxThrownKinds));
            equipMinApparelDurabilityPct = Math.Max(0, Math.Min(90, equipMinApparelDurabilityPct));
            equipArmorMaxDistance = Math.Max(5, Math.Min(80, equipArmorMaxDistance));
        }

        public static EquipRoleRule GetEquipRoleRule(int role)
        {
            return EquipRules.GetRule(equipRoleEntries, role);
        }

        public static void SetEquipRoleRule(int role, EquipRoleRule rule)
        {
            if (equipRoleEntries == null)
                equipRoleEntries = EquipRules.CreateDefaultEntries();
            EquipRules.SetRule(equipRoleEntries, role, rule);
        }

        public static void ResetEquipRulesToDefault()
        {
            equipRoleEntries = EquipRules.CreateDefaultEntries();
        }

        public static int ClampEquipInterval(int ticks)
        {
            // 30 秒 ~ 300 秒，避免过于频繁扫描或长时间不更新
            return Math.Max(1800, Math.Min(18000, ticks));
        }
    }
}
