using RimWorld;
using Verse;

namespace AutoEquipment
{
    /// <summary>
    /// 战斗画像工具：统一判定 Pawn 的战斗行为画像。
    /// 抽取自 SidearmAllocator 与 BeltAllocator 的重复实现，
    /// 保证"纯近战射击者"等判定逻辑在副武器与腰带分配间一致。
    /// </summary>
    public static class PawnCombatProfile
    {
        /// <summary>
        /// 纯近战角色判定：射击技能无火（passion=None）。
        /// 此类殖民者远程射击天赋不足，副武器应优先 EMP 而非普通远程武器，
        /// 腰带层应优先护盾腰带或消防背包以贴近作战。
        /// </summary>
        public static bool IsPureMeleeShooter(Pawn pawn)
        {
            if (pawn?.skills == null) return false;
            SkillRecord shooting = pawn.skills.GetSkill(SkillDefOf.Shooting);
            if (shooting == null) return true;
            return shooting.passion == Passion.None;
        }
    }
}
