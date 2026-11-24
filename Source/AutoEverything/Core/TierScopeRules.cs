namespace AutoEverything.Core
{
    /// <summary>
    /// 人员评级范围的人群分类。
    /// 位于 Core 且不引用 Verse / RimWorld 类型，便于单元测试。
    /// </summary>
    public enum PawnScopeKind
    {
        Colonist = 0,
        Visitor = 1,
        Slave = 2,
        Prisoner = 3
    }

    /// <summary>
    /// 人员评级范围规则（纯逻辑）：把 Pawn 归入人群分类，并判断当前范围配置是否包含该分类。
    /// 范围开关见 <see cref="AESettings"/> 的 tierScope* 字段。
    /// </summary>
    public static class TierScopeRules
    {
        /// <summary>
        /// 分类优先级：囚犯 &gt; 奴隶 &gt; 访问者 &gt; 殖民者。
        /// 奴隶/囚犯也可能带有客状态，必须先按身份判定；其余人形按殖民者处理。
        /// </summary>
        public static PawnScopeKind Classify(bool isPrisonerOfColony, bool isSlaveOfColony, bool isVisitor)
        {
            if (isPrisonerOfColony) return PawnScopeKind.Prisoner;
            if (isSlaveOfColony) return PawnScopeKind.Slave;
            if (isVisitor) return PawnScopeKind.Visitor;
            return PawnScopeKind.Colonist;
        }

        /// <summary>当前范围配置是否包含该人群（奴隶与囚犯共用 slavesPrisoners 开关）。</summary>
        public static bool IsIncluded(bool colonists, bool visitors, bool slavesPrisoners, PawnScopeKind kind)
        {
            switch (kind)
            {
                case PawnScopeKind.Visitor:
                    return visitors;
                case PawnScopeKind.Slave:
                case PawnScopeKind.Prisoner:
                    return slavesPrisoners;
                default:
                    return colonists;
            }
        }
    }
}
