using System.Collections.Generic;

namespace AutoEverything.RoleEvaluation
{
    /// <summary>
    /// 殖民者栏角色分组框的纯布局计算（不依赖 UnityEngine / 游戏运行时，便于单元测试）。
    ///
    /// 背景（RimWorld 1.6 反编译确认）：
    /// - 头像纹理高度 75，条目槽高度 48，纹理从槽位上方 27*scale 处开始绘制；
    /// - 原版分组框由 GroupFrameRect 计算：rect.y 恒为 0，四周再向外扩 12*scale，
    ///   因此可见顶边正好落在殖民者栏基线（屏幕 y=0），原版上边距等于 12*scale；
    /// - 角色分组启用后分组框必须显示，若沿用原版 21f 的槽位起始高度，
    ///   头像顶部会伸进顶部分组区域并与角色标签重叠。
    ///
    /// 解决思路：把所有绘制位置整体下移，在可见顶边下方预留一条
    /// "标签高度 + 间距"的头部带，保证头像纹理顶部不高于头部带底边。
    /// </summary>
    public static class RoleGroupLayout
    {
        /// <summary>
        /// "无角色"哨兵：空地图占位条目没有 pawn，不参与角色合并，各自独立成组。
        /// 合法角色序为 0..4，不会与该值冲突。
        /// </summary>
        public const int NoRoleOrder = int.MinValue;

        /// <summary>
        /// 计算每个条目重编后的 group id，返回组数（最大组 id + 1）。
        ///
        /// 规则与原生一致：原生 group 变化（跨地图/远行队）、角色序变化或空占位条目都会开新组。
        /// 返回的组数必须覆盖所有写入的 id —— 原版 CalculateColonistsInGroup 以 group 为
        /// 下标取数组，组数偏小会直接数组越界，进而每帧 OnGUI 抛异常（表现为屏幕闪烁）。
        /// </summary>
        /// <param name="origGroups">原生 group id（地图/远行队序号）</param>
        /// <param name="roleOrders">角色可视顺序；NoRoleOrder 表示空占位条目</param>
        /// <param name="assignedGroups">输出：每个条目重编后的 group id</param>
        public static int AssignGroupIds(IReadOnlyList<int> origGroups, IReadOnlyList<int> roleOrders,
            IList<int> assignedGroups)
        {
            int newGroup = -1;
            int prevOrigGroup = -1;
            int prevRole = NoRoleOrder;
            for (int i = 0; i < origGroups.Count; i++)
            {
                int origGroup = origGroups[i];
                int role = roleOrders[i];
                if (role == NoRoleOrder)
                {
                    // 空地图占位条目：保持独立一组（与原生行为一致）
                    newGroup++;
                }
                else if (origGroup != prevOrigGroup || role != prevRole)
                {
                    newGroup++;
                }
                prevOrigGroup = origGroup;
                prevRole = role;
                assignedGroups[i] = newGroup;
            }
            return newGroup + 1;
        }

        /// <summary>角色标签高度（Tiny 字体单行固定高度）。</summary>
        public const float TagHeight = 18f;

        /// <summary>标签与分组框可见顶边的间距。</summary>
        public const float TagMargin = 2f;

        /// <summary>标签底边与头像纹理顶部之间的安全间距。</summary>
        public const float TagGap = 2f;

        /// <summary>原版分组框向外扩展的上边距（BaseGroupFrameMargin）。</summary>
        public const float GroupFrameMargin = 12f;

        /// <summary>标签内角色图标边长。</summary>
        public const float IconSize = 16f;

        /// <summary>标签左右内边距。</summary>
        public const float TagPadding = 2f;

        /// <summary>图标与文字之间的间距。</summary>
        public const float IconTextGap = 2f;

        /// <summary>头部带总高度：标签订位 + 标签高度 + 安全间距。</summary>
        public static float HeaderHeight
        {
            get { return TagMargin + TagHeight + TagGap; }
        }

        /// <summary>
        /// 计算绘制位置需要整体下移的距离。
        /// portraitOverhang 为头像纹理超出条目槽顶部的距离（27*scale），
        /// minDrawLocY 为当前所有绘制位置中的最小 y（原版为 21f）。
        /// 结果不小于 0：布局已满足时不下移。
        /// </summary>
        public static float ComputeHeaderOffset(float minDrawLocY, float portraitOverhang)
        {
            float offset = HeaderHeight + portraitOverhang - minDrawLocY;
            return offset > 0f ? offset : 0f;
        }

        /// <summary>
        /// 分组框可见顶边：原版 rect.y 为 -12*scale（画到屏幕外），
        /// 加上上边距后即殖民者栏基线，绘制描边与命中判断都应从这里开始。
        /// </summary>
        public static float ComputeFrameVisibleTop(float frameY, float scale)
        {
            return frameY + GroupFrameMargin * scale;
        }

        /// <summary>标签顶边：位于可见顶边下方的 TagMargin 处。</summary>
        public static float ComputeTagY(float frameY, float scale)
        {
            return ComputeFrameVisibleTop(frameY, scale) + TagMargin;
        }

        /// <summary>
        /// 标签宽度：图标 + 文字 + 内边距为期望宽度，但不得超过分组框宽度。
        /// 分组框过窄时返回 0，由调用方跳过标签绘制。
        /// </summary>
        public static float ComputeTagWidth(float textWidth, float frameWidth)
        {
            float preferred = TagPadding * 2f + IconSize + IconTextGap + textWidth;
            float max = frameWidth - TagMargin * 2f;
            if (max <= 0f) return 0f;
            return preferred < max ? preferred : max;
        }

        /// <summary>
        /// 标签内是否有足够宽度同时容纳图标与文字；
        /// 过窄时只画文字，避免图标挤压导致文字被裁切。
        /// </summary>
        public static bool CanShowTagIcon(float tagWidth)
        {
            // 预留至少 2 个 Tiny 字宽（约 8px）给文字
            return tagWidth >= TagPadding * 2f + IconSize + IconTextGap + 8f;
        }
    }
}
