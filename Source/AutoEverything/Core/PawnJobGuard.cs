using RimWorld;
using Verse;
using Verse.AI;

namespace AutoEverything.Core
{
    /// <summary>
    /// 殖民者 Job 守卫：判断 Pawn 是否正在执行不应被打断的 Job。
    ///
    /// 供所有"会修改 Pawn 状态的全局入口"复用：
    /// - WorkAllocator.ReallocateAll（工作优先级重配）
    ///
    /// 设计意图：SetPriority 会取消 Pawn 当前 Job，
    /// 若 Pawn 正在手术/治疗/休养，会导致手术死循环或伤员失救死亡。
    /// 所有全局重配入口必须在收集候选时调用本守卫跳过此类 Pawn。
    /// </summary>
    public static class PawnJobGuard
    {
        /// <summary>
        /// 判断 Pawn 是否正在执行医疗工作（医生视角）。
        /// 包含：治疗伤员、治疗实体、救援、送床手术、执行手术 DoBill(Bill_Medical)。
        /// 漏检 DoBill 会导致取药 TryTakeOrderedJob 取消手术 Job，手术永远做不完。
        /// </summary>
        public static bool IsDoingMedicalJob(Pawn pawn)
        {
            if (pawn == null) return false;
            Job job = pawn.CurJob;
            if (job == null) return false;
            JobDef def = job.def;
            return def == JobDefOf.TendPatient
                || def == JobDefOf.TendEntity
                || def == JobDefOf.Rescue
                || def == JobDefOf.TakeToBedToOperate
                || (def == JobDefOf.DoBill && job.bill is Bill_Medical);
        }

        /// <summary>
        /// 判断 Pawn 是否正在床上休养（伤员视角）。
        /// 卧床且需要医疗休养——过滤掉正常睡眠，仅捕获受伤/患病休养。
        /// 打断 LayDown Job 会中断免疫力/治疗进度，导致重伤者死亡。
        /// </summary>
        public static bool IsRecoveringInBed(Pawn pawn)
        {
            if (pawn == null) return false;
            return pawn.InBed() && HealthAIUtility.ShouldSeekMedicalRest(pawn);
        }

        /// <summary>
        /// 综合守卫：正在执行医疗工作 或 正在休养。
        /// 所有全局重配入口应用此方法跳过 Pawn。
        /// </summary>
        public static bool ShouldSkipForMedical(Pawn pawn)
        {
            return IsDoingMedicalJob(pawn) || IsRecoveringInBed(pawn);
        }
    }
}
