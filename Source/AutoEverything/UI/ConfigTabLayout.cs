namespace AutoEverything.UI
{
    /// <summary>
    /// 【自动】配置窗口滚动视图的纯布局计算（不依赖 UnityEngine / 游戏运行时，便于单元测试）。
    ///
    /// 背景（RimWorld 1.6 反编译确认）：Listing.GetRect 在内容高度超出 Begin 传入的矩形时
    /// 会调用 NewColumnIfNeeded 自动"新开一列"（curX 右移、curY 归零）。
    /// 而滚动视图只显示第一列宽度，第二列会被画到可见区域之外——
    /// 表现为"人员自动页切到其他标签再切回来后，前排/射手/工人/贸易选项消失"。
    /// 且分列后 CurHeight 只返回最后一列高度，高度缓存随之变小，形成"越画越少"的震荡。
    ///
    /// 因此：单列绘制必须靠 maxOneColumn 关闭自动分列，内容高度只用于滚动范围。
    /// </summary>
    public static class ConfigTabLayout
    {
        /// <summary>内容底部留白：避免最后一行紧贴滚动视图底边。</summary>
        public const float BottomPadding = 40f;

        /// <summary>高度缓存初值：打开窗口/切换标签的首帧使用，随后由实际绘制高度取代。</summary>
        public const float DefaultContentHeight = 620f;

        /// <summary>
        /// 计算滚动视图的内容高度。
        /// 取"单列累计高度 + 底部留白"，并保证不小于视口高度——
        /// 内容比视口矮时不应出现无意义的滚动条范围。
        /// </summary>
        public static float ComputeContentHeight(float singleColumnHeight, float viewportHeight)
        {
            float content = singleColumnHeight + BottomPadding;
            if (content < viewportHeight) content = viewportHeight;
            if (content < 0f) content = 0f;
            return content;
        }

        /// <summary>标签页是否发生变化：变化时必须复位滚动位置并重建高度缓存。</summary>
        public static bool ShouldResetScroll(int cachedTab, int currentTab)
        {
            return cachedTab != currentTab;
        }
    }
}
