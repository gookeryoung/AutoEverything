using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

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
        /// 判断 Pawn 是否正在参与仪式/聚会/商队组建等 Lord 管理的集体活动。
        /// 参与者当前 Job 可能是等待/走位/执行动作，单看 JobDef 无法覆盖全部阶段，故查 Lord 的 LordJob 类型：
        /// - LordJob_VoluntarilyJoinable 基类：一次覆盖 Ideo 仪式（LordJob_Ritual 系，含授予仪式/演讲/鼓舞派对）
        ///   及婚礼（LordJob_Joinable_MarriageCeremony 直连此类）/派对/音乐会（经 LordJob_Joinable_Gathering）
        /// - LordJob_PsychicRitual：Anomaly 心灵仪式（含 Repeating 子类），独立继承链必须单独判断
        /// - LordJob_FormAndSendCaravan：商队组建，打断会延迟商队出发
        /// TryTakeOrderedJob 取药 / SetPriority 工作重配都会取消当前 Job，导致仪式中断或聚会散场。
        /// </summary>
        public static bool IsInRitualOrGathering(Pawn pawn)
        {
            if (pawn == null) return false;
            Lord lord = pawn.GetLord();
            if (lord == null) return false;
            LordJob lordJob = lord.LordJob;
            return lordJob is LordJob_VoluntarilyJoinable
                || lordJob is LordJob_PsychicRitual
                || lordJob is LordJob_FormAndSendCaravan;
        }

        /// <summary>
        /// 判断 Pawn 是否正在执行不应被自动功能打断的日常活动。
        /// 覆盖：正常睡眠（LayDown，医疗休养已由 ShouldSkipForMedical 处理）、
        /// 冥想（Meditate/MeditatePray，psyfocus 恢复被打断丢进度）、
        /// 死眠（Deathrest，血族休眠加成失效）、实体研究（StudyInteract，Anomaly 长任务）、
        /// 玩家手动命令（playerForced，玩家右键指派的 Job 优先级最高）。
        /// 吃饭（Ingest）不在此列：原生各 Job 也会打断吃饭，属正常游戏节奏。
        /// </summary>
        public static bool IsDoingProtectedActivity(Pawn pawn)
        {
            if (pawn == null) return false;
            Job job = pawn.CurJob;
            if (job == null) return false;
            if (job.playerForced) return true;
            JobDef def = job.def;
            return def == JobDefOf.LayDown
                || def == JobDefOf.Meditate
                || def == JobDefOf.MeditatePray
                || def == JobDefOf.Deathrest
                || def == JobDefOf.StudyInteract;
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
