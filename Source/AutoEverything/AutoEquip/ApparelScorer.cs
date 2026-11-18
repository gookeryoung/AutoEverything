using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace AutoEverything.AutoEquip
{
    /// <summary>
    /// 服装运行时取数层：把衣物数值折算成 <see cref="ApparelSetInput"/>。
    ///
    /// 与原版结算口径对齐（B0 反编译结论）：
    /// - 逐部位累加 <c>coverageAbs</c> 权重，未覆盖部位按"无护甲"计入；
    /// - 同一部位的多件衣物按层串行结算（期望剩余伤害连乘），不是取最高；
    /// - 移动/工作惩罚取 <c>equippedStatOffsets</c> 的原始偏移，避免 base 值干扰。
    /// </summary>
    public static class ApparelScorer
    {
        /// <summary>一次换装最多连带丢弃的旧衣物数量，超过则放弃该候选。</summary>
        public const int MaxConflictDrops = 2;

        private struct PieceStats
        {
            public float Sharp;
            public float Blunt;
            public float Heat;
            public float InsulationCold;
            public float InsulationHeat;
            public float MoveOffset;
            public float WorkOffset;
            public float Durability;
        }

        private static readonly Dictionary<Apparel, PieceStats> StatsCache =
            new Dictionary<Apparel, PieceStats>();

        private static readonly List<Apparel> SetBuffer = new List<Apparel>();

        /// <summary>每轮开始时清空缓存，避免地图物品变化后读到旧数值。</summary>
        public static void BeginRound()
        {
            StatsCache.Clear();
        }

        public static void EndRound()
        {
            StatsCache.Clear();
        }

        public static int GetQualityRank(Apparel apparel)
        {
            if (apparel == null) return (int)QualityCategory.Normal;
            CompQuality quality = apparel.TryGetComp<CompQuality>();
            return quality != null ? (int)quality.Quality : (int)QualityCategory.Normal;
        }

        public static float GetDurability(Apparel apparel)
        {
            if (apparel == null) return 1f;
            if (apparel.MaxHitPoints <= 0) return 1f;
            return apparel.HitPoints / (float)apparel.MaxHitPoints;
        }

        /// <summary>
        /// 计算"当前穿着 − drops + candidate"这一套装备的评分输入。
        /// candidate/drops 都可为 null 或空，此时即为当前状态。
        /// </summary>
        public static ApparelSetInput BuildSet(Pawn pawn, Apparel candidate, List<Apparel> drops,
            ApparelProfile profile)
        {
            ApparelSetInput input = new ApparelSetInput
            {
                Profile = profile,
                SharpSurvival = 1f,
                BluntSurvival = 1f,
                HeatSurvival = 1f,
                Durability = 1f
            };
            if (pawn == null || pawn.def == null || pawn.RaceProps == null) return input;

            SetBuffer.Clear();
            List<Apparel> worn = pawn.apparel != null ? pawn.apparel.WornApparel : null;
            if (worn != null)
            {
                for (int i = 0; i < worn.Count; i++)
                {
                    Apparel piece = worn[i];
                    if (piece == null || piece.def == null || piece.Destroyed) continue;
                    if (drops != null && drops.Contains(piece)) continue;
                    SetBuffer.Add(piece);
                }
            }
            if (candidate != null) SetBuffer.Add(candidate);

            float insulationCold = 0f;
            float insulationHeat = 0f;
            float moveOffset = 0f;
            float workOffset = 0f;
            float durability = 1f;

            for (int i = 0; i < SetBuffer.Count; i++)
            {
                PieceStats stats = GetStats(SetBuffer[i]);
                insulationCold += stats.InsulationCold;
                insulationHeat += stats.InsulationHeat;
                moveOffset += stats.MoveOffset;
                workOffset += stats.WorkOffset;
                if (stats.Durability < durability) durability = stats.Durability;
            }

            input.InsulationCold = insulationCold;
            input.InsulationHeat = insulationHeat;
            input.MoveSpeedOffset = moveOffset;
            input.WorkSpeedOffset = workOffset;
            input.Durability = durability;

            AccumulateCoverage(pawn, out float sharpSurvival, out float bluntSurvival, out float heatSurvival);
            input.SharpSurvival = sharpSurvival;
            input.BluntSurvival = bluntSurvival;
            input.HeatSurvival = heatSurvival;

            if (pawn.health != null)
            {
                float needCold;
                float needHeat;
                ApparelScoreCore.TemperatureNeed(pawn.GetStatValue(StatDefOf.ComfyTemperatureMin),
                    pawn.GetStatValue(StatDefOf.ComfyTemperatureMax), pawn.AmbientTemperature,
                    out needCold, out needHeat);
                input.NeedCold = needCold;
                input.NeedHeat = needHeat;
            }

            SetBuffer.Clear();
            return input;
        }

        private static void AccumulateCoverage(Pawn pawn, out float sharpSurvival, out float bluntSurvival,
            out float heatSurvival)
        {
            sharpSurvival = 1f;
            bluntSurvival = 1f;
            heatSurvival = 1f;

            BodyDef body = pawn.RaceProps.body;
            if (body == null || body.AllParts == null || body.AllParts.Count == 0) return;

            float totalWeight = 0f;
            float sharpAcc = 0f;
            float bluntAcc = 0f;
            float heatAcc = 0f;

            for (int p = 0; p < body.AllParts.Count; p++)
            {
                BodyPartRecord part = body.AllParts[p];
                if (part == null) continue;
                float weight = part.coverageAbs;
                if (weight <= 0f) continue;
                if (pawn.health != null && pawn.health.hediffSet != null && pawn.health.hediffSet.PartIsMissing(part))
                    continue;

                totalWeight += weight;
                float sharp = 1f;
                float blunt = 1f;
                float heat = 1f;
                for (int i = 0; i < SetBuffer.Count; i++)
                {
                    Apparel piece = SetBuffer[i];
                    if (piece.def.apparel == null || !piece.def.apparel.CoversBodyPart(part)) continue;
                    PieceStats stats = GetStats(piece);
                    sharp *= ApparelScoreCore.LayerSurvival(stats.Sharp);
                    blunt *= ApparelScoreCore.LayerSurvival(stats.Blunt);
                    heat *= ApparelScoreCore.LayerSurvival(stats.Heat);
                }

                sharpAcc += weight * sharp;
                bluntAcc += weight * blunt;
                heatAcc += weight * heat;
            }

            if (totalWeight <= 0.0001f) return;

            sharpSurvival = sharpAcc / totalWeight;
            bluntSurvival = bluntAcc / totalWeight;
            heatSurvival = heatAcc / totalWeight;
        }

        private static PieceStats GetStats(Apparel apparel)
        {
            PieceStats stats;
            if (StatsCache.TryGetValue(apparel, out stats)) return stats;

            try
            {
                stats.Sharp = apparel.GetStatValue(StatDefOf.ArmorRating_Sharp);
                stats.Blunt = apparel.GetStatValue(StatDefOf.ArmorRating_Blunt);
                stats.Heat = apparel.GetStatValue(StatDefOf.ArmorRating_Heat);
                stats.InsulationCold = apparel.GetStatValue(StatDefOf.Insulation_Cold);
                stats.InsulationHeat = apparel.GetStatValue(StatDefOf.Insulation_Heat);
                stats.MoveOffset = apparel.def.equippedStatOffsets != null
                    ? apparel.def.equippedStatOffsets.GetStatOffsetFromList(StatDefOf.MoveSpeed)
                    : 0f;
                stats.WorkOffset = apparel.def.equippedStatOffsets != null
                    ? apparel.def.equippedStatOffsets.GetStatOffsetFromList(StatDefOf.WorkSpeedGlobal)
                    : 0f;
                stats.Durability = GetDurability(apparel);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] 服装数值读取失败: " + ex.Message, 0xA932);
                stats = new PieceStats { Durability = 1f };
            }

            StatsCache[apparel] = stats;
            return stats;
        }
    }
}
