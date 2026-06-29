using RimWorld;
using Verse;
using AutoEquipment.Scoring;

namespace AutoEquipment
{
    /// <summary>
    /// 装备评分器：重构为管线调度入口。
    /// 保留旧 API 以兼容现有调用方，内部委托给 ScoringPipeline。
    /// 关键变化：移除硬编码角色契合度，由技能+特质直接驱动评分。
    /// </summary>
    public static class GearScorer
    {
        // ===================== 武器评分 =====================

        /// <summary>
        /// 为 Pawn 评分武器适配度，分数越高越适合。
        /// 重构后：委托给武器评分管线，由各 IScorer 独立评分。
        /// 性能优化：使用 EvaluateFast 跳过 items 累积，避免 Tick 路径 GC 压力。
        /// </summary>
        public static float ScoreWeapon(Pawn pawn, Thing weapon, Role role, GearContext context)
        {
            if (weapon?.def == null) return -1000f;

            GearWeights weights = GearPolicyEngine.GetWeights();
            var pipeline = ScoringPipelineFactory.GetWeaponPipeline();
            // 性能路径：跳过 items 累积
            var breakdown = pipeline.EvaluateFast(pawn, weapon, role, context, weights);

            float score = breakdown.Vetoed ? breakdown.VetoScore : breakdown.Total;

            // 可疑评分：真实武器却得 0 或负分时记录（WarningOnce 防刷屏，详细报告走调试开关）
            // 仅在可疑评分且开启调试时才重新走 WithBreakdown 取明细
            if (score <= 0f && (weapon.def.IsRangedWeapon || weapon.def.IsMeleeWeapon))
            {
                Log.WarningOnce($"[AutoEquipment] ScoreWeapon 可疑评分 {score:F1}: {pawn.LabelShort} + '{weapon.def.defName}' (role={role}, context={context})",
                    pawn.thingIDNumber ^ weapon.thingIDNumber);
                if (AEDebug.IsActive)
                {
                    // 重新评分取明细（仅可疑时，频率低）
                    var detailedBreakdown = ScoreWeaponWithBreakdown(pawn, weapon, role, context);
                    AEDebug.Log(() => detailedBreakdown.BuildReport(pawn.LabelShort, weapon.LabelShort));
                }
            }

            return score;
        }

        /// <summary>
        /// 为 Pawn 评分武器，返回带明细的 ScoreBreakdown。
        /// 用于调试监测，性能路径请用 ScoreWeapon。
        /// </summary>
        public static ScoreBreakdown ScoreWeaponWithBreakdown(Pawn pawn, Thing weapon, Role role, GearContext context)
        {
            if (weapon?.def == null) return new ScoreBreakdown();

            GearWeights weights = GearPolicyEngine.GetWeights();
            var pipeline = ScoringPipelineFactory.GetWeaponPipeline();
            return pipeline.Evaluate(pawn, weapon, role, context, weights);
        }

        // ===================== 防具评分 =====================

        /// <summary>
        /// 为 Pawn 评分防具适配度，分数越高越适合。
        /// 重构后：委托给防具评分管线。
        /// 性能优化：使用 EvaluateFast 跳过 items 累积，避免 Tick 路径 GC 压力。
        /// </summary>
        public static float ScoreApparel(Pawn pawn, Apparel apparel, Role role, GearContext context)
        {
            if (apparel?.def == null) return -1000f;

            GearWeights weights = GearPolicyEngine.GetWeights();
            var pipeline = ScoringPipelineFactory.GetApparelPipeline();
            // 性能路径：跳过 items 累积
            var breakdown = pipeline.EvaluateFast(pawn, apparel, role, context, weights);

            float score = breakdown.Vetoed ? breakdown.VetoScore : breakdown.Total;

            // 可疑评分：非沾染却得负分（WarningOnce 防刷屏，详细报告走调试开关）
            if (score <= 0f && !apparel.WornByCorpse)
            {
                Log.WarningOnce($"[AutoEquipment] ScoreApparel 可疑评分 {score:F1}: {pawn.LabelShort} + '{apparel.def.defName}' (role={role}, context={context})",
                    pawn.thingIDNumber ^ apparel.thingIDNumber);
                if (AEDebug.IsActive)
                {
                    // 重新评分取明细（仅可疑时，频率低）
                    var detailedBreakdown = ScoreApparelWithBreakdown(pawn, apparel, role, context);
                    AEDebug.Log(() => detailedBreakdown.BuildReport(pawn.LabelShort, apparel.LabelShort));
                }
            }

            return score;
        }

        /// <summary>
        /// 为 Pawn 评分防具，返回带明细的 ScoreBreakdown。
        /// </summary>
        public static ScoreBreakdown ScoreApparelWithBreakdown(Pawn pawn, Apparel apparel, Role role, GearContext context)
        {
            if (apparel?.def == null) return new ScoreBreakdown();

            GearWeights weights = GearPolicyEngine.GetWeights();
            var pipeline = ScoringPipelineFactory.GetApparelPipeline();
            return pipeline.Evaluate(pawn, apparel, role, context, weights);
        }

        // ===================== 副武器评分 =====================

        /// <summary>
        /// 为 Pawn 评分副武器（备用武器）：远程 Pawn 配近战副武器，
        /// 近战 Pawn 配远程副武器。
        /// 副武器逻辑较简单，暂不纳入管线，保留独立实现。
        /// </summary>
        public static float ScoreSidearm(Pawn pawn, Thing weapon, Role role)
        {
            if (weapon?.def == null) return -1000f;

            bool isMelee = weapon.def.IsMeleeWeapon;
            bool isRanged = weapon.def.IsRangedWeapon;
            bool wantsMelee = RoleDetector.PrefersMelee(role);

            float score = 0f;

            // 副武器应为主武器的相反类型
            if (wantsMelee && isRanged)
                score += 50f; // 格斗者偏好远程副武器
            else if (!wantsMelee && isMelee)
                score += 50f; // 射手偏好近战副武器
            else
                score -= 30f; // 与主武器同类型 = 副武器价值低

            // 近战副武器：偏好快速轻便的武器（匕首、短剑）
            if (isMelee)
            {
                float dps = weapon.GetStatValue(StatDefOf.MeleeWeapon_AverageDPS);
                score += dps * 3f;
                float mass = weapon.GetStatValue(StatDefOf.Mass);
                score -= mass * 5f;
            }

            // 远程副武器：偏好手枪/冲锋枪（短射程、快射速）
            if (isRanged)
            {
                float cooldown = weapon.GetStatValue(StatDefOf.RangedWeapon_Cooldown);
                if (cooldown < 1.5f) score += 20f;
                float mass = weapon.GetStatValue(StatDefOf.Mass);
                score -= mass * 3f;
            }

            // 品质
            if (weapon.TryGetQuality(out QualityCategory quality))
                score += ((int)quality - 2) * 5f;

            return score;
        }
    }
}
