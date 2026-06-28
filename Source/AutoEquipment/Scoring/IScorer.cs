using RimWorld;
using Verse;

namespace AutoEquipment.Scoring
{
    /// <summary>
    /// 评分策略接口：所有评分维度（技能、特质、情境、DPS 等）实现此接口。
    /// 设计模式：策略模式——每个评分维度是一个可独立替换的策略。
    /// </summary>
    /// <typeparam name="TThing">被评分对象类型（Thing 或 Apparel）</typeparam>
    public interface IScorer<TThing> where TThing : Thing
    {
        /// <summary>
        /// 评分维度名称（用于调试明细输出，如"技能"、"特质"）。
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 对装备进行评分，将加分项写入 breakdown。
        /// 实现应通过 breakdown.AddScore() 累加分数，便于调试追踪。
        /// </summary>
        void Score(Pawn pawn, TThing gear, Role role, GearContext context,
                   GearWeights weights, ScoreBreakdown breakdown);
    }
}
