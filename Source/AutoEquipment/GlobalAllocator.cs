using System.Collections.Generic;
using RimWorld;
using Verse;

namespace AutoEquipment
{
    /// <summary>
    /// 全局重配：按战斗价值降序遍历所有殖民者，依次强制评估全部装备。
    ///
    /// 设计目的：
    /// - 高战斗价值殖民者优先评估，优先拾取地图上的高价值装备
    /// - 低价值殖民者后评估时，优质装备已被拾取，只能拿次优
    /// - 实现"高价值装备优先分配给高价值殖民者"的全局分配语义
    ///
    /// 战斗价值复用 SidearmAllocator.ComputeCombatValue：
    /// 射击等级 × 兴趣乘数 + 近战等级 × 兴趣乘数
    /// 兴趣乘数：无火 1.0，单火 1.5，双火 2.0
    ///
    /// 注意：本分配器不强制殖民者放下当前装备。
    /// 若需真正重配，玩家可先让殖民者放下装备再点击全局重配。
    /// </summary>
    public static class GlobalAllocator
    {
        // 候选缓存（手动触发，非 Tick 路径，但仍复用静态字段避免 GC）
        private static readonly List<Pawn> sortedPawns = new List<Pawn>();

        /// <summary>
        /// 全局重配：遍历所有地图的玩家殖民者，按战斗价值降序强制评估。
        /// 返回被触发的殖民者数量。
        /// </summary>
        public static int ReallocateAll()
        {
            sortedPawns.Clear();

            foreach (Map map in Find.Maps)
            {
                foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
                {
                    if (DLCCompat.IsGhoul(pawn)) continue;
                    if (!PawnSuitabilityChecker.CanManageGear(pawn)) continue;
                    if (pawn.Dead || pawn.Downed) continue;

                    CompGearManager comp = pawn.GetComp<CompGearManager>();
                    if (comp == null || comp.locked) continue;

                    sortedPawns.Add(pawn);
                }
            }

            if (sortedPawns.Count == 0) return 0;

            // 按战斗价值降序排序：高价值殖民者优先评估
            sortedPawns.Sort(ComparePawnByCombatValueDesc);

            int triggered = 0;
            for (int i = 0; i < sortedPawns.Count; i++)
            {
                Pawn pawn = sortedPawns[i];
                CompGearManager comp = pawn.GetComp<CompGearManager>();
                if (comp == null) continue;

                comp.ForceEvaluate(CompGearManager.ReloadTarget.All);
                triggered++;

                // 决策日志：玩家可见的全局重配顺序
                Log.Message($"[AutoEquipment] 全局重配 #{i + 1}: {pawn.LabelShort} (战斗价值={SidearmAllocator.ComputeCombatValue(pawn):F1})");
            }

            return triggered;
        }

        private static int ComparePawnByCombatValueDesc(Pawn a, Pawn b)
        {
            return SidearmAllocator.ComputeCombatValue(b).CompareTo(SidearmAllocator.ComputeCombatValue(a));
        }
    }
}
