using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using AutoEverything.Core;

namespace AutoEverything.AutoFood
{
    /// <summary>
    /// 子功能2：自动喂食伤员。
    ///
    /// 设计目的：
    /// - 扫描地图 FreeColonistsSpawned，找出卧床且饥饿（CurLevelPercentage &lt; 0.3）的伤员
    /// - 找空闲医生（非征召、非医疗 Job 中、非床上休养）
    /// - 安排 JobDefOf.FeedPatient Job：JobMaker.MakeJob(JobDefOf.FeedPatient, food, patient)
    ///   FeedPatient Job 结构（RimWorld 1.6 已确认）：
    ///     - targetA (FoodSourceInd = TargetIndex.A) = 食物 Thing
    ///     - targetB (DelivereeInd = TargetIndex.B) = 伤员 Pawn
    ///     - targetC (FoodHolderInd = TargetIndex.C) = 食物持有者（可选，不设）
    ///
    /// 食物来源：
    /// - 优先医生库存中已有的食物（FoodUtility.BestFoodInInventory，仅限 Meal 区间）
    /// - 否则在地图上找最近可食用食物（ThingRequestGroup.FoodSourceNotPlantOrTree，排除植物/树/营养 paste 机）
    ///
    /// 医疗守卫（强制）：
    /// - 医生走 PawnJobGuard.ShouldSkipForMedical：正在执行 TendPatient/DoBill/Rescue 时跳过
    /// - 伤员不应用 ShouldSkipForMedical（伤员本身就在床上休养，IsRecoveringInBed=true 会让 ShouldSkip 永真，
    ///   导致永远无法喂食）。FeedPatient Job 不移动伤员，伤员维持 LayDown Job 不被打断。
    ///   仅在候选过滤阶段跳过"正在被手术的伤员"——伤员被手术时 CurJob.def=DoBill(Bill_Medical)
    ///   属于医生视角的 IsDoingMedicalJob，伤员本身 CurJob 仍是 LayDown，故此处无需特殊处理。
    ///
    /// 模式：参考 TreatmentAllocator——AllocateForPawn(pawn) 内部周期门控（3000 tick），
    /// 第一个触发的 Pawn 承担全局扫描成本。
    /// </summary>
    public static class FeedingAllocator
    {
        // 全局分配间隔：与 FoodAllocator 一致（3000 tick ≈ 50 秒）
        // 喂食非紧急到秒级，原生 work giver 会处理紧急喂食
        private const int AllocationInterval = 3000;
        private static int lastAllocationTick = -9999;

        // 候选缓存（Tick 路径禁止 new List，复用静态字段）
        private static readonly List<Pawn> patientCandidates = new List<Pawn>();
        private static readonly List<Pawn> doctorCandidates = new List<Pawn>();
        // 已分配医生缓存：每轮扫描重用，避免同一医生被分配多个伤员
        private static readonly HashSet<Pawn> assignedDoctors = new HashSet<Pawn>();

        // 错误去重 salt：与 MealCarrier/FoodRestrictionManager/FoodAllocator 区分
        private const int FeedingErrorSalt = 0xA720;

        /// <summary>
        /// 单 Pawn 入口：受全局周期控制，仅当距离上次全局分配超过 AllocationInterval 时才执行。
        /// 第一个触发的 Pawn 承担全局扫描成本，其余 Pawn 跳过。
        /// </summary>
        public static void AllocateForPawn(Pawn pawn)
        {
            int tick = Find.TickManager.TicksGame;
            if (tick - lastAllocationTick < AllocationInterval) return;
            lastAllocationTick = tick;

            AllocateAllColonists();
        }

        /// <summary>
        /// 全局扫描：收集所有伤员 + 所有可用医生，按饥饿度升序匹配。
        /// try-catch 隔离：失败时 Log.ErrorOnce 记录，不影响其他 Pawn 评估。
        /// </summary>
        private static void AllocateAllColonists()
        {
            if (!AESettings.autoFeeding) return;

            try
            {
                patientCandidates.Clear();
                doctorCandidates.Clear();
                assignedDoctors.Clear();

                foreach (Map map in Find.Maps)
                {
                    if (map?.mapPawns == null) continue;
                    foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
                    {
                        if (pawn == null) continue;
                        if (DLCCompat.IsGhoul(pawn)) continue;
                        if (!PawnSuitabilityChecker.CanManageGear(pawn)) continue;
                        if (pawn.Dead) continue;

                        // 伤员候选：卧床且饥饿（CurLevelPercentage < 0.3），且 FeedPatientUtility 判定需要喂食
                        // 注：不应用 ShouldSkipForMedical——伤员本身在床上休养（IsRecoveringInBed=true），
                        //   若应用守卫会永真跳过，导致永远无法喂食。FeedPatient 不移动伤员，伤员维持休养。
                        if (pawn.InBed() && pawn.needs?.food != null
                            && pawn.needs.food.CurLevelPercentage < 0.3f
                            && FeedPatientUtility.ShouldBeFed(pawn))
                        {
                            patientCandidates.Add(pawn);
                            continue;
                        }

                        // 伤员（Downed）不参与做医生
                        if (pawn.Downed) continue;

                        // 医生候选：非征召、非医疗 Job 中、非床上休养
                        // 医疗守卫：医生正在执行 TendPatient/DoBill 时不安排新 Job（打断当前治疗）
                        if (pawn.Drafted) continue;
                        if (PawnJobGuard.ShouldSkipForMedical(pawn)) continue;
                        // 喂食不需要 Care 工作标签过滤——任何空闲殖民者都能喂食
                        // 但排除幼童/未成年（无法喂食他人）——CanManageGear 已过滤动物，未成年仍可能通过
                        if (DLCCompat.IsChild(pawn)) continue;

                        doctorCandidates.Add(pawn);
                    }
                }

                if (patientCandidates.Count == 0 || doctorCandidates.Count == 0) return;

                // 按饥饿度升序排序伤员——越饿越优先（CurLevelPercentage 越小越优先）
                // List.Sort 非 LINQ，Tick 路径允许
                patientCandidates.Sort(ComparePatientByHungerAsc);

                // 逐个伤员分配医生：每个医生只安排一个喂食 Job，避免堆叠
                for (int i = 0; i < patientCandidates.Count; i++)
                {
                    Pawn patient = patientCandidates[i];
                    Pawn doctor = FindBestAvailableDoctor(patient);
                    if (doctor == null) continue;

                    AssignFeedJob(doctor, patient);
                    assignedDoctors.Add(doctor);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] 自动喂食伤员失败: " + ex.Message, FeedingErrorSalt);
            }
        }

        /// <summary>
        /// 伤员排序比较器：按食物需求 CurLevelPercentage 升序（越饿越优先）。
        /// 设计意图：重伤员饥饿度上升更快，优先喂食避免饿死。
        /// </summary>
        private static int ComparePatientByHungerAsc(Pawn a, Pawn b)
        {
            float fa = a.needs?.food?.CurLevelPercentage ?? 1f;
            float fb = b.needs?.food?.CurLevelPercentage ?? 1f;
            return fa.CompareTo(fb);
        }

        /// <summary>
        /// 为伤员找最佳可用医生：已分配的医生跳过，且医生能到达伤员。
        /// </summary>
        private static Pawn FindBestAvailableDoctor(Pawn patient)
        {
            for (int i = 0; i < doctorCandidates.Count; i++)
            {
                Pawn doctor = doctorCandidates[i];
                if (assignedDoctors.Contains(doctor)) continue;
                // 医生必须能到达伤员：跨地图或不可达时跳过
                if (doctor.Map != patient.Map) continue;
                if (!doctor.CanReach(patient, PathEndMode.ClosestTouch, Danger.Deadly)) continue;
                return doctor;
            }
            return null;
        }

        /// <summary>
        /// 安排医生喂食伤员的 Job：JobDefOf.FeedPatient。
        /// 食物来源：医生库存优先（FoodUtility.BestFoodInInventory 仅限 Meal 区间），
        /// 否则地图上找最近可食用食物（FoodSourceNotPlantOrTree 组）。
        /// count=1：一次喂食取一份，避免医生堆叠库存。
        /// </summary>
        private static void AssignFeedJob(Pawn doctor, Pawn patient)
        {
            // 双重医疗守卫：安排前再检查医生（伤员在床上不需要守卫，见 AllocateAllColonists 注释）
            if (PawnJobGuard.ShouldSkipForMedical(doctor)) return;

            // 食物来源1：医生库存中已有食物（仅限 Meal 区间，避免 raw/drug）
            // FoodUtility.BestFoodInInventory 默认 maxPref=MealLavish，minPref=NeverForNutrition
            // 此处收紧到 MealTerrible..MealLavish，避免给伤员吃 raw 食物
            Thing food = FoodUtility.BestFoodInInventory(doctor, patient,
                FoodPreferability.MealTerrible, FoodPreferability.MealLavish, 0f, false, false);

            // 食物来源2：地图上找最近可食用食物（FoodSourceNotPlantOrTree 组，排除植物/树）
            // 谓词：未禁用、可储备、有堆叠、排除药物/成瘾品（preferability != NeverForNutrition，
            //   FoodSourceNotPlantOrTree 组不排除 NeverForNutrition，WillEat(careIfNotAcceptable=false) 也不拒绝，
            //   不加此检查会把 Go-juice/Penoxycyline 等药物当作食物喂给伤员，伤员不获营养始终饥饿→反复喂食循环）、
            //   伤员愿意吃（WillEat 兼顾 Ideo/特质/头衔）
            if (food == null)
            {
                food = GenClosest.ClosestThingReachable(
                    doctor.Position, doctor.Map,
                    ThingRequest.ForGroup(ThingRequestGroup.FoodSourceNotPlantOrTree),
                    PathEndMode.ClosestTouch,
                    TraverseParms.For(doctor),
                    50f,
                    t => !t.IsForbidden(doctor) && doctor.CanReserve(t) && t.stackCount > 0
                         && t.def.ingestible != null
                         && t.def.ingestible.preferability != FoodPreferability.NeverForNutrition
                         && FoodUtility.WillEat(patient, t, doctor, false, false));
            }

            if (food == null)
            {
                if (AEDebug.IsActive)
                    AEDebug.Log(() => $"[AutoEverything] 喂食失败：伤员 {AEDebug.Label(patient)} 无可用食物来源");
                return;
            }

            // FeedPatient Job 结构：targetA=food (FoodSourceInd=A), targetB=patient (DelivereeInd=B)
            // JobMaker.MakeJob(JobDef, targetA, targetB) 对应此签名
            Job job = JobMaker.MakeJob(JobDefOf.FeedPatient, food, patient);
            job.count = 1;
            doctor.jobs.TryTakeOrderedJob(job, JobTag.Misc);

            if (AEDebug.IsActive)
                AEDebug.Log(() => $"[AutoEverything] 喂食安排: 医生 {AEDebug.Label(doctor)} → 伤员 {AEDebug.Label(patient)} (食物={food.LabelShort})");
        }
    }
}
