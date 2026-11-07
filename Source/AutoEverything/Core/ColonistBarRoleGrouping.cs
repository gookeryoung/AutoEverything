using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.Core
{
    /// <summary>
    /// 殖民者栏按角色分组：把殖民者栏头像按五类角色分组（前排/射手/工人/贸易/其他）。
    /// 分组判定阈值由【自动】→ 人员自动面板配置，见 RoleDetector.EvaluateRoleGroupCore。
    ///
    /// 原理（基于 RimWorld 1.6 反编译确认）：
    /// - ColonistBar.CheckRecacheEntries（私有）按地图/远行队构建 cachedEntries，
    ///   每个地图/远行队一个 Entry.group
    /// - ColonistBarOnGUI 中 Entry.group 变化时调用 drawer.DrawGroupFrame 画分组背景框，
    ///   且每个 group 是独立的 ReorderableWidget 组（组内可拖拽排序）
    /// - drawLocsFinder.CalculateDrawLocs（公开）按组数在组间插入 25f 空隙并自动缩放
    ///
    /// 实现方式：CheckRecacheEntries 重建条目后，把同地图内相邻的同角色条目重编为
    /// 连续的 group id，再重调 CalculateDrawLocs 重算绘制位置。
    /// 分组框绘制、组内拖拽、分组点击跳转均复用原生逻辑，零自绘。
    ///
    /// 性能设计：
    /// - Entries getter 每帧多次调用 CheckRecacheEntries，但仅在 entriesDirty 时真正重建；
    ///   Harmony Postfix 在 early-return 时也会执行，故用 pendingApply 脏标记消费一次，
    ///   避免 DetectRole（技能/特质查询较重）被每帧触发
    /// - MarkColonistsDirty Postfix 置脏，保证殖民者增减/手动重排后分组刷新
    /// </summary>
    internal static class ColonistBarRoleGrouping
    {
        /// <summary>
        /// 待应用分组标记：初始 true 保证游戏加载后首次 recache 即分组；
        /// 由 MarkColonistsDirty Postfix 置 true，CheckRecacheEntries Postfix 消费置 false。
        /// </summary>
        private static bool pendingApply = true;

        // ColonistBar 私有字段反射缓存（分组逻辑仅在 recache 时执行，反射开销可接受）
        private static readonly FieldInfo fiCachedEntries = AccessTools.Field(typeof(ColonistBar), "cachedEntries");
        private static readonly FieldInfo fiCachedDrawLocs = AccessTools.Field(typeof(ColonistBar), "cachedDrawLocs");
        private static readonly FieldInfo fiCachedScale = AccessTools.Field(typeof(ColonistBar), "cachedScale");
        private static readonly FieldInfo fiDrawLocsFinder = AccessTools.Field(typeof(ColonistBar), "drawLocsFinder");

        /// <summary>
        /// MarkColonistsDirty 的 Postfix 入口：条目变更后需要重新应用角色分组。
        /// 无条件置脏——若分组设置已关闭，Apply 前置检查会直接跳过，
        /// 原生重建的 group 保留，殖民者栏自然恢复原生按地图/远行队分组。
        /// </summary>
        public static void NotifyDirty()
        {
            pendingApply = true;
        }

        /// <summary>
        /// CheckRecacheEntries 的 Postfix 入口：仅在有待应用标记时执行一次分组。
        /// early-return（非 dirty）的频繁调用在此短路，DetectRole 不会进入每帧路径。
        /// </summary>
        public static void TryApply(ColonistBar __instance)
        {
            if (!pendingApply) return;
            pendingApply = false;
            if (!AESettings.enabled || !AESettings.groupColonistBarByRole) return;

            try
            {
                Apply(__instance);
            }
            catch (System.Exception ex)
            {
                // 分组失败降级为原生分组，不阻断殖民者栏渲染
                Log.ErrorOnce("[AutoEverything] 殖民者栏角色分组失败: " + ex.Message, 0xA900);
            }
        }

        /// <summary>
        /// 按分组重编 cachedEntries 的 group 并重算绘制位置。
        /// 分组键 = (原生 group, 五类角色序)：原生 group 保留地图/远行队边界，
        /// 同一地图/远行队内相邻的同分组条目合为一个新组。
        /// 注：同分组条目是否相邻由 displayOrder 排序决定（建议配合"按角色+评级"排序）。
        /// </summary>
        private static void Apply(ColonistBar bar)
        {
            var entries = (List<ColonistBar.Entry>)fiCachedEntries.GetValue(bar);
            if (entries == null || entries.Count == 0) return;

            int newGroup = -1;
            int prevOrigGroup = -1;
            int prevRole = int.MinValue;
            for (int i = 0; i < entries.Count; i++)
            {
                ColonistBar.Entry entry = entries[i];
                if (entry.pawn != null)
                {
                    int role = RoleDetector.GetRoleGroupOrder(RoleDetector.DetectRoleGroup(entry.pawn));
                    if (entry.group != prevOrigGroup || role != prevRole) newGroup++;
                    prevOrigGroup = entry.group;
                    prevRole = role;
                }
                else
                {
                    // 空地图占位条目：保持独立一组（与原生行为一致）
                    newGroup++;
                    prevOrigGroup = entry.group;
                    prevRole = int.MinValue;
                }
                entry.group = newGroup;
                entries[i] = entry;  // Entry 是 struct，需写回
            }

            // 重算绘制位置：CalculateDrawLocs 内部读 Find.ColonistBar.Entries（此时 dirty 已
            // 复位，直接返回改好 group 的 cachedEntries），并按新组数插入组间空隙与缩放
            var finder = (ColonistBarDrawLocsFinder)fiDrawLocsFinder.GetValue(bar);
            var drawLocs = (List<Vector2>)fiCachedDrawLocs.GetValue(bar);
            finder.CalculateDrawLocs(drawLocs, out float scale, newGroup + 1);
            fiCachedScale.SetValue(bar, scale);
        }
    }
}
