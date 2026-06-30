using RimWorld;
using Verse;
using AutoEverything.RoleEvaluation;
using AutoEverything.AutoEquipment;

namespace AutoEverything.AutoEquipment.Scoring.Apparels
{
    /// <summary>
    /// 实验服偏好：研究型殖民者穿实验服时加分。
    /// 研究型 = 非 Brawler + 近战/射击均无火 + 医疗或研究 ≥ 8。
    /// 设计意图：非战斗型殖民者应优先穿实验服（提供 ResearchSpeed/EntityStudyRate 加成），
    /// 让其在同类防具评分中胜出。加分 +50 参考 WeaponSkillScorer 双修远程偏好分。
    /// </summary>
    public class ApparelLabCoatScorer : IScorer<Apparel>
    {
        public string Name => "实验服偏好";

        public void Score(Pawn pawn, Apparel gear, Role role, GearContext context,
                          GearWeights weights, ScoreBreakdown breakdown)
        {
            if (!IsResearchOriented(pawn, role)) return;
            if (!GearDefClassifier.IsLabCoat(gear)) return;

            breakdown.AddScore(Name, "研究型+实验服=偏好", 50f);
        }

        /// <summary>
        /// 研究型判定：非 Brawler + 近战/射击均无火 + 医疗或研究 ≥ 8。
        /// </summary>
        private static bool IsResearchOriented(Pawn pawn, Role role)
        {
            // Brawler 是重甲前排，应穿重甲战斗而非实验服
            if (role == Role.Brawler) return false;

            var skills = pawn.skills;
            if (skills == null) return false;

            // 近战和射击均无火（None）—— 非战斗型
            SkillRecord shooting = skills.GetSkill(SkillDefOf.Shooting);
            SkillRecord melee = skills.GetSkill(SkillDefOf.Melee);
            if (shooting == null || melee == null) return false;
            if (shooting.passion != Passion.None || melee.passion != Passion.None) return false;

            // 医疗 ≥ 8 或 研究 ≥ 8 —— 有专长可发挥
            SkillRecord medical = skills.GetSkill(SkillDefOf.Medicine);
            SkillRecord research = skills.GetSkill(SkillDefOf.Intellectual);
            int medLevel = medical?.Level ?? 0;
            int resLevel = research?.Level ?? 0;
            return medLevel >= 8 || resLevel >= 8;
        }
    }
}
