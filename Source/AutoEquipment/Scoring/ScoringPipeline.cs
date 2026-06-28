using System.Collections.Generic;
using RimWorld;
using Verse;

namespace AutoEquipment.Scoring
{
    /// <summary>
    /// 评分管线：按顺序执行所有 IScorer 策略，累加分数到 ScoreBreakdown。
    /// 设计模式：责任链/管线——每个 Scorer 独立处理，结果累加。
    /// 否决（Veto）会短路后续 Scorer（除耐久修正外）。
    /// </summary>
    /// <typeparam name="TThing">被评分对象类型</typeparam>
    public class ScoringPipeline<TThing> where TThing : Thing
    {
        private readonly List<IScorer<TThing>> scorers;

        public ScoringPipeline(List<IScorer<TThing>> scorers)
        {
            this.scorers = scorers ?? new List<IScorer<TThing>>();
        }

        /// <summary>
        /// 执行评分管线，返回带明细的 ScoreBreakdown。
        /// </summary>
        public ScoreBreakdown Evaluate(Pawn pawn, TThing gear, Role role,
                                       GearContext context, GearWeights weights)
        {
            var breakdown = new ScoreBreakdown();

            // 按顺序执行所有 Scorer
            for (int i = 0; i < scorers.Count; i++)
            {
                // 否决后跳过后续 Scorer（耐久修正除外，仍需应用）
                if (breakdown.Vetoed)
                {
                    // 仅允许耐久修正 Scorer 继续执行
                    // 但实际上否决分数已经是 -9000，耐久修正无意义
                    break;
                }

                try
                {
                    scorers[i].Score(pawn, gear, role, context, weights, breakdown);
                }
                catch (System.Exception ex)
                {
                    // 单个 Scorer 失败不应中断整个管线
                    Log.WarningOnce($"[AutoEquipment] 评分器 {scorers[i].Name} 失败: {ex.Message}",
                        pawn.thingIDNumber ^ gear.thingIDNumber ^ i);
                }
            }

            return breakdown;
        }

        /// <summary>
        /// 快速评分：仅返回总分，不构造明细。
        /// 性能路径下使用，避免明细列表分配。
        /// </summary>
        public float EvaluateScore(Pawn pawn, TThing gear, Role role,
                                   GearContext context, GearWeights weights)
        {
            // 即使快速路径也用 ScoreBreakdown，因为 AddScore 是 O(1)
            // 明细列表的分配在性能路径下可接受（每个装备约 5-10 项）
            var breakdown = Evaluate(pawn, gear, role, context, weights);
            return breakdown.Vetoed ? breakdown.VetoScore : breakdown.Total;
        }
    }
}
