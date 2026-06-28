using System.Collections.Generic;
using System.Text;

namespace AutoEquipment.Scoring
{
    /// <summary>
    /// 评分明细建造者：记录每个评分维度的加分项，最终汇总总分。
    /// 设计模式：建造者模式——逐步累积评分项，支持调试输出。
    /// </summary>
    public class ScoreBreakdown
    {
        // 评分项列表：每项包含维度名、说明、分值
        private readonly List<ScoreItem> items = new List<ScoreItem>();

        /// <summary>
        /// 总分（所有加分项之和）。
        /// 性能路径下可仅使用此字段，跳过 items 累积。
        /// </summary>
        public float Total { get; private set; }

        /// <summary>
        /// 是否被否决（如生物编码不匹配、格斗者拿远程）。
        /// 一旦否决，最终评分直接返回极端负分，忽略其他维度。
        /// </summary>
        public bool Vetoed { get; private set; }

        /// <summary>
        /// 否决时的最终分数（默认 -9000）。
        /// </summary>
        public float VetoScore { get; private set; } = -9000f;

        /// <summary>
        /// 添加一个评分项。
        /// </summary>
        /// <param name="dimension">维度名（如"技能"、"特质"）</param>
        /// <param name="description">说明（如"射击12 × 4.0 × 2.0(双火)"）</param>
        /// <param name="score">分值（正为加，负为减）</param>
        public void AddScore(string dimension, string description, float score)
        {
            items.Add(new ScoreItem(dimension, description, score));
            Total += score;
        }

        /// <summary>
        /// 否决：直接返回极端负分。
        /// 用于硬性约束：生物编码不匹配、格斗者拿远程武器等。
        /// </summary>
        public void Veto(float score = -9000f)
        {
            Vetoed = true;
            VetoScore = score;
        }

        /// <summary>
        /// 应用乘法修正（如耐久度修正）。
        /// 修正会记录为一个独立评分项，便于调试。
        /// </summary>
        public void ApplyMultiplier(string dimension, string description, float multiplier)
        {
            float delta = Total * (multiplier - 1f);
            items.Add(new ScoreItem(dimension, description, delta));
            Total *= multiplier;
        }

        /// <summary>
        /// 生成中文可读的评分明细文本（用于调试监测）。
        /// </summary>
        public string BuildReport(string pawnLabel, string gearLabel)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[AutoEquipment] {pawnLabel} 评分明细");
            sb.AppendLine($"候选: {gearLabel}");
            sb.AppendLine(new string('─', 40));

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                sb.AppendLine($"  [{item.Dimension}] {item.Description}: {item.Score:+0.0;-0.0;0.0}");
            }

            sb.AppendLine(new string('─', 40));
            sb.AppendLine($"  合计: {Total:+0.0;-0.0;0.0}");

            if (Vetoed)
            {
                sb.AppendLine($"  [否决] 最终: {VetoScore}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// 单个评分项。
        /// </summary>
        private struct ScoreItem
        {
            public string Dimension { get; }
            public string Description { get; }
            public float Score { get; }

            public ScoreItem(string dimension, string description, float score)
            {
                Dimension = dimension;
                Description = description;
                Score = score;
            }
        }
    }
}
