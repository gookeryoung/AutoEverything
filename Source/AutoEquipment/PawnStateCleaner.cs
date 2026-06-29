using System.Collections.Generic;
using RimWorld;
using Verse;

namespace AutoEquipment
{
    /// <summary>
    /// 字典状态清理工具：统一处理按 Pawn ID 索引的字典中已死亡/离开的 Pawn 残留条目。
    /// 抽取自 RoleDetector 与 ContextDetector 的重复实现，保证清理逻辑一致，
    /// 避免thingIDNumber 复用导致的误判窗口与字典无限增长。
    /// </summary>
    public static class PawnStateCleaner
    {
        // 复用静态集合避免每次清理分配（非 Tick 热路径，但保持习惯）
        private static readonly HashSet<int> alivePawnIds = new HashSet<int>();
        private static readonly List<int> keysToRemove = new List<int>();

        /// <summary>
        /// 清理单个字典中已死亡/离开地图的 Pawn 残留条目。
        /// </summary>
        public static void Cleanup<TValue>(Dictionary<int, TValue> dict)
        {
            RefreshAlivePawns();
            RemoveDeadKeys(dict);
        }

        /// <summary>
        /// 清理多个字典中已死亡/离开地图的 Pawn 残留条目。
        /// 共享一次存活 Pawn 收集，避免多次遍历地图。
        /// </summary>
        public static void CleanupAll<T1, T2, T3>(
            Dictionary<int, T1> a,
            Dictionary<int, T2> b,
            Dictionary<int, T3> c)
        {
            RefreshAlivePawns();
            RemoveDeadKeys(a);
            RemoveDeadKeys(b);
            RemoveDeadKeys(c);
        }

        /// <summary>
        /// 校验指定 Pawn ID 是否仍存活（在当前地图的 FreeColonistsSpawned 中）。
        /// 仅在 Cleanup/CleanupAll 调用后有效——基于上次清理时收集的存活集合。
        /// 用于防御 thingIDNumber 复用：若字典中记录的 ID 已不在存活集合，视为旧记录。
        /// </summary>
        public static bool IsAlive(int pawnId)
        {
            return alivePawnIds.Contains(pawnId);
        }

        private static void RefreshAlivePawns()
        {
            alivePawnIds.Clear();
            foreach (Map map in Find.Maps)
            {
                foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
                {
                    alivePawnIds.Add(pawn.thingIDNumber);
                }
            }
        }

        private static void RemoveDeadKeys<TValue>(Dictionary<int, TValue> dict)
        {
            keysToRemove.Clear();
            foreach (var kvp in dict)
            {
                if (!alivePawnIds.Contains(kvp.Key))
                    keysToRemove.Add(kvp.Key);
            }
            for (int i = 0; i < keysToRemove.Count; i++)
            {
                dict.Remove(keysToRemove[i]);
            }
        }
    }
}
