using System.Collections.Generic;
using RimWorld;
using Verse;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.AutoEquipment.Scoring
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

        // 性能路径静态缓存：复用 ScoreBreakdown 实例避免 Tick 路径 GC 压力
        // RimWorld 单线程，调用方（GearScorer.ScoreWeapon/ScoreApparel）在下次评分前使用完返回值
        // 泛型类每个 TThing 实例独立缓存（武器管线与防具管线互不干扰）
        private static readonly ScoreBreakdown fastBreakdown = new ScoreBreakdown(false);

        public ScoringPipeline(List<IScorer<TThing>> scorers)
        {
            this.scorers = scorers ?? new List<IScorer<TThing>>();
        }

        /// <summary>
        /// 执行评分管线，返回带明细的 ScoreBreakdown。
        /// 性能路径调用方应使用 EvaluateFast（collectItems=false）避免 GC。
        /// </summary>
        public ScoreBreakdown Evaluate(Pawn pawn, TThing gear, Role role,
                                       GearContext context, GearWeights weights)
        {
            return EvaluateCore(pawn, gear, role, context, weights, true);
        }

        /// <summary>
        /// 性能路径入口：跳过 items 累积，仅计算 Total。
        /// 用于 ScoreWeapon/ScoreApparel 非 WithBreakdown 版本，
        /// 避免 Tick 路径中 N 件候选 × M 个 Scorer × 字符串插值分配。
        /// </summary>
        public ScoreBreakdown EvaluateFast(Pawn pawn, TThing gear, Role role,
                                           GearContext context, GearWeights weights)
        {
            return EvaluateCore(pawn, gear, role, context, weights, false);
        }

        /// <summary>
        /// 评分管线核心实现：按顺序执行所有 IScorer，累加分数到 ScoreBreakdown。
        /// 否决（Veto）会短路后续 Scorer（除耐久修正外）。
        /// </summary>
        /// <param name="collectItems">true=收集明细项（调试路径）；false=仅累加 Total（性能路径）</param>
        private ScoreBreakdown EvaluateCore(Pawn pawn, TThing gear, Role role,
                                            GearContext context, GearWeights weights,
                                            bool collectItems)
        {
            // 性能路径复用静态缓存避免 GC，调试路径 new 独立实例（BuildReport 需要持久数据）
            ScoreBreakdown breakdown = collectItems
                ? new ScoreBreakdown(true)
                : fastBreakdown;
            if (!collectItems) breakdown.Reset();

            // 按顺序执行所有 Scorer
            for (int i = 0; i < scorers.Count; i++)
            {
                // 否决后短路：跳过后续所有 Scorer
                // 耐久修正 Scorer 即使在管线末尾也不会执行——
                // 否决分已是 -9000，再乘以耐久系数无意义
                if (breakdown.Vetoed)
                {
                    break;
                }

                try
                {
                    scorers[i].Score(pawn, gear, role, context, weights, breakdown);
                }
                catch (System.Exception ex)
                {
                    // 单个 Scorer 失败不应中断整个管线
                    Log.WarningOnce($"[AutoEverything] 评分器 {scorers[i].Name} 失败: {ex.Message}",
                        pawn.thingIDNumber ^ gear.thingIDNumber ^ i);
                }
            }

            return breakdown;
        }
    }
}