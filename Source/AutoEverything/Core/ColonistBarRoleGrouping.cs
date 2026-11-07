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
    [StaticConstructorOnStartup]
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

        // 分组框绘制：GroupFrameRect 为私有方法，仅绘制期调用（每帧每组一次，反射开销可接受）
        private static readonly MethodInfo miGroupFrameRect =
            AccessTools.Method(typeof(ColonistBarColonistDrawer), "GroupFrameRect");

        // group id → 五类角色；由 Apply 每次重编分组时重建，供绘制标签使用
        private static readonly Dictionary<int, RoleGroup> groupRoles = new Dictionary<int, RoleGroup>();

        // 右键拖拽状态：-1 表示未拖拽；拖动分组框标签/边框开始，释放到目标分组完成换序
        private static int draggingFromGroup = -1;
        private static RoleGroup draggingRole;

        // 角色图标：延迟到资源加载完成后获取（避免 ContentFinder 在静态初始化早期调用）
        private static Dictionary<RoleGroup, Texture2D> roleIcons;

        static ColonistBarRoleGrouping()
        {
            LongEventHandler.ExecuteWhenFinished(LoadRoleIcons);
        }

        private static void LoadRoleIcons()
        {
            roleIcons = new Dictionary<RoleGroup, Texture2D>();
            roleIcons[RoleGroup.Front] = ContentFinder<Texture2D>.Get("UI/Icons/Role/Role_Frontline", false);
            roleIcons[RoleGroup.Shooter] = ContentFinder<Texture2D>.Get("UI/Icons/Role/Role_Ranged", false);
            roleIcons[RoleGroup.Worker] = ContentFinder<Texture2D>.Get("UI/Icons/Role/Role_Worker", false);
            roleIcons[RoleGroup.Trade] = ContentFinder<Texture2D>.Get("UI/Icons/Role/Role_Trader", false);
            roleIcons[RoleGroup.Other] = ContentFinder<Texture2D>.Get("UI/Icons/Role/Role_Default", false);
        }

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
            if (!AESettings.enabled || !AESettings.groupColonistBarByRole)
            {
                // 关闭时清空映射与拖拽状态，避免残留 group id 命中错误标签
                groupRoles.Clear();
                draggingFromGroup = -1;
                return;
            }

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

            groupRoles.Clear();

            int newGroup = -1;
            int prevOrigGroup = -1;
            int prevRole = int.MinValue;
            for (int i = 0; i < entries.Count; i++)
            {
                ColonistBar.Entry entry = entries[i];
                if (entry.pawn != null)
                {
                    RoleGroup group = RoleDetector.DetectRoleGroup(entry.pawn);
                    // 可视顺序：使用玩家右键拖拽后的自定义顺序，重新分组时保持该顺序
                    int role = AESettings.GetRoleGroupOrder(group);
                    if (entry.group != prevOrigGroup || role != prevRole) newGroup++;
                    prevOrigGroup = entry.group;
                    prevRole = role;
                    groupRoles[newGroup] = group;
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

        /// <summary>
        /// DrawGroupFrame 的 Postfix：在原生分组框上叠加"图标 + 文字"标签与彩色边界，
        /// 并处理右键拖拽换序。分组框 id 来自 Apply 的 groupRoles 映射，未映射（空地图占位）不绘制。
        /// 坐标说明：GroupFrameRect 与 Event.mousePosition 均处于同一 GUI Group 内，可直接命中测试。
        /// </summary>
        public static void DrawGroupDecorations(ColonistBarColonistDrawer __instance, int group)
        {
            if (!AESettings.enabled || !AESettings.groupColonistBarByRole) return;

            RoleGroup role;
            if (!groupRoles.TryGetValue(group, out role)) return;

            Rect rect;
            try
            {
                rect = (Rect)miGroupFrameRect.Invoke(__instance, new object[] { group });
            }
            catch (System.Exception ex)
            {
                // 反射失败降级为原生分组框（无标签），不阻断殖民者栏
                Log.ErrorOnce("[AutoEverything] 分组框矩形获取失败: " + ex.Message, 0xA901);
                return;
            }
            if (rect.width <= 0f || rect.height <= 0f) return;

            DrawBorder(rect, role);
            Rect tagRect = DrawTag(rect, role);
            HandleGroupInput(rect, tagRect, group, role);

            // 拖拽跟随光标的小标签：只在被拖拽的组上绘制一次，避免重复叠加
            if (draggingFromGroup == group) DrawDragGhost(role);
        }

        /// <summary>
        /// ColonistBarOnGUI 的 Postfix：右键在分组框外释放时清理拖拽状态，
        /// 避免拖拽标记残留导致后续右键误判。
        /// </summary>
        public static void EndGroupDragIfReleased()
        {
            if (draggingFromGroup >= 0 && Event.current != null && Event.current.type == EventType.MouseUp)
            {
                draggingFromGroup = -1;
            }
        }

        private static void DrawBorder(Rect rect, RoleGroup role)
        {
            Color prev = GUI.color;
            GUI.color = GetRoleColor(role);
            Widgets.DrawBox(rect, 2);

            // 拖拽悬停到本分组：白色高亮表示释放目标
            if (draggingFromGroup >= 0 && Event.current != null
                && Event.current.type != EventType.MouseUp
                && rect.Contains(Event.current.mousePosition))
            {
                GUI.color = Color.white;
                Widgets.DrawBox(rect, 1);
            }
            GUI.color = prev;
        }

        /// <summary>
        /// 在分组框左上角绘制"图标 + 文字"标签，返回标签矩形（作为右键拖拽把手）。
        /// 关闭换行并按 CalcSize 计算宽度，避免中文标签换行撑乱布局。
        /// </summary>
        private static Rect DrawTag(Rect frameRect, RoleGroup role)
        {
            string label = GetRoleLabel(role);

            GameFont prevFont = Text.Font;
            bool prevWrap = Text.WordWrap;
            TextAnchor prevAnchor = Text.Anchor;

            Text.Font = GameFont.Tiny;
            Vector2 textSize = Text.CalcSize(label);
            float width = Mathf.Min(22f + textSize.x + 6f, frameRect.width - 4f);
            Rect tag = new Rect(frameRect.x + 2f, frameRect.y + 2f, width, 18f);

            Widgets.DrawBoxSolid(tag, new Color(0f, 0f, 0f, 0.55f));

            Texture2D icon = null;
            if (roleIcons != null) roleIcons.TryGetValue(role, out icon);
            if (icon != null)
            {
                GUI.DrawTexture(new Rect(tag.x + 2f, tag.y + 1f, 16f, 16f), icon, ScaleMode.ScaleToFit);
            }

            Text.WordWrap = false;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(tag.x + 20f, tag.y, width - 22f, 18f), label);

            Text.Anchor = prevAnchor;
            Text.WordWrap = prevWrap;
            Text.Font = prevFont;
            return tag;
        }

        private static void DrawDragGhost(RoleGroup role)
        {
            Event e = Event.current;
            if (e == null) return;

            Rect ghost = new Rect(e.mousePosition.x + 12f, e.mousePosition.y + 12f, 90f, 20f);
            Widgets.DrawBoxSolid(ghost, new Color(0f, 0f, 0f, 0.7f));
            GUI.color = GetRoleColor(role);
            Widgets.DrawBox(ghost, 1);
            GUI.color = Color.white;

            bool prevWrap = Text.WordWrap;
            Text.WordWrap = false;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(ghost, GetRoleLabel(role));
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = prevWrap;
        }

        /// <summary>
        /// 右键拖拽换序：
        /// - MouseDown 命中"标签或分组框边缘 5px 边框带"才开始拖拽，避免抢走原生右键（如右键殖民者头像）
        /// - MouseUp 落在另一分组框内 → 按该位置重排分组顺序并写盘
        /// </summary>
        private static void HandleGroupInput(Rect frameRect, Rect tagRect, int group, RoleGroup role)
        {
            Event e = Event.current;
            if (e == null) return;

            if (e.type == EventType.MouseDown && e.button == 1 && IsDragHandle(frameRect, tagRect, e.mousePosition))
            {
                draggingFromGroup = group;
                draggingRole = role;
                e.Use();
                return;
            }

            if (draggingFromGroup < 0) return;

            if (e.type == EventType.MouseDrag)
            {
                // 消费事件，避免右键拖拽在殖民者栏触发其他行为
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 1)
            {
                if (frameRect.Contains(e.mousePosition) && role != draggingRole)
                {
                    AESettings.MoveRoleGroup(draggingRole, role);
                }
                draggingFromGroup = -1;
                e.Use();
            }
        }

        /// <summary>
        /// 拖拽把手：标签区域，或分组框四周 5px 宽的边框带（头像区域留给原生右键菜单）。
        /// </summary>
        private static bool IsDragHandle(Rect frameRect, Rect tagRect, Vector2 mouse)
        {
            if (tagRect.Contains(mouse)) return true;
            if (!frameRect.Contains(mouse)) return false;

            const float Band = 5f;
            return mouse.x <= frameRect.x + Band
                || mouse.x >= frameRect.xMax - Band
                || mouse.y <= frameRect.y + Band
                || mouse.y >= frameRect.yMax - Band;
        }

        private static string GetRoleLabel(RoleGroup role)
        {
            switch (role)
            {
                case RoleGroup.Front: return "AE_RoleGroup_Front".Translate();
                case RoleGroup.Shooter: return "AE_RoleGroup_Shooter".Translate();
                case RoleGroup.Worker: return "AE_RoleGroup_Worker".Translate();
                case RoleGroup.Trade: return "AE_RoleGroup_Trade".Translate();
                default: return "AE_RoleGroup_Other".Translate();
            }
        }

        private static Color GetRoleColor(RoleGroup role)
        {
            switch (role)
            {
                case RoleGroup.Front: return new Color(0.85f, 0.30f, 0.25f, 0.9f);
                case RoleGroup.Shooter: return new Color(0.90f, 0.70f, 0.20f, 0.9f);
                case RoleGroup.Worker: return new Color(0.35f, 0.70f, 0.40f, 0.9f);
                case RoleGroup.Trade: return new Color(0.65f, 0.50f, 0.85f, 0.9f);
                default: return new Color(0.60f, 0.60f, 0.60f, 0.9f);
            }
        }
    }
}
