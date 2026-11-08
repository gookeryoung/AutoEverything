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
        private static readonly FieldInfo fiEntriesDirty = AccessTools.Field(typeof(ColonistBar), "entriesDirty");

        /// <summary>
        /// CheckRecacheEntries 调用深度。原生 recache 结束时会调用 CalculateDrawLocs，
        /// 而 CalculateDrawLocs 内部会再读 Entries；若此时缓存已置脏，就会嵌套触发一次 recache。
        /// 嵌套层重编 group 会让外层仍在使用的 groupsCount 与新的 group id 不匹配，
        /// 原生 CalculateColonistsInGroup 随即数组越界 → 每帧 OnGUI 异常 → 屏幕闪烁。
        /// 因此只在最外层（深度 1）结束后应用分组。
        /// </summary>
        private static int recacheDepth;

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

        /// <summary>CheckRecacheEntries 的 Prefix：记录调用深度，供 Postfix 判断是否为最外层。</summary>
        public static void OnRecachePrefix()
        {
            recacheDepth++;
        }

        /// <summary>CheckRecacheEntries 的 Finalizer：无论正常返回还是抛异常都复位深度，避免深度泄漏。</summary>
        public static void OnRecacheFinalizer()
        {
            if (recacheDepth > 0) recacheDepth--;
        }

        /// <summary>
        /// CheckRecacheEntries 的 Postfix 入口：仅在有待应用标记时执行一次分组。
        /// early-return（非 dirty）的频繁调用在此短路，DetectRole 不会进入每帧路径。
        /// 嵌套 recache 一律跳过，由最外层统一应用（见 recacheDepth 说明）。
        /// </summary>
        public static void TryApply(ColonistBar __instance)
        {
            if (recacheDepth > 1) return;
            if (!pendingApply) return;
            if (!AESettings.enabled || !AESettings.groupColonistBarByRole)
            {
                // 关闭时清空映射与拖拽状态，避免残留 group id 命中错误标签
                pendingApply = false;
                groupRoles.Clear();
                draggingFromGroup = -1;
                return;
            }

            // 本轮重建过程中又被置脏（如设置 displayOrder 会调用 MarkColonistsDirty）：
            // 保留标记，等紧接着的那次 recache 结束后再应用，避免与外层计算交错
            if (fiEntriesDirty != null && (bool)fiEntriesDirty.GetValue(__instance)) return;

            pendingApply = false;
            try
            {
                Apply(__instance);
            }
            catch (System.Exception ex)
            {
                // 分组失败降级为原生分组，不阻断殖民者栏渲染；下轮 recache 重试
                pendingApply = true;
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

            int count = entries.Count;
            var origGroups = new int[count];
            var roleOrders = new int[count];
            var roles = new RoleGroup[count];
            for (int i = 0; i < count; i++)
            {
                ColonistBar.Entry entry = entries[i];
                origGroups[i] = entry.group;
                if (entry.pawn != null)
                {
                    roles[i] = RoleDetector.DetectRoleGroup(entry.pawn);
                    // 可视顺序：使用玩家右键拖拽后的自定义顺序，重新分组时保持该顺序
                    roleOrders[i] = AESettings.GetRoleGroupOrder(roles[i]);
                }
                else
                {
                    // 空地图占位条目：int.MinValue 表示独立开组（与原生行为一致）
                    roleOrders[i] = RoleGroupLayout.NoRoleOrder;
                }
            }

            // 自愈：新殖民者加入时 displayOrder 为默认值 0，会被插到地图块最前面，
            // 同一角色被切成两段 → 殖民者栏出现两个同名分组框（如两个"射手"）。
            // 这里先按角色重写 displayOrder 并置脏，下一次 recache 重建出连续条目后再分组；
            // 本次返回可避免用尚未重排的条目算出错误分组。
            // 注意：玩家在组内拖拽不会触发（同组拖拽始终连续），不会覆盖玩家的组内顺序。
            if (RoleGroupLayout.RequiresRoleReorder(origGroups, roleOrders))
            {
                var reordered = new int[count];
                RoleGroupLayout.ComputeRoleStableOrder(origGroups, roleOrders, reordered);
                bool changed = false;
                for (int i = 0; i < count; i++)
                {
                    if (reordered[i] < 0) continue;
                    Pawn pawn = entries[i].pawn;
                    if (pawn?.playerSettings == null) continue;
                    if (pawn.playerSettings.displayOrder == reordered[i]) continue;
                    pawn.playerSettings.displayOrder = reordered[i];
                    changed = true;
                }

                if (changed)
                {
                    // 清空映射避免这一帧用旧 group id 画错标签
                    groupRoles.Clear();
                    pendingApply = true;
                    bar.MarkColonistsDirty();
                    return;
                }
            }

            var newGroups = new int[count];
            int groupsCount = RoleGroupLayout.AssignGroupIds(origGroups, roleOrders, newGroups);

            int maxGroup = 0;
            for (int i = 0; i < count; i++)
            {
                ColonistBar.Entry entry = entries[i];
                int newGroupId = newGroups[i];
                // 必须用构造函数重建 Entry，而不是只改 group 字段：
                // 原生 Entry 在构造时用构造参数 group 捕获 reorderAction/
                // extraDraggedItemOnGUI 闭包（Find.ColonistBar.Reorder(from, to, group)），
                // 只改字段会让拖拽换序仍按"旧地图组 id"查找条目——旧 id 恰好命中
                // 另一个角色分组（如 0=前排），表现为"拖动射手却移动了前排"。
                entries[i] = new ColonistBar.Entry(entry.pawn, entry.map, newGroupId);
                if (newGroupId > maxGroup) maxGroup = newGroupId;
                if (roleOrders[i] != RoleGroupLayout.NoRoleOrder) groupRoles[newGroupId] = roles[i];
            }

            // 防御：groupsCount 必须覆盖所有已写入的 group id，否则原生
            // CalculateColonistsInGroup 以 group 为下标取数组会越界（曾导致每帧 OnGUI 异常闪烁）
            if (groupsCount < maxGroup + 1) groupsCount = maxGroup + 1;

            // 分组期间清掉脏标记，避免 CalculateDrawLocs → Entries 嵌套触发 recache，
            // 用未分组的条目覆盖刚写入的 group id（外层 groupsCount 随即失配）
            if (fiEntriesDirty != null) fiEntriesDirty.SetValue(bar, false);

            // 重算绘制位置：CalculateDrawLocs 内部读 Find.ColonistBar.Entries（此时 dirty 已
            // 复位，直接返回改好 group 的 cachedEntries），并按新组数插入组间空隙与缩放
            var finder = (ColonistBarDrawLocsFinder)fiDrawLocsFinder.GetValue(bar);
            var drawLocs = (List<Vector2>)fiCachedDrawLocs.GetValue(bar);
            finder.CalculateDrawLocs(drawLocs, out float scale, groupsCount);
            fiCachedScale.SetValue(bar, scale);

            ApplyHeaderOffset(drawLocs, scale);
        }

        /// <summary>
        /// 分组框可见顶边位于殖民者栏基线，原版槽位起始高度（21f）只够原生的
        /// 隐藏分组框使用；头像纹理高出槽位 27*scale，因此头像会伸进顶部分组区域。
        /// 这里把所有绘制位置整体下移，预留出"角色标签 + 间距"的头部带。
        /// </summary>
        private static void ApplyHeaderOffset(List<Vector2> drawLocs, float scale)
        {
            if (drawLocs == null || drawLocs.Count == 0) return;

            float minY = float.MaxValue;
            for (int i = 0; i < drawLocs.Count; i++)
            {
                if (drawLocs[i].y < minY) minY = drawLocs[i].y;
            }

            // 头像纹理比条目槽高出的部分：PawnTextureSize.y - BaseSize.y（原版 75 - 48）
            float overhang = (ColonistBarColonistDrawer.PawnTextureSize.y - ColonistBar.BaseSize.y) * scale;
            float offset = RoleGroupLayout.ComputeHeaderOffset(minY, overhang);
            if (offset <= 0f) return;

            for (int i = 0; i < drawLocs.Count; i++)
            {
                Vector2 loc = drawLocs[i];
                loc.y += offset;
                drawLocs[i] = loc;
            }
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

            // 原版分组框向上扩到屏幕外，可见顶边在栏基线；描边与命中都用可见框
            ColonistBar bar = Find.ColonistBar;
            if (bar == null) return;
            float scale = bar.Scale;
            Rect visibleRect = ClipToVisibleTop(rect, scale);
            if (visibleRect.width <= 0f || visibleRect.height <= 0f) return;

            // 名字高度是固定像素、不随殖民者栏缩放；原版 12*scale 的下边距不够时
            // 下探分组框底边，避免名字穿过下边框
            float labelHeight = Prefs.DisableTinyText
                ? RoleGroupLayout.NameLabelHeightLarge
                : RoleGroupLayout.NameLabelHeight;
            visibleRect.yMax += RoleGroupLayout.ComputeFrameBottomExtend(scale, labelHeight);

            DrawBorder(visibleRect, role, group);
            DrawTag(rect, scale, role);

            // 头部整条标签带都可作为拖拽把手：悬停时提示右键拖动，降低发现成本
            Rect headerRect = new Rect(visibleRect.x, visibleRect.y, visibleRect.width,
                RoleGroupLayout.HeaderHeight);
            if (Mouse.IsOver(headerRect))
                TooltipHandler.TipRegion(headerRect, "AE_TT_DragGroupOrder".Translate());

            // 拖拽跟随光标的小标签：只在被拖拽的组上绘制一次，避免重复叠加
            if (draggingFromGroup == group) DrawDragGhost(role);
        }

        /// <summary>
        /// ColonistBarOnGUI 的 Postfix：处理分组框右键拖拽换序。
        ///
        /// 必须放在这里而不是 DrawGroupFrame Postfix：原版仅在 EventType.repaint
        /// 时调用 DrawGroupFrame，MouseDown/MouseDrag/MouseUp 根本不会进入该方法
        /// （这是此前右键拖拽完全无响应的根因）。ColonistBarOnGUI 对所有事件类型
        /// 都会执行，且此时 cachedEntries/cachedDrawLocs 已由本帧循环刷新。
        /// </summary>
        public static void HandleGroupInputOnGui(ColonistBar __instance)
        {
            Event e = Event.current;
            if (e == null) return;
            // Layout/Repaint 不处理输入；MouseDown 等事件才是拖拽入口
            if (e.type == EventType.Layout || e.type == EventType.Repaint) return;

            if (!AESettings.enabled || !AESettings.groupColonistBarByRole)
            {
                draggingFromGroup = -1;
                return;
            }

            var entries = (List<ColonistBar.Entry>)fiCachedEntries.GetValue(__instance);
            if (entries == null || entries.Count == 0)
            {
                draggingFromGroup = -1;
                return;
            }

            // 新的右键按下视为新一轮拖拽，先清理可能残留的状态
            if (e.type == EventType.MouseDown)
            {
                draggingFromGroup = -1;
                if (e.button != 1) return;

                int group;
                RoleGroup role;
                if (!TryFindGroupAt(__instance, entries, e.mousePosition, true, out group, out role)) return;
                // "其他"固定最后，不允许拖动
                if (role == RoleGroup.Other) return;

                draggingFromGroup = group;
                draggingRole = role;
                e.Use();
                return;
            }

            if (draggingFromGroup < 0) return;

            // 拖拽期间消费事件，避免右键拖拽在殖民者栏触发其他行为
            if (e.type == EventType.MouseDrag)
            {
                e.Use();
                return;
            }

            if (e.type == EventType.MouseUp && e.button == 1)
            {
                int group;
                RoleGroup role;
                if (TryFindGroupAt(__instance, entries, e.mousePosition, false, out group, out role)
                    && role != draggingRole)
                {
                    AESettings.MoveRoleGroup(draggingRole, role);
                }
                draggingFromGroup = -1;
                e.Use();
            }
        }

        /// <summary>
        /// 查找鼠标位置命中的角色分组。
        /// handleOnly=true 用于按下判定，只认标签或 5px 边框带（头像区域保留原生右键菜单）；
        /// handleOnly=false 用于释放判定，分组框内部任意位置都算目标。
        /// </summary>
        private static bool TryFindGroupAt(ColonistBar bar, List<ColonistBar.Entry> entries,
            Vector2 mouse, bool handleOnly, out int group, out RoleGroup role)
        {
            group = -1;
            role = RoleGroup.Other;

            ColonistBarColonistDrawer drawer = bar.drawer;
            float scale = bar.Scale;
            int prevGroup = int.MinValue;

            for (int i = 0; i < entries.Count; i++)
            {
                int g = entries[i].group;
                if (g == prevGroup) continue;
                prevGroup = g;

                RoleGroup r;
                if (!groupRoles.TryGetValue(g, out r)) continue;

                Rect rawRect, visibleRect;
                if (!TryGetFrameRects(bar, drawer, g, out rawRect, out visibleRect)) continue;

                if (handleOnly)
                {
                    if (!IsDragHandle(visibleRect, ComputeTagRect(rawRect, scale, r), mouse)) continue;
                }
                else if (!visibleRect.Contains(mouse))
                {
                    continue;
                }

                group = g;
                role = r;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 计算分组框的原始矩形与可见矩形（含向下扩展的名字避让）。
        /// GroupFrameRect 为私有方法，仅在命中判定/绘制时调用，反射开销可接受。
        /// </summary>
        private static bool TryGetFrameRects(ColonistBar bar, ColonistBarColonistDrawer drawer,
            int group, out Rect rawRect, out Rect visibleRect)
        {
            rawRect = Rect.zero;
            visibleRect = Rect.zero;

            try
            {
                rawRect = (Rect)miGroupFrameRect.Invoke(drawer, new object[] { group });
            }
            catch (System.Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] 分组框矩形获取失败: " + ex.Message, 0xA901);
                return false;
            }
            if (rawRect.width <= 0f || rawRect.height <= 0f) return false;

            float scale = bar.Scale;
            visibleRect = ClipToVisibleTop(rawRect, scale);
            if (visibleRect.width <= 0f || visibleRect.height <= 0f) return false;

            float labelHeight = Prefs.DisableTinyText
                ? RoleGroupLayout.NameLabelHeightLarge
                : RoleGroupLayout.NameLabelHeight;
            visibleRect.yMax += RoleGroupLayout.ComputeFrameBottomExtend(scale, labelHeight);
            return true;
        }

        /// <summary>
        /// 把分组框裁剪到可见区域：保留原版 rect 的左右与底边，顶边取可见顶边。
        /// 顶边已在屏幕内时原样返回。
        /// </summary>
        private static Rect ClipToVisibleTop(Rect frameRect, float scale)
        {
            float visibleTop = RoleGroupLayout.ComputeFrameVisibleTop(frameRect.y, scale);
            if (visibleTop <= frameRect.y) return frameRect;

            return new Rect(frameRect.x, visibleTop, frameRect.width, frameRect.yMax - visibleTop);
        }

        private static void DrawBorder(Rect rect, RoleGroup role, int group)
        {
            Color prev = GUI.color;
            GUI.color = GetRoleColor(role);
            Widgets.DrawBox(rect, 2);

            // 拖拽悬停到本分组：白色高亮表示释放目标
            if (draggingFromGroup >= 0 && draggingFromGroup != group && Event.current != null
                && Event.current.type != EventType.MouseUp
                && rect.Contains(Event.current.mousePosition))
            {
                GUI.color = Color.white;
                Widgets.DrawBox(rect, 1);

                // 插入位置指示线：线在目标分组左边缘，表示"插入到该分组之前"
                Widgets.DrawBoxSolid(new Rect(rect.x - 1f, rect.y, 3f, rect.height), Color.white);
            }
            GUI.color = prev;
        }

        /// <summary>
        /// 在分组框头部带绘制"图标 + 文字"标签。
        /// 位置取可见顶边下方，尺寸按分组框实际宽度收缩；关闭换行避免中文撑乱布局。
        /// </summary>
        private static void DrawTag(Rect frameRect, float scale, RoleGroup role)
        {
            Rect tag = ComputeTagRect(frameRect, scale, role);
            if (tag.width <= 0f) return;

            string label = GetRoleLabel(role);
            Widgets.DrawBoxSolid(tag, new Color(0f, 0f, 0f, 0.55f));

            Texture2D icon = null;
            if (roleIcons != null) roleIcons.TryGetValue(role, out icon);

            float textX;
            float textWidth;
            if (icon != null && RoleGroupLayout.CanShowTagIcon(tag.width))
            {
                GUI.DrawTexture(
                    new Rect(tag.x + RoleGroupLayout.TagPadding,
                        tag.y + (RoleGroupLayout.TagHeight - RoleGroupLayout.IconSize) * 0.5f,
                        RoleGroupLayout.IconSize, RoleGroupLayout.IconSize),
                    icon, ScaleMode.ScaleToFit);
                textX = tag.x + RoleGroupLayout.TagPadding + RoleGroupLayout.IconSize + RoleGroupLayout.IconTextGap;
                textWidth = tag.xMax - RoleGroupLayout.TagPadding - textX;
            }
            else
            {
                // 分组过窄时只保留文字，避免图标把文字挤没
                textX = tag.x + RoleGroupLayout.TagPadding;
                textWidth = tag.width - RoleGroupLayout.TagPadding * 2f;
            }

            if (textWidth > 0f)
            {
                GameFont prevFont = Text.Font;
                bool prevWrap = Text.WordWrap;
                TextAnchor prevAnchor = Text.Anchor;

                Text.Font = GameFont.Tiny;
                Text.WordWrap = false;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = Color.white;
                Widgets.Label(new Rect(textX, tag.y, textWidth, RoleGroupLayout.TagHeight), label);

                Text.Anchor = prevAnchor;
                Text.WordWrap = prevWrap;
                Text.Font = prevFont;
            }
        }

        /// <summary>
        /// 计算标签矩形（绘制与命中判定共用同一套尺寸算法，避免两处不一致）。
        /// frameRect 必须是未裁剪的原始分组框矩形，否则标签定位与绘制会偏移。
        /// </summary>
        private static Rect ComputeTagRect(Rect frameRect, float scale, RoleGroup role)
        {
            GameFont prevFont = Text.Font;
            Text.Font = GameFont.Tiny;
            Vector2 textSize = Text.CalcSize(GetRoleLabel(role));
            Text.Font = prevFont;

            float width = RoleGroupLayout.ComputeTagWidth(textSize.x, frameRect.width);
            if (width <= 0f) return Rect.zero;

            return new Rect(
                frameRect.x + RoleGroupLayout.TagMargin,
                RoleGroupLayout.ComputeTagY(frameRect.y, scale),
                width,
                RoleGroupLayout.TagHeight);
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
        /// 拖拽把手：整个头部标签带（含标签上下留白，横向覆盖整框宽度），
        /// 或分组框四周 5px 宽的边框带（头像区域留给原生右键菜单）。
        /// 头部带按 HeaderHeight 计算，悬停提示与绘制使用同一高度，命中范围与视觉一致。
        /// </summary>
        private static bool IsDragHandle(Rect frameRect, Rect tagRect, Vector2 mouse)
        {
            if (!frameRect.Contains(mouse)) return false;

            // 头部整条标签带：y 从可见顶边到 HeaderHeight 下沿
            if (mouse.y <= frameRect.y + RoleGroupLayout.HeaderHeight) return true;
            if (tagRect.Contains(mouse)) return true;

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
