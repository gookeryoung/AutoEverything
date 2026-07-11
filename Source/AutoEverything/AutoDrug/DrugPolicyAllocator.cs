using System.Collections.Generic;
using RimWorld;
using Verse;
using AutoEverything.Core;

namespace AutoEverything.AutoDrug
{
    /// <summary>
    /// 用药方案自动分配器：根据殖民者的特质与信仰（文化）分配用药方案。
    ///
    /// 方案列表：
    /// - AE_常规：社交用药（啤酒+烟叶允许娱乐使用），基于 SocialDrugs 模板
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
        // 方案标签（固定标识，不随语言变化）
        private const string NormalLabel = "AE_常规";
        private const string NoDrugsLabel = "AE_禁药";

        // 缓存方案引用
        private static DrugPolicy _normalPolicy;
        private static DrugPolicy _noDrugsPolicy;

        // 首次初始化标志
        private static bool _defsInitialized;
        private static TraitDef _drugDesireTrait;
        private static DrugPolicyDef _socialDrugsDef;
        private static DrugPolicyDef _noDrugsDef;

        /// <summary>
        /// 为所有殖民者分配用药方案。返回被修改的殖民者数量。
        /// </summary>
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

        /// <summary>
        /// 确保两个 AE 用药方案存在并配置正确。
        /// 首次创建时从 DrugPolicyDef 模板初始化；已存在的不重新初始化（尊重玩家手动修改）。
        /// </summary>
        private static void EnsurePoliciesExist()
        {
            InitDefs();

            DrugPolicyDatabase db = Current.Game.drugPolicyDatabase;
            if (db == null) return;

            _normalPolicy = FindOrCreatePolicyFromDef(db, NormalLabel, _socialDrugsDef);
            _noDrugsPolicy = FindOrCreatePolicyFromDef(db, NoDrugsLabel, _noDrugsDef);
        }

        /// <summary>
        /// 在数据库中查找指定标签的方案，不存在则从 DrugPolicyDef 模板创建。
        /// NewDrugPolicyFromDef 内部调用 private InitializeIfNeeded 完成模板初始化。
        /// 已存在的方案不重新初始化，尊重玩家手动修改。
        /// </summary>
        private static DrugPolicy FindOrCreatePolicyFromDef(DrugPolicyDatabase db, string label, DrugPolicyDef def)
        {
            foreach (DrugPolicy p in db.AllPolicies)
            {
                if (p.label == label) return p;
            }
            // def 为 null 时回退到空方案（仅 WarningOnce，不阻断功能）
            DrugPolicy newPolicy = def != null
                ? db.NewDrugPolicyFromDef(def)
                : db.MakeNewDrugPolicy();
            newPolicy.label = label;
            return newPolicy;
        }

        /// <summary>
        /// 根据特质与信仰判定 Pawn 应使用哪个用药方案。
        /// 优先级：禁酒主义特质 > 禁药信条 > 常规。
        /// </summary>
        private static DrugPolicy DeterminePolicyForPawn(Pawn pawn)
        {
            // 禁酒主义特质（DrugDesire degree=-1）→ AE_禁药
            if (_drugDesireTrait != null && pawn.story?.traits != null)
            {
                int degree = pawn.story.traits.DegreeOfTrait(_drugDesireTrait);
                if (degree == -1)
                    return _noDrugsPolicy;
            }

            // 信仰禁药信条 → AE_禁药
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

        /// <summary>
        /// 初始化缓存的 Def 引用。只需执行一次。
        /// </summary>
        private static void InitDefs()
        {
            if (_defsInitialized) return;
            _defsInitialized = true;

            _drugDesireTrait = TraitDefOf.DrugDesire;
            _socialDrugsDef = DefDatabase<DrugPolicyDef>.GetNamed("SocialDrugs", false);
            _noDrugsDef = DefDatabase<DrugPolicyDef>.GetNamed("NoDrugs", false);

            if (_socialDrugsDef == null)
                Log.WarningOnce("[AutoEverything] SocialDrugs 用药方案模板未找到", 0xB200);
            if (_noDrugsDef == null)
                Log.WarningOnce("[AutoEverything] NoDrugs 用药方案模板未找到", 0xB300);
        }
    }
}
