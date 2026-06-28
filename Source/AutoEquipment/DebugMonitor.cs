using RimWorld;
using Verse;
using AutoEquipment.Scoring;

namespace AutoEquipment
{
    /// <summary>
    /// 调试监测系统：向玩家/开发者展示装备评分与换装事件。
    /// 设计模式：观察者模式——订阅评分与换装事件，按开关输出。
    /// 监测开关由玩家在设置界面勾选，默认关闭以避免性能影响。
    /// </summary>
    public static class DebugMonitor
    {
        // ===================== 监测开关 =====================

        /// <summary>主开关：启用监测</summary>
        public static bool monitorEnabled = false;

        /// <summary>监测换装事件（顶部消息）</summary>
        public static bool monitorSwapEvents = true;

        /// <summary>监测武器评分（日志）</summary>
        public static bool monitorWeaponScore = false;

        /// <summary>监测防具评分（日志）</summary>
        public static bool monitorApparelScore = false;

        /// <summary>显示分值构成明细</summary>
        public static bool monitorBreakdown = true;

        /// <summary>显示评分对比（候选 vs 当前）</summary>
        public static bool monitorComparison = true;

        // ===================== 事件上报 =====================

        /// <summary>
        /// 上报换装事件：在游戏顶部消息区显示。
        /// </summary>
        /// <param name="pawn">换装 Pawn</param>
        /// <param name="slot">装备槽（武器/防具/副武器/库存）</param>
        /// <param name="oldGear">旧装备（可能为 null）</param>
        /// <param name="newGear">新装备</param>
        /// <param name="oldScore">旧装备评分</param>
        /// <param name="newScore">新装备评分</param>
        public static void ReportSwap(Pawn pawn, string slot,
                                       Thing oldGear, Thing newGear,
                                       float oldScore, float newScore)
        {
            if (!monitorEnabled || !monitorSwapEvents) return;
            if (pawn == null || newGear == null) return;

            string oldLabel = oldGear?.LabelShort ?? "无";
            string newLabel = newGear.LabelShort;
            float improvement = newScore - oldScore;
            float improvementPct = oldScore > 0 ? (improvement / oldScore * 100f) : 0f;

            string msg = "[AE] " + pawn.LabelShort + " " + slot +
                         " 换下:" + oldLabel + " → 换上:" + newLabel +
                         " (" + newScore.ToString("F0") + " vs " + oldScore.ToString("F0") +
                         ", " + (improvement >= 0 ? "+" : "") + improvement.ToString("F0") +
                         " = " + (improvement >= 0 ? "+" : "") + improvementPct.ToString("F0") + "%)";

            Messages.Message(msg, MessageTypeDefOf.TaskCompletion);
        }

        /// <summary>
        /// 上报武器评分明细：在日志输出。
        /// </summary>
        public static void ReportWeaponScore(Pawn pawn, Thing candidate,
                                             ScoreBreakdown breakdown,
                                             Thing currentGear, float currentScore)
        {
            if (!monitorEnabled || !monitorWeaponScore) return;
            if (pawn == null || candidate == null) return;

            // 明细报告
            if (monitorBreakdown)
            {
                Log.Message(breakdown.BuildReport(pawn.LabelShort, candidate.LabelShort));
            }

            // 对比报告
            if (monitorComparison && currentGear != null)
            {
                float candidateScore = breakdown.Vetoed ? breakdown.VetoScore : breakdown.Total;
                float diff = candidateScore - currentScore;
                float pct = currentScore > 0 ? (diff / currentScore * 100f) : 0f;

                Log.Message($"[AutoEquipment] {pawn.LabelShort} 武器评分对比:\n" +
                            $"  候选 {candidate.LabelShort}: {candidateScore:+0.0;-0.0;0.0}\n" +
                            $"  当前 {currentGear.LabelShort}: {currentScore:+0.0;-0.0;0.0}\n" +
                            $"  差异: {(diff >= 0 ? "+" : "")}{diff:F0} ({(pct >= 0 ? "+" : "")}{pct:F1}%)");
            }
        }

        /// <summary>
        /// 上报防具评分明细：在日志输出。
        /// </summary>
        public static void ReportApparelScore(Pawn pawn, Apparel candidate,
                                              ScoreBreakdown breakdown,
                                              Apparel currentGear, float currentScore)
        {
            if (!monitorEnabled || !monitorApparelScore) return;
            if (pawn == null || candidate == null) return;

            // 明细报告
            if (monitorBreakdown)
            {
                Log.Message(breakdown.BuildReport(pawn.LabelShort, candidate.LabelShort));
            }

            // 对比报告
            if (monitorComparison && currentGear != null)
            {
                float candidateScore = breakdown.Vetoed ? breakdown.VetoScore : breakdown.Total;
                float diff = candidateScore - currentScore;
                float pct = currentScore > 0 ? (diff / currentScore * 100f) : 0f;

                Log.Message($"[AutoEquipment] {pawn.LabelShort} 防具评分对比:\n" +
                            $"  候选 {candidate.LabelShort}: {candidateScore:+0.0;-0.0;0.0}\n" +
                            $"  当前 {currentGear.LabelShort}: {currentScore:+0.0;-0.0;0.0}\n" +
                            $"  差异: {(diff >= 0 ? "+" : "")}{diff:F0} ({(pct >= 0 ? "+" : "")}{pct:F1}%)");
            }
        }

        /// <summary>
        /// 存档持久化：保存监测开关状态。
        /// </summary>
        public static void ExposeData()
        {
            Scribe_Values.Look(ref monitorEnabled, "ae_monitorEnabled", false);
            Scribe_Values.Look(ref monitorSwapEvents, "ae_monitorSwapEvents", true);
            Scribe_Values.Look(ref monitorWeaponScore, "ae_monitorWeaponScore", false);
            Scribe_Values.Look(ref monitorApparelScore, "ae_monitorApparelScore", false);
            Scribe_Values.Look(ref monitorBreakdown, "ae_monitorBreakdown", true);
            Scribe_Values.Look(ref monitorComparison, "ae_monitorComparison", true);
        }
    }
}
