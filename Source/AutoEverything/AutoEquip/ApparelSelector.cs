using System;
using System.Collections.Generic;
using AutoEverything.RoleEvaluation;
using RimWorld;
using Verse;

namespace AutoEverything.AutoEquip
{
    /// <summary>
    /// 服装筛选与评分。
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

        public static bool HasConflict(Pawn pawn, ThingDef def, Apparel ignore)
        {
            if (pawn == null || pawn.apparel == null || def == null) return true;

            List<Apparel> worn = pawn.apparel.WornApparel;
            if (worn == null) return false;
            for (int i = 0; i < worn.Count; i++)
            {
                Apparel other = worn[i];
                if (other == null || other == ignore || other.def == def) continue;
                if (!ApparelUtility.CanWearTogether(def, other.def, pawn.RaceProps.body))
                    return true;
            }
            return false;
        }

        public static Apparel FindWornInLayer(Pawn pawn, ApparelLayerDef layer)
        {
            if (pawn == null || pawn.apparel == null || layer == null) return null;

            List<Apparel> worn = pawn.apparel.WornApparel;
            if (worn == null) return null;
            for (int i = 0; i < worn.Count; i++)
            {
                Apparel apparel = worn[i];
                if (apparel == null || apparel.def == null) continue;
                if (HasLayer(apparel.def, layer)) return apparel;
            }
            return null;
        }

        public static bool TryGetFirstLayer(ThingDef def, out ApparelLayerDef layer)
        {
            layer = null;
            if (def == null || def.apparel == null || def.apparel.layers == null || def.apparel.layers.Count == 0)
                return false;
            layer = def.apparel.layers[0];
            return layer != null;
        }

        private static bool HasLayer(ThingDef def, ApparelLayerDef layer)
        {
            if (def == null || def.apparel == null || def.apparel.layers == null) return false;
            for (int i = 0; i < def.apparel.layers.Count; i++)
            {
                if (def.apparel.layers[i] == layer) return true;
            }
            return false;
        }

        /// <summary>
        /// 服装评分：护甲优先，其次移动惩罚、耐久、品质。
        /// 前排更看重护甲，工人/贸易减轻重量惩罚。
        /// </summary>
        public static float Score(Pawn pawn, Apparel apparel, RoleGroup role)
        {
            if (apparel == null || apparel.def == null) return 0f;
            if (apparel.WornByCorpse) return -9999f;

            try
            {
                float sharp = apparel.GetStatValue(StatDefOf.ArmorRating_Sharp);
                float blunt = apparel.GetStatValue(StatDefOf.ArmorRating_Blunt);
                float heat = apparel.GetStatValue(StatDefOf.ArmorRating_Heat);

                float armor;
                if (role == RoleGroup.Front)
                    armor = sharp * 1.4f + blunt * 0.8f + heat * 0.4f;
                else if (role == RoleGroup.Shooter)
                    armor = sharp * 1.1f + blunt * 0.6f + heat * 0.3f;
                else
                    armor = sharp * 0.6f + blunt * 0.4f + heat * 0.2f;

                float moveSpeed = apparel.GetStatValue(StatDefOf.MoveSpeed);
                float durability = apparel.MaxHitPoints > 0
                    ? apparel.HitPoints / (float)apparel.MaxHitPoints
                    : 1f;

                CompQuality quality = apparel.TryGetComp<CompQuality>();
                int qualityRank = quality != null ? (int)quality.Quality : (int)QualityCategory.Normal;

                float score = armor * 10f + moveSpeed * 20f + durability * 3f + qualityRank * 2f;
                if (role == RoleGroup.Worker || role == RoleGroup.Trade)
                    score -= Math.Max(0f, -moveSpeed) * 20f;
                return score;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] 服装评分失败: " + ex.Message,
                    (apparel.thingIDNumber ^ 0xA931));
                return 0f;
            }
        }
    }
}
