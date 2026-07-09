using System;
using RimWorld;
using Verse;
using Verse.AI;
using AutoEverything.Core;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.AutoSerum
{
    /// <summary>
    /// 子功能3：战斗情境自动注射增益血清（Anomaly DLC）。
    ///
    /// 设计目的：
    /// - 战斗情境（ContextDetector.GetContext == Combat）自动注射增益血清
    /// - 重甲前排（Brawler）→ Metalblood（减伤增强生存）
    /// - 其他战斗员 → Juggernaut（力量/速度↑增强输出）
    /// - 低血量（&lt; 50%）→ MindNumb（防精神崩溃，生存优先）
    ///
    /// 血清 API（RimWorld 1.6 Anomaly）：
    /// - MetalbloodSerum / JuggernautSerum / MindNumbSerum 均为 SerumBase 派生
    /// - SerumBase 的 CompProperties_Usable.useJob = Ingest，且 ingestible 可服用
    /// - 故注射 Job 用 JobDefOf.Ingest（参考 MedicationAllocator.AssignIngestJob 模式）
    ///
    /// 血清来源：优先库存，其次地图上找最近可用（参考 MedicationAllocator）。
    ///
    /// 冷却：60000 tick ≈ 1 天（增益血清有副作用/稀缺，避免频繁注射）。
    /// 已有对应 hediff 时跳过（避免重复注射浪费）。
    ///
    /// DLC 检查（强制）：3 个增益血清均为 Anomaly DLC，
    /// 必须 ModsConfig.AnomalyActive + DefDatabase.GetNamed(defName, false) + null 检查。
    ///
    /// 模式：per-pawn 处理（参考 MedicationAllocator），不周期门控，
    /// 由 SerumAllocator.RunAllPawns 控制 3000 tick 周期；内部加 60000 tick 注射冷却。
    /// </summary>
    public static class BoostSerumAllocator
    {
        // 错误去重 salt：与 SerumCarrier/HealerSerumAllocator/SerumAllocator 区分
        private const int BoostErrorSalt = 0xA830;

        // 注射冷却：60000 tick ≈ 1 天，避免频繁注射（增益血清有副作用/稀缺）
        private const int InjectCooldown = 60000;
        private static int lastInjectTick = -9999;

        // 低血量阈值：血量 < 50% 优先注射 MindNumb（防崩溃）
        private const float LowHealthPercent = 0.5f;

        // ===================== Anomaly 血清 ThingDef 懒加载缓存 =====================
        // 规则：禁止在静态字段初始化器中调用 DefDatabase（跨线程风险），用懒加载属性
        // 3 个增益血清均为 Anomaly DLC，懒加载属性内不查 ModsConfig（由调用方先 gate）

        private static ThingDef cachedMetalbloodDef;
        private static bool metalbloodResolved = false;
        private static ThingDef MetalbloodDef
        {
            get
            {
                if (!metalbloodResolved)
                {
                    metalbloodResolved = true;
                    cachedMetalbloodDef = DefDatabase<ThingDef>.GetNamed("MetalbloodSerum", false);
                }
                return cachedMetalbloodDef;
            }
        }

        private static ThingDef cachedJuggernautDef;
        private static bool juggernautResolved = false;
        private static ThingDef JuggernautDef
        {
            get
            {
                if (!juggernautResolved)
                {
                    juggernautResolved = true;
                    cachedJuggernautDef = DefDatabase<ThingDef>.GetNamed("JuggernautSerum", false);
                }
                return cachedJuggernautDef;
            }
        }

        private static ThingDef cachedMindNumbDef;
        private static bool mindNumbResolved = false;
        private static ThingDef MindNumbDef
        {
            get
            {
                if (!mindNumbResolved)
                {
                    mindNumbResolved = true;
                    cachedMindNumbDef = DefDatabase<ThingDef>.GetNamed("MindNumbSerum", false);
                }
                return cachedMindNumbDef;
            }
        }

        // ===================== 对应 HediffDef 懒加载缓存（避免重复注射） =====================
        private static HediffDef cachedMetalbloodHediff;
        private static bool metalbloodHediffResolved = false;
        private static HediffDef MetalbloodHediff
        {
            get
            {
                if (!metalbloodHediffResolved)
                {
                    metalbloodHediffResolved = true;
                    cachedMetalbloodHediff = DefDatabase<HediffDef>.GetNamed("Metalblood", false);
                }
                return cachedMetalbloodHediff;
            }
        }

        private static HediffDef cachedJuggernautHediff;
        private static bool juggernautHediffResolved = false;
        private static HediffDef JuggernautHediff
        {
            get
            {
                if (!juggernautHediffResolved)
                {
                    juggernautHediffResolved = true;
                    cachedJuggernautHediff = DefDatabase<HediffDef>.GetNamed("JuggernautSerum", false);
                }
                return cachedJuggernautHediff;
            }
        }

        private static HediffDef cachedMindNumbHediff;
        private static bool mindNumbHediffResolved = false;
        private static HediffDef MindNumbHediff
        {
            get
            {
                if (!mindNumbHediffResolved)
                {
                    mindNumbHediffResolved = true;
                    cachedMindNumbHediff = DefDatabase<HediffDef>.GetNamed("MindNumbSerum", false);
                }
                return cachedMindNumbHediff;
            }
        }

        /// <summary>
        /// 单 Pawn 入口：战斗情境下按角色/血量选择并注射增益血清。
        /// 受 AESettings.autoBoostSerum 子开关控制。
        /// </summary>
        public static void AllocateForPawn(Pawn pawn)
        {
            if (!AESettings.autoBoostSerum) return;
            if (pawn == null || pawn.Dead || pawn.Downed || pawn.Map == null) return;

            // DLC gate：3 个增益血清均为 Anomaly DLC，未加载 DLC 直接跳过
            if (!ModsConfig.AnomalyActive) return;

            // 冷却检查：避免频繁注射
            if (Find.TickManager.TicksGame - lastInjectTick < InjectCooldown) return;

            // 医疗守卫：注射 Job 会 TryTakeOrderedJob 取消当前 Job，医疗 Job 中/床上休养 均跳过
            // Combat 情境的 Pawn（征召或 alwaysShowWeapon Job）按定义不在床上休养，
            // 故 ShouldSkipForMedical 不会误拦战斗员，仅拦截医生战斗中救人的边角情况
            if (PawnJobGuard.ShouldSkipForMedical(pawn)) return;

            try
            {
                // 战斗情境检测：仅战斗中注射（参考 ContextDetector.GetContext）
                GearContext context = ContextDetector.GetContext(pawn);
                if (context != GearContext.Combat) return;

                // 按角色/血量选择血清
                ThingDef serumDef = SelectSerumForPawn(pawn);
                if (serumDef == null) return;

                // 已有对应 hediff 则跳过（避免重复注射浪费）
                HediffDef hediffDef = GetHediffForSerum(serumDef);
                if (hediffDef != null)
                {
                    Hediff existing = pawn.health?.hediffSet?.GetFirstHediffOfDef(hediffDef);
                    if (existing != null && existing.Severity > 0f)
                    {
                        if (AEDebug.IsActive)
                            AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(pawn)} 增益血清 {serumDef.defName} 药效仍在 (severity={existing.Severity:F2})");
                        return;
                    }
                }

                // 血清来源：优先库存，其次地图（参考 MedicationAllocator）
                Thing target = FindSerumInInventory(pawn, serumDef);
                if (target == null)
                {
                    target = FindSerumOnMap(pawn, serumDef);
                }

                if (target == null)
                {
                    if (AEDebug.IsActive)
                        AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(pawn)} 战斗需 {serumDef.defName} 但库存与地图均无可用");
                    return;
                }

                AssignIngestJob(pawn, target);
                lastInjectTick = Find.TickManager.TicksGame;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] 自动增益血清注射失败 " + pawn.LabelShort + ": " + ex.Message,
                    pawn.thingIDNumber ^ BoostErrorSalt);
            }
        }

        /// <summary>
        /// 按 Pawn 角色/血量选择增益血清：
        /// - 低血量（&lt; 50%）→ MindNumb（防精神崩溃，生存优先）
        /// - Brawler → Metalblood（减伤增强前排生存）
        /// - 其他战斗员 → Juggernaut（力量/速度↑增强输出）
        /// 返回 null 表示对应血清 Def 未加载或角色不匹配。
        /// </summary>
        private static ThingDef SelectSerumForPawn(Pawn pawn)
        {
            // 低血量优先 MindNumb（防崩溃救命）
            float healthPct = pawn.health?.summaryHealth?.SummaryHealthPercent ?? 1f;
            if (healthPct < LowHealthPercent)
            {
                return MindNumbDef;
            }

            Role role = RoleDetector.DetectRole(pawn);
            if (role == Role.Brawler)
            {
                return MetalbloodDef;
            }

            // 其他战斗员（Shooter/Hunter/Leader/Doctor/Default）注射 Juggernaut
            return JuggernautDef;
        }

        /// <summary>
        /// 取血清 ThingDef 对应的 HediffDef（用于检测药效是否仍在）。
        /// </summary>
        private static HediffDef GetHediffForSerum(ThingDef serumDef)
        {
            if (serumDef == MetalbloodDef) return MetalbloodHediff;
            if (serumDef == JuggernautDef) return JuggernautHediff;
            if (serumDef == MindNumbDef) return MindNumbHediff;
            return null;
        }

        /// <summary>
        /// 在 Pawn 库存中查找指定血清。
        /// </summary>
        private static Thing FindSerumInInventory(Pawn pawn, ThingDef serumDef)
        {
            foreach (Thing item in pawn.inventory.innerContainer)
            {
                if (item.def == serumDef && item.stackCount > 0) return item;
            }
            return null;
        }

        /// <summary>
        /// 在地图上找最近的可用血清（未禁用、可储备）。
        /// 用 GenClosest.ClosestThingReachable，避免 LINQ（参考 MedicationAllocator.FindPenoxycylineOnMap）。
        /// </summary>
        private static Thing FindSerumOnMap(Pawn pawn, ThingDef serumDef)
        {
            return GenClosest.ClosestThingReachable(
                pawn.Position, pawn.Map,
                ThingRequest.ForDef(serumDef),
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn),
                50f,
                t => !t.IsForbidden(pawn) && pawn.CanReserve(t) && t.stackCount > 0);
        }

        /// <summary>
        /// 安排注射增益血清的 Job：JobDefOf.Ingest。
        /// Anomaly 血清的 CompProperties_Usable.useJob = Ingest，且 ingestible 可服用，
        /// 故用 Ingest Job 让 Pawn 自动取用并注射（参考 MedicationAllocator.AssignIngestJob）。
        /// </summary>
        private static void AssignIngestJob(Pawn pawn, Thing serum)
        {
            Job job = JobMaker.MakeJob(JobDefOf.Ingest, serum);
            job.count = 1;
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);

            if (AEDebug.IsActive)
                AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(pawn)} 战斗注射增益血清 (来源={serum.LabelShort})");
        }
    }
}
