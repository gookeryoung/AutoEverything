using System.Collections.Generic;
using System.Text;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.AutoEquipment.Scoring
{
    /// <summary>
    /// 评分明细建造者：记录每个评分维度的加分项，最终汇总总分。
    /// 设计模式：建造者模式——逐步累积评分项，支持调试输出。
    ///
    /// 性能优化：
    /// - 性能路径（ScoreWeapon/ScoreApparel 非 WithBreakdown 版本）传 collectItems=false，
    ///   AddScore 仅累加 Total，不构造 description 字符串、不分配 ScoreItem
    /// - 调试路径（ScoreWeaponWithBreakdown 或监测路径）传 collectItems=true，
    ///   完整记录 items 供 BuildReport 输出
    /// </summary>
    public class ScoreBreakdown
    {
        // 评分项列表：仅在 collectItems=true 时分配，避免 Tick 路径 GC 压力
        // 字段初始化器先于构造函数体执行，因此 items 必须在此处初始化为 null，
        // 在构造函数内根据 collectItems 决定是否分配
        private readonly List<ScoreItem> items;

        // 实例默认值：由 Evaluate 调用方通过构造函数指定
        // 性能路径传 false，调试路径传 true
        private readonly bool collectItems;

        /// <summary>
        /// 默认构造：collectItems=true，兼容旧调用方与 ScoreWeaponWithBreakdown 调试路径。
        /// </summary>
        public ScoreBreakdown() : this(true) { }

        /// <summary>
        /// 指定是否收集明细项。
        /// 性能路径（非 WithBreakdown）传 false，跳过 items 累积与字符串构造。
        /// </summary>
        public ScoreBreakdown(bool collectItems)
        {
            this.collectItems = collectItems;
            // 仅在需要收集明细时分配 List，性能路径保持 null
            if (collectItems) items = new List<ScoreItem>();
        }

        /// <summary>
        /// 总分（所有加分项之和）。
        /// 性能路径下可仅使用此字段，跳过 items 累积。
        /// </summary>
        public float Total { get; private set; }

        /// <summary>
        /// 是否正在收集明细项。Scorer 可据此跳过字符串构造。
        /// 性能路径（false）下 AddScore 仍可被调用，但 description 会被忽略——
        /// 为彻底避免字符串分配，Scorer 应在 CollectItems=false 时传 null 或常量字符串。
        /// </summary>
        public bool CollectItems => collectItems;

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
        /// 性能路径（collectItems=false）仅累加 Total，不分配 ScoreItem、不构造字符串。
        /// </summary>
        /// <param name="dimension">维度名（如"技能"、"特质"）</param>
        /// <param name="description">说明（如"射击12 × 4.0 × 2.0(双火)"）</param>
        /// <param name="score">分值（正为加，负为减）</param>
        public void AddScore(string dimension, string description, float score)
        {
            // 关键优化：性能路径跳过 items 累积，避免字符串与 ScoreItem 分配
            if (collectItems && items != null)
            {
                items.Add(new ScoreItem(dimension, description, score));
            }
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
        /// 重置状态供静态缓存复用（性能路径避免每次 new ScoreBreakdown 的 GC 压力）。
        /// RimWorld 单线程，调用方在下次评分前使用完返回值即可。
        /// </summary>
        public void Reset()
        {
            Total = 0f;
            Vetoed = false;
            VetoScore = -9000f;
            items?.Clear();
        }

        /// <summary>
        /// 应用乘法修正（如耐久度修正）。
        /// 修正会记录为一个独立评分项，便于调试。
        /// 性能路径跳过 items 累积，仅调整 Total。
        /// </summary>
        public void ApplyMultiplier(string dimension, string description, float multiplier)
        {
            float delta = Total * (multiplier - 1f);
            if (collectItems && items != null)
            {
                items.Add(new ScoreItem(dimension, description, delta));
            }
            Total *= multiplier;
        }

        /// <summary>
        /// 生成中文可读的评分明细文本（用于调试监测）。
        /// 仅在 collectItems=true 时有效。
        /// </summary>
        public string BuildReport(string pawnLabel, string gearLabel)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[AutoEverything] {pawnLabel} 评分明细");
            sb.AppendLine($"候选: {gearLabel}");
            sb.AppendLine(new string('─', 40));

            if (items != null)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    sb.AppendLine($"  [{item.Dimension}] {item.Description}: {item.Score:+0.0;-0.0;0.0}");
                }
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