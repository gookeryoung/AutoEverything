namespace AutoEverything.Core
{
    /// <summary>
    /// 殖民者战斗价值档次（用于全局重配优先级与 DEBUG 显示）。
    /// 设计：把"战斗价值"离散化为 6 档，便于玩家直观判断优先级。
    ///   X：无法从事暴力活动（如医疗特质 DisableViolent）
    ///   D：无火无战斗特质（基础农民）
    ///   C：无火但有战斗特质（如 Tough 农民）
    ///   B：单火/单 Major（任一射击或近战有兴趣，但非双 Major）
    ///   A：双火（射击+近战均为 Major 兴趣）
    ///   S：双火且带战斗特质（最高优先级）
    /// </summary>
    public enum CombatTier : byte
    {
        X = 0,
        D = 1,
        C = 2,
        B = 3,
        A = 4,
        S = 5
    }
}
