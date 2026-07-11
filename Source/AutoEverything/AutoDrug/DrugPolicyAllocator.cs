using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;
using AutoEverything.Core;

namespace AutoEverything.AutoDrug
{
    /// <summary>
    /// 用药方案自动分配器：根据殖民者的特质与信仰（文化）分配用药方案。
    ///
    /// 方案列表：
    /// - AE_常规：社交用药（啤酒+烟叶允许娱乐使用）+ 精神茶按 2 天 1 次定时使用，基于 SocialDrugs 模板
    /// - AE_禁药：完全禁药，基于 NoDrugs 模板
    ///
    /// 分配规则：
    /// - 禁酒主义特质（DrugDesire degree=-1）→ AE_禁药
    /// - 信仰 DrugUse_Prohibited / DrugUse_Abhorrent 信条 → AE_禁药
    /// - 其他 → AE_常规
    ///
    /// 触发方式：事件驱动（殖民者数量变化、信仰变化），非周期 Tick。
    /// </summary>
    public static class DrugPolicyAllocator
    {
        private const string NormalLabel = "AE_常规";
        private const string NoDrugsLabel = "AE_禁药";

        private static DrugPolicy _normalPolicy;
        private static DrugPolicy _noDrugsPolicy;

        private static bool _defsInitialized;
        private static TraitDef _drugDesireTrait;
        private static DrugPolicyDef _socialDrugsDef;
        private static DrugPolicyDef _noDrugsDef;
        private static ThingDef _psychiteTeaDef;

        // 反射缓存：DrugPolicy.entriesInt 是 private 字段
        private static FieldInfo _entriesField;

        public static int ReallocateAll()
        {
            EnsurePoliciesExist();
            int count = 0;
            foreach (Pawn pawn in PawnsFinder.AllMaps_FreeColonists)
            {
                if (pawn == null) continue;
                if (pawn.drugs == null) continue;
                DrugPolicy target = DeterminePolicyForPawn(pawn);
                if (target == null) continue;
                if (pawn.drugs.CurrentPolicy != target)
                {
                    pawn.drugs.CurrentPolicy = target;
                    count++;
                }
            }
            return count;
        }

        private static void EnsurePoliciesExist()
        {
            InitDefs();

            DrugPolicyDatabase db = Current.Game.drugPolicyDatabase;
            if (db == null) return;

            _normalPolicy = FindOrCreatePolicyFromDef(db, NormalLabel, _socialDrugsDef);
            _noDrugsPolicy = FindOrCreatePolicyFromDef(db, NoDrugsLabel, _noDrugsDef);

            // 确保 AE_常规 包含精神茶条目（2 天 1 次定时使用）
            EnsurePsychiteTeaEntry(_normalPolicy);
        }

        private static DrugPolicy FindOrCreatePolicyFromDef(DrugPolicyDatabase db, string label, DrugPolicyDef def)
        {
            foreach (DrugPolicy p in db.AllPolicies)
            {
                if (p.label == label) return p;
            }
            DrugPolicy newPolicy = def != null
                ? db.NewDrugPolicyFromDef(def)
                : db.MakeNewDrugPolicy();
            newPolicy.label = label;
            return newPolicy;
        }

        /// <summary>
        /// 确保 AE_常规 方案包含精神茶（PsychiteTea）条目，按 2 天 1 次定时使用。
        /// 已存在精神茶条目时不重复添加（尊重玩家手动修改的参数）。
        /// DrugPolicy.entriesInt 是 private，用反射访问。
        /// </summary>
        private static void EnsurePsychiteTeaEntry(DrugPolicy policy)
        {
            if (_psychiteTeaDef == null) return;
            if (_entriesField == null) return;

            var entries = _entriesField.GetValue(policy) as List<DrugPolicyEntry>;
            if (entries == null) return;

            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].drug == _psychiteTeaDef)
                    return;
            }

            entries.Add(new DrugPolicyEntry
            {
                drug = _psychiteTeaDef,
                allowedForJoy = true,
                allowScheduled = true,
                daysFrequency = 2f,
                takeToInventory = 1
            });
        }

        private static DrugPolicy DeterminePolicyForPawn(Pawn pawn)
        {
            if (_drugDesireTrait != null && pawn.story?.traits != null)
            {
                int degree = pawn.story.traits.DegreeOfTrait(_drugDesireTrait);
                if (degree == -1)
                    return _noDrugsPolicy;
            }

            if (pawn.Ideo != null)
            {
                var precepts = pawn.Ideo.PreceptsListForReading;
                for (int i = 0; i < precepts.Count; i++)
                {
                    string dn = precepts[i].def.defName;
                    if (dn == "DrugUse_Prohibited" || dn == "DrugUse_Abhorrent")
                        return _noDrugsPolicy;
                }
            }

            return _normalPolicy;
        }

        private static void InitDefs()
        {
            if (_defsInitialized) return;
            _defsInitialized = true;

            _drugDesireTrait = TraitDefOf.DrugDesire;
            _socialDrugsDef = DefDatabase<DrugPolicyDef>.GetNamed("SocialDrugs", false);
            _noDrugsDef = DefDatabase<DrugPolicyDef>.GetNamed("NoDrugs", false);
            _psychiteTeaDef = DefDatabase<ThingDef>.GetNamed("PsychiteTea", false);
            _entriesField = typeof(DrugPolicy).GetField("entriesInt", BindingFlags.NonPublic | BindingFlags.Instance);

            if (_socialDrugsDef == null)
                Log.WarningOnce("[AutoEverything] SocialDrugs 用药方案模板未找到", 0xB200);
            if (_noDrugsDef == null)
                Log.WarningOnce("[AutoEverything] NoDrugs 用药方案模板未找到", 0xB300);
            if (_psychiteTeaDef == null)
                Log.WarningOnce("[AutoEverything] PsychiteTea 精神茶未找到，AE_常规将不含精神茶定时使用", 0xB400);
            if (_entriesField == null)
                Log.WarningOnce("[AutoEverything] DrugPolicy.entriesInt 反射失败，精神茶条目无法添加", 0xB410);
        }
    }
}
