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
    /// - AE_常规：与默认方案一致（禁止人肉/虫肉）
    /// - AE_人肉：在常规基础上允许人肉（食人族特质或食人主义信条）
    /// - AE_虫肉：在常规基础上允许虫肉（虫肉热爱信条）
    /// "允许"而非"仅含"：方案仍包含常规食物，只是额外允许人肉/虫肉。
    ///
    /// 触发方式：事件驱动（殖民者数量变化、信仰变化），非周期 Tick。
    /// </summary>
    public static class FoodPolicyAllocator
    {
        // 方案标签（固定标识，不随语言变化）
        private const string NormalLabel = "AE_常规";
        private const string HumanMeatLabel = "AE_人肉";
        private const string InsectMeatLabel = "AE_虫肉";

        // 缓存方案引用（每次 ReallocateAll 前刷新）
        private static FoodPolicy _normalPolicy;
        private static FoodPolicy _humanMeatPolicy;
        private static FoodPolicy _insectMeatPolicy;

        // 首次初始化标志
        private static bool _defsInitialized;
        private static TraitDef _cannibalTrait;
        private static SpecialThingFilterDef _allowCannibalFilter;
        private static SpecialThingFilterDef _allowInsectMeatFilter;

        /// <summary>
        /// 为所有殖民者分配食物方案。返回被修改的殖民者数量。
        /// </summary>
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

        /// <summary>
        /// 确保三个 AE 食物方案存在并配置正确。
        /// 每次调用都重新配置 filter，确保方案与默认方案同步。
        /// </summary>
        private static void EnsurePoliciesExist()
        {
            InitDefs();

            FoodRestrictionDatabase db = Current.Game.foodRestrictionDatabase;
            if (db == null) return;

            _normalPolicy = FindOrCreatePolicy(db, NormalLabel);
            _humanMeatPolicy = FindOrCreatePolicy(db, HumanMeatLabel);
            _insectMeatPolicy = FindOrCreatePolicy(db, InsectMeatLabel);

            // 以默认方案为基准配置 filter
            FoodPolicy defaultPolicy = db.DefaultFoodRestriction();
            if (defaultPolicy == null) return;

            // AE_常规：完全复制默认方案
            _normalPolicy.filter.CopyAllowancesFrom(defaultPolicy.filter);

            // AE_人肉：复制常规 + 允许人肉
            _humanMeatPolicy.filter.CopyAllowancesFrom(_normalPolicy.filter);
            if (_allowCannibalFilter != null)
                _humanMeatPolicy.filter.SetAllow(_allowCannibalFilter, true);

            // AE_虫肉：复制常规 + 允许虫肉
            _insectMeatPolicy.filter.CopyAllowancesFrom(_normalPolicy.filter);
            if (_allowInsectMeatFilter != null)
                _insectMeatPolicy.filter.SetAllow(_allowInsectMeatFilter, true);
        }

        /// <summary>
        /// 在数据库中查找指定标签的方案，不存在则创建。
        /// </summary>
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

        /// <summary>
        /// 根据特质与信仰判定 Pawn 应使用哪个食物方案。
        /// 优先级：食人族特质 > 食人主义信条 > 虫肉热爱信条 > 常规。
        /// </summary>
        private static FoodPolicy DeterminePolicyForPawn(Pawn pawn)
        {
            // 食人族特质 → AE_人肉
            if (_cannibalTrait != null && pawn.story?.traits?.HasTrait(_cannibalTrait) == true)
                return _humanMeatPolicy;

            // 信仰食人主义信条 → AE_人肉
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

        /// <summary>
        /// 初始化缓存的 Def 引用。只需执行一次。
        /// </summary>
        private static void InitDefs()
        {
            if (_defsInitialized) return;
            _defsInitialized = true;

            _cannibalTrait = DefDatabase<TraitDef>.GetNamed("Cannibal", false);
            _allowCannibalFilter = SpecialThingFilterDefOf.AllowCannibal;
            _allowInsectMeatFilter = SpecialThingFilterDefOf.AllowInsectMeat;

            if (_cannibalTrait == null)
                Log.WarningOnce("[AutoEverything] 食人族特质未找到，AE_人肉方案将仅依赖信仰信条", 0xB100);
        }
    }
}
