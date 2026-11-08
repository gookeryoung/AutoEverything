namespace AutoEverything.AutoMarkPawn
{
    /// <summary>
    /// 伤痕条数的纯函数判定：不接触 RimWorld 运行时对象，便于单元测试独立验证。
    /// 规则来源（2026-11-08 用户决策 + 原版逻辑）：
    /// - 疤痕必须"已显现"：IsPermanent 只表示命中时 roll 到会留疤，伤口仍在愈合时不能计入
    /// - 缺失部位必须"真缺失"：被仿生/假肢（AddedPart）覆盖的部位属于替换，不算伤痕
    /// - 条数上限 3，超出封顶
    /// </summary>
    public static class ScarMarkRules
    {
        /// <summary>头像上最多绘制的条数。</summary>
        public const int MaxBars = 3;

        /// <summary>
        /// InstantPermanentInjury 的哨兵阈值：生成即永久，无需等待愈合即可算疤痕。
        /// 与 Verse.HediffComp_GetsPermanent.CompPostInjuryHeal 中的 9999f 判断保持一致。
        /// </summary>
        public const float InstantPermanentThreshold = 9999f;

        /// <summary>
        /// 判断一条永久伤是否已成长为肉眼可见的疤痕。
        /// 原版 CompPostInjuryHeal 在 Severity 降到 permanentDamageThreshold 时钳制并固定，
        /// 因此在 Severity <= threshold 之前，它仍只是"正在愈合、将来会留疤的伤口"。
        /// </summary>
        public static bool IsScarRevealed(bool isPermanent, float severity, float permanentThreshold)
        {
            if (!isPermanent) return false;
            if (permanentThreshold >= InstantPermanentThreshold) return true;
            return severity <= permanentThreshold;
        }

        /// <summary>
        /// 判断缺失部位是否应计入伤痕：被 AddedPart（仿生/假肢）覆盖的部位视为替换而非缺失。
        /// </summary>
        public static bool ShouldCountMissingPart(bool coveredByAddedPart)
        {
            return !coveredByAddedPart;
        }

        /// <summary>条数封顶：0~3。</summary>
        public static int CapBars(int count)
        {
            if (count <= 0) return 0;
            return count > MaxBars ? MaxBars : count;
        }
    }
}
