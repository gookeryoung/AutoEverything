using System.Collections.Generic;
using RimWorld;
using Verse;
using AutoEverything.Core;

namespace AutoEverything.AutoFood
{
    /// <summary>
    /// 食物方案自动分配器：根据殖民者的特质与信仰（文化）分配食物方案。
    ///
    /// 方案列表：
    /// - AE_常规：禁止人肉/虫肉/生食/尸体/动物饲料，仅允许熟食等正常食物
    /// - AE_人肉：在常规基础上允许人肉（食人族特质或食人主义信条）
    /// - AE_虫肉：在常规基础上允许虫肉（虫肉热爱信条）
    /// "允许"而非"仅含"：方案仍包含常规食物，只是额外允许人肉/虫肉。
    /// AllowCannibal/AllowInsectMeat 特殊过滤器可覆盖 FoodRaw 类别禁止。
    ///
    /// 触发方式：事件驱动（殖民者数量变化、信仰变化），非周期 Tick。
    /// </summary>
    public static class FoodPolicyAllocator
    {
        private const string NormalLabel = "AE_常规";
        private const string HumanMeatLabel = "AE_人肉";
        private const string InsectMeatLabel = "AE_虫肉";

        private static FoodPolicy _normalPolicy;
        private static FoodPolicy _humanMeatPolicy;
        private static FoodPolicy _insectMeatPolicy;

        private static bool _defsInitialized;
        private static TraitDef _cannibalTrait;
        private static SpecialThingFilterDef _allowCannibalFilter;
        private static SpecialThingFilterDef _allowInsectMeatFilter;
        private static ThingCategoryDef _foodRawCategory;
        private static ThingCategoryDef _corpsesCategory;
        private static ThingDef _kibbleDef;

        public static int ReallocateAll()
        {
            EnsurePoliciesExist();
            int count = 0;
            foreach (Pawn pawn in PawnsFinder.AllMaps_FreeColonists)
            {
                if (pawn == null) continue;
                if (pawn.foodRestriction == null) continue;
                if (!pawn.foodRestriction.Configurable) continue;
                FoodPolicy target = DeterminePolicyForPawn(pawn);
                if (target == null) continue;
                if (pawn.foodRestriction.CurrentFoodPolicy != target)
                {
                    pawn.foodRestriction.CurrentFoodPolicy = target;
                    count++;
                }
            }
            return count;
        }

        private static void EnsurePoliciesExist()
        {
            InitDefs();
            FoodRestrictionDatabase db = Current.Game.foodRestrictionDatabase;
            if (db == null) return;

            _normalPolicy = FindOrCreatePolicy(db, NormalLabel);
            _humanMeatPolicy = FindOrCreatePolicy(db, HumanMeatLabel);
            _insectMeatPolicy = FindOrCreatePolicy(db, InsectMeatLabel);

            FoodPolicy defaultPolicy = db.DefaultFoodRestriction();
            if (defaultPolicy == null) return;

            // AE_常规：复制默认方案，然后禁止生食/尸体/动物饲料/人肉/虫肉
            _normalPolicy.filter.CopyAllowancesFrom(defaultPolicy.filter);
            ConfigureNormalFilter(_normalPolicy.filter);

            // AE_人肉：复制常规 + 允许人肉（特殊过滤器覆盖 FoodRaw 类别禁止）
            _humanMeatPolicy.filter.CopyAllowancesFrom(_normalPolicy.filter);
            if (_allowCannibalFilter != null)
                _humanMeatPolicy.filter.SetAllow(_allowCannibalFilter, true);

            // AE_虫肉：复制常规 + 允许虫肉（特殊过滤器覆盖 FoodRaw 类别禁止）
            _insectMeatPolicy.filter.CopyAllowancesFrom(_normalPolicy.filter);
            if (_allowInsectMeatFilter != null)
                _insectMeatPolicy.filter.SetAllow(_allowInsectMeatFilter, true);
        }

        /// <summary>
        /// 配置常规 filter：禁止生食、尸体、动物饲料、人肉、虫肉。
        /// 三种方案共享此基础配置，人肉/虫肉方案通过特殊过滤器覆盖类别禁止。
        /// </summary>
        private static void ConfigureNormalFilter(ThingFilter filter)
        {
            if (_allowCannibalFilter != null)
                filter.SetAllow(_allowCannibalFilter, false);
            if (_allowInsectMeatFilter != null)
                filter.SetAllow(_allowInsectMeatFilter, false);
            if (_foodRawCategory != null)
                filter.SetAllow(_foodRawCategory, false, null, null);
            if (_corpsesCategory != null)
                filter.SetAllow(_corpsesCategory, false, null, null);
            if (_kibbleDef != null)
                filter.SetAllow(_kibbleDef, false);
        }

        private static FoodPolicy FindOrCreatePolicy(FoodRestrictionDatabase db, string label)
        {
            foreach (FoodPolicy p in db.AllFoodRestrictions)
            {
                if (p.label == label) return p;
            }
            FoodPolicy newPolicy = db.MakeNewFoodRestriction();
            newPolicy.label = label;
            return newPolicy;
        }

        private static FoodPolicy DeterminePolicyForPawn(Pawn pawn)
        {
            if (_cannibalTrait != null && pawn.story?.traits?.HasTrait(_cannibalTrait) == true)
                return _humanMeatPolicy;

            if (pawn.Ideo != null)
            {
                var precepts = pawn.Ideo.PreceptsListForReading;
                for (int i = 0; i < precepts.Count; i++)
                {
                    string dn = precepts[i].def.defName;
                    if (dn == "Cannibalism_Preferred"
                        || dn == "Cannibalism_RequiredRavenous"
                        || dn == "Cannibalism_RequiredStrong")
                        return _humanMeatPolicy;
                    if (dn == "InsectMeatEating_Loved")
                        return _insectMeatPolicy;
                }
            }

            return _normalPolicy;
        }

        private static void InitDefs()
        {
            if (_defsInitialized) return;
            _defsInitialized = true;

            _cannibalTrait = DefDatabase<TraitDef>.GetNamed("Cannibal", false);
            _allowCannibalFilter = SpecialThingFilterDefOf.AllowCannibal;
            _allowInsectMeatFilter = SpecialThingFilterDefOf.AllowInsectMeat;
            _foodRawCategory = DefDatabase<ThingCategoryDef>.GetNamed("FoodRaw", false);
            _corpsesCategory = ThingCategoryDefOf.Corpses;
            _kibbleDef = DefDatabase<ThingDef>.GetNamed("Kibble", false);

            if (_cannibalTrait == null)
                Log.WarningOnce("[AutoEverything] 食人族特质未找到，AE_人肉方案将仅依赖信仰信条", 0xB100);
            if (_foodRawCategory == null)
                Log.WarningOnce("[AutoEverything] FoodRaw 食物类别未找到，生食禁止可能无效", 0xB110);
            if (_kibbleDef == null)
                Log.WarningOnce("[AutoEverything] Kibble 物品定义未找到，动物饲料禁止可能无效", 0xB120);
        }
    }
}
