using System;
using System.Collections.Generic;
using AutoEverything.Core;
using RimWorld;
using Verse;

namespace AutoEverything.AutoEquip
{
    /// <summary>
    /// 服装筛选：政策、部位、分层冲突、服装要求保护、可穿性硬门槛。
    /// 硬约束：只在殖民者当前【管理→着装】政策允许的范围内挑选，绝不修改玩家政策。
    /// </summary>
    public static class ApparelSelector
    {
        public static bool IsAllowedByPolicy(Pawn pawn, ThingDef def)
        {
            if (pawn == null || def == null || !def.IsApparel) return false;

            Pawn_OutfitTracker outfits = pawn.outfits;
            if (outfits == null) return true;

            ApparelPolicy policy = outfits.CurrentApparelPolicy;
            if (policy == null || policy.filter == null) return true;
            return policy.filter.Allows(def);
        }

        public static bool HasPartsToWear(Pawn pawn, ThingDef def)
        {
            if (pawn == null || def == null) return false;
            try
            {
                return ApparelUtility.HasPartsToWear(pawn, def);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] 服装部位检查失败: " + ex.Message, 0xA930);
                return false;
            }
        }

        /// <summary>
        /// 收集穿上 <paramref name="def"/> 后必须脱掉的已穿衣物（同层或跨层部位冲突）。
        /// 原版 <c>Pawn_ApparelTracker.Wear</c> 会自动丢弃这些件，所以这里只用于评估代价与保护判断。
        /// </summary>
        public static void CollectConflicts(Pawn pawn, ThingDef def, List<Apparel> buffer)
        {
            buffer.Clear();
            if (pawn == null || def == null || pawn.apparel == null) return;

            List<Apparel> worn = pawn.apparel.WornApparel;
            if (worn == null) return;
            for (int i = 0; i < worn.Count; i++)
            {
                Apparel other = worn[i];
                if (other == null || other.def == null || other.def == def) continue;
                if (!ApparelUtility.CanWearTogether(def, other.def, pawn.RaceProps.body))
                    buffer.Add(other);
            }
        }

        /// <summary>
        /// 丢弃这些衣物是否会破坏当前生效的服装要求
        /// （Royalty 头衔威望服装、Ideology 角色服装；两者已由 <c>ActiveRequirementsForReading</c> 合并）。
        /// </summary>
        public static bool WouldBreakRequirement(Pawn pawn, List<Apparel> drops)
        {
            if (pawn == null || drops == null || drops.Count == 0) return false;
            if (pawn.apparel == null) return false;

            List<ApparelRequirement> requirements = pawn.apparel.ActiveRequirementsForReading;
            if (requirements == null || requirements.Count == 0) return false;

            for (int i = 0; i < drops.Count; i++)
            {
                Apparel dropped = drops[i];
                if (dropped == null || dropped.def == null) continue;
                for (int r = 0; r < requirements.Count; r++)
                {
                    ApparelRequirement requirement = requirements[r];
                    if (requirement == null) continue;
                    if (requirement.ApparelMeetsRequirement(dropped.def, true)) return true;
                }
            }
            return false;
        }

        /// <summary>候选衣物的硬门槛：可穿层、品质下限、非尸体污损、不替换锁定衣物。</summary>
        public static bool IsWearableNow(Pawn pawn, Apparel candidate)
        {
            if (candidate == null || candidate.def == null || candidate.def.apparel == null) return false;
            if (candidate.WornByCorpse) return false;
            if (candidate.def.apparel.layers == null || candidate.def.apparel.layers.Count == 0) return false;
            if (ApparelScorer.GetQualityRank(candidate) < (int)AESettings.equipMinQuality) return false;
            if (pawn != null && pawn.apparel != null && pawn.apparel.WouldReplaceLockedApparel(candidate))
                return false;
            return true;
        }
    }
}
