using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using AutoEverything.Core;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.AutoDrug
{
    /// <summary>
    /// 子功能3：自动治疗安排。
    ///
    /// 设计目的：
    /// - 扫描地图 FreeColonistsSpawned，找出需要治疗的伤员（HealthAIUtility.ShouldBeTendedNowByPlayer）
    /// - 找空闲医生（medSkill 高者优先，非征召、非医疗中、非战斗中）
    /// - 安排 JobDefOf.TendPatient Job：JobMaker.MakeJob(JobDefOf.TendPatient, patient)
    ///
    /// 医疗守卫（强制）：
    /// - 医生走 PawnJobGuard.ShouldSkipForMedical：正在执行 TendPatient/DoBill/Rescue 时不安排新 Job
    /// - 伤员走 PawnJobGuard.ShouldSkipForMedical：正在床上休养（IsRecoveringInBed）时不强制安排治疗
    ///   （伤员已在床上等医生，由游戏原生 work giver 触发，本模块仅作补充）
    ///
    /// 模式：参考 BeltAllocator——AllocateForPawn(pawn) 内部周期门控（3000 tick），
    /// 第一个触发的 Pawn 承担全局扫描成本。
    /// </summary>
    public static class TreatmentAllocator
    {
        // 全局分配间隔：与 DrugAllocator 一致（3000 tick ≈ 50 秒）
        // 治疗非紧急到秒级，原生 work giver 会处理紧急治疗
        private const int AllocationInterval = 3000;
        private static int lastAllocationTick = -9999;

        // 候选缓存（Tick 路径禁止 new List，复用静态字段）
        private static readonly List<Pawn> woundedCandidates = new List<Pawn>();
        private static readonly List<Pawn> doctorCandidates = new List<Pawn>();

        // 错误去重 salt：与 DrugPolicyManager/MedicationAllocator 区分
        private const int TreatmentErrorSalt = 0xA610;

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
        /// 全局扫描：收集所有伤员 + 所有可用医生，按 medSkill 降序匹配。
        /// try-catch 隔离：失败时 Log.ErrorOnce 记录，不影响其他 Pawn 评估。
        /// </summary>
        private static void AllocateAllColonists()
        {
            if (!AESettings.autoTreatment) return;

            try
            {
                woundedCandidates.Clear();
                doctorCandidates.Clear();

                foreach (Map map in Find.Maps)
                {
                    if (map?.mapPawns == null) continue;
                    foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
                    {
                        if (pawn == null) continue;
                        if (DLCCompat.IsGhoul(pawn)) continue;
                        if (!PawnSuitabilityChecker.CanManageGear(pawn)) continue;
                        if (pawn.Dead || pawn.Downed) continue;

                        // 伤员候选：HealthAIUtility.ShouldBeTendedNowByPlayer 判定需立即治疗
                        // 医疗守卫：伤员正在床上休养（IsRecoveringInBed）时跳过——
                        //   伤员已在床上等医生，由游戏原生 work giver 触发，本模块不打断休养
                        if (HealthAIUtility.ShouldBeTendedNowByPlayer(pawn) && !PawnJobGuard.ShouldSkipForMedical(pawn))
                        {
                            woundedCandidates.Add(pawn);
                            continue;
                        }

                        // 医生候选：medSkill >= 4，非征召，非医疗 Job 中，非床上休养
                        // 医疗守卫：医生正在执行 TendPatient/DoBill 时不安排新 Job（打断当前治疗）
                        if (pawn.Drafted) continue;
                        if (PawnJobGuard.ShouldSkipForMedical(pawn)) continue;

                        int medSkill = pawn.skills?.GetSkill(SkillDefOf.Medicine)?.Level ?? 0;
                        if (medSkill < 4) continue;
                        if (pawn.WorkTagIsDisabled(WorkTags.Caring)) continue;

                        doctorCandidates.Add(pawn);
                    }
                }

                if (woundedCandidates.Count == 0 || doctorCandidates.Count == 0) return;

                // 按 medSkill 降序排序医生——List.Sort 非 LINQ，Tick 路径允许
                // 设计意图：medSkill 高者优先治疗，提高治疗成功率与速度
                doctorCandidates.Sort(CompareDoctorByMedSkillDesc);

                // 逐个伤员分配医生：每个医生只安排一个治疗 Job，避免堆叠
                // 用静态 HashSet 跟踪已分配的医生，避免重复安排
                assignedDoctors.Clear();
                for (int i = 0; i < woundedCandidates.Count; i++)
                {
                    Pawn patient = woundedCandidates[i];
                    Pawn doctor = FindBestAvailableDoctor(patient);
                    if (doctor == null) continue;

                    AssignTendJob(doctor, patient);
                    assignedDoctors.Add(doctor);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] 自动治疗安排失败: " + ex.Message, TreatmentErrorSalt);
            }
        }

        // 已分配医生缓存：每轮扫描重用，避免同一医生被分配多个伤员
        private static readonly HashSet<Pawn> assignedDoctors = new HashSet<Pawn>();

        /// <summary>
        /// 医生排序比较器：按 Medicine 技能等级降序。
        /// medSkill 高者优先治疗重伤员，提高治疗成功率。
        /// </summary>
        private static int CompareDoctorByMedSkillDesc(Pawn a, Pawn b)
        {
            int sa = a.skills?.GetSkill(SkillDefOf.Medicine)?.Level ?? 0;
            int sb = b.skills?.GetSkill(SkillDefOf.Medicine)?.Level ?? 0;
            return sb.CompareTo(sa);
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
        /// 安排医生治疗伤员的 Job：JobDefOf.TendPatient。
        /// TryTakeOrderedJob 会取消医生当前 Job，但医生候选已过滤掉医疗 Job 中的 Pawn，
        /// 安全的取消（如 Idle、Hauling）不会影响关键工作。
        /// </summary>
        private static void AssignTendJob(Pawn doctor, Pawn patient)
        {
            // 双重医疗守卫：安排前再检查一次（候选过滤后到此处可能医生已被其他逻辑安排）
            if (PawnJobGuard.ShouldSkipForMedical(doctor)) return;
            if (PawnJobGuard.ShouldSkipForMedical(patient)) return;

            Job job = JobMaker.MakeJob(JobDefOf.TendPatient, patient);
            doctor.jobs.TryTakeOrderedJob(job, JobTag.Misc);

            if (AEDebug.IsActive)
                AEDebug.Log(() => $"[AutoEverything] 治疗安排: 医生 {AEDebug.Label(doctor)} → 伤员 {AEDebug.Label(patient)}");
        }
    }
}
