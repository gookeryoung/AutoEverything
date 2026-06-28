using RimWorld;
using Verse;

namespace AutoEquipment.Scoring.Apparels
{
    /// <summary>
    /// 防具工作加成评分：医生偏好医疗加成，工人偏好工作速度。
    /// 仅在工作情境下生效。
    /// </summary>
    public class ApparelWorkScorer : IScorer<Apparel>
    {
        public string Name => "工作";

        public void Score(Pawn pawn, Apparel gear, Role role, GearContext context,
                          GearWeights weights, ScoreBreakdown breakdown)
        {
            if (context != GearContext.Work) return;

            switch (role)
            {
                case Role.Doctor:
                    float medSurgery = gear.GetStatValue(StatDefOf.MedicalSurgerySuccessChance, true, -1);
                    if (medSurgery != 0f)
                    {
                        float score = medSurgery * 100f;
                        breakdown.AddScore(Name, $"手术成功率{medSurgery:F2} × 100", score);
                    }
                    float medTend = gear.GetStatValue(StatDefOf.MedicalTendQuality, true, -1);
                    if (medTend != 0f)
                    {
                        float score = medTend * 80f;
                        breakdown.AddScore(Name, $"医疗质量{medTend:F2} × 80", score);
                    }
                    break;

                case Role.Worker:
                    float workSpeed = gear.GetStatValue(StatDefOf.WorkSpeedGlobal, true, -1);
                    if (workSpeed != 0f)
                    {
                        float score = workSpeed * weights.w_workspeed;
                        breakdown.AddScore(Name, $"工作速度{workSpeed:F2} × {weights.w_workspeed:F0}", score);
                    }
                    break;

                case Role.Shooter:
                case Role.Brawler:
                    // 战斗角色工作中仍重视护甲
                    float armor = gear.GetStatValue(StatDefOf.ArmorRating_Sharp);
                    if (armor > 0f)
                    {
                        float score = armor * 80f;
                        breakdown.AddScore(Name, $"战斗角色+护甲{armor:F1} × 80", score);
                    }
                    break;
            }
        }
    }
}
