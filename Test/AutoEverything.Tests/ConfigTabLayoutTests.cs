using System;
using AutoEverything.UI;

namespace AutoEverything.Tests
{
    /// <summary>
    /// ConfigTabLayout 滚动布局契约（不依赖游戏运行时）。
    ///
    /// 回归背景：配置窗口曾把"上一帧内容高度"直接当作 Listing 的矩形高度。
    /// RimWorld 的 Listing 在内容超出矩形高度时会自动新开一列（curX 右移、curY 归零），
    /// 而滚动视图只显示第一列，超出的配置项被画到屏幕外——
    /// 表现为"人员自动页切到其他标签再切回来后，前排/射手/工人/贸易选项消失"。
    /// 分列还让 CurHeight 只返回最后一列高度，高度缓存越滚越小。
    ///
    /// 覆盖点：
    /// 1. 内容高度 = 单列累计高度 + 底部留白；
    /// 2. 内容比视口矮时取视口高度（无意义滚动条不应出现）；
    /// 3. 非法高度安全兜底；
    /// 4. 高度缓存一次收敛、不震荡（分列震荡的直接成因）；
    /// 5. 标签变化必须复位滚动与高度缓存。
    /// </summary>
    public static class ConfigTabLayoutTests
    {
        public static int RunAll()
        {
            int failures = 0;
            int total = 0;

            // ── 内容高度计算 ──
            CheckFloat(ConfigTabLayout.ComputeContentHeight(1200f, 596f), 1240f,
                "长内容高度 = 单列高度 + 底部留白", ref failures, ref total);
            CheckFloat(ConfigTabLayout.ComputeContentHeight(0f, 596f), 596f,
                "空内容至少占满视口", ref failures, ref total);
            CheckFloat(ConfigTabLayout.ComputeContentHeight(200f, 596f), 596f,
                "短内容按视口高度，不产生滚动范围", ref failures, ref total);
            CheckFloat(ConfigTabLayout.ComputeContentHeight(596f - ConfigTabLayout.BottomPadding, 596f), 596f,
                "内容恰好等于视口时不出现滚动条", ref failures, ref total);
            CheckFloat(ConfigTabLayout.ComputeContentHeight(5000f, 0f), 5040f,
                "视口为 0 时仍按内容高度", ref failures, ref total);
            CheckFloat(ConfigTabLayout.ComputeContentHeight(-100f, 0f), 0f,
                "负内容高度安全兜底为 0", ref failures, ref total);
            CheckFloat(ConfigTabLayout.ComputeContentHeight(0f, -50f), ConfigTabLayout.BottomPadding,
                "负视口按内容留白兜底", ref failures, ref total);

            // ── 高度缓存收敛性（回归：不能像自动分列那样来回震荡） ──
            // 模拟连续多帧绘制同一份内容：高度必须稳定在"内容高度 + 留白"
            const float contentHeight = 1550f;
            const float viewport = 596f;
            float cached = ConfigTabLayout.ComputeContentHeight(0f, viewport);
            CheckFloat(cached, viewport, "首帧按视口高度起算", ref failures, ref total);
            for (int i = 0; i < 10; i++)
            {
                cached = ConfigTabLayout.ComputeContentHeight(contentHeight, viewport);
            }
            CheckFloat(cached, contentHeight + ConfigTabLayout.BottomPadding,
                "多帧后高度稳定收敛", ref failures, ref total);

            // 内容高度增长时滚动范围单调增长（不允许出现"越画越少"）
            float prev = -1f;
            float[] heights = { 0f, 100f, 596f, 597f, 1200f, 3000f };
            for (int i = 0; i < heights.Length; i++)
            {
                float current = ConfigTabLayout.ComputeContentHeight(heights[i], viewport);
                CheckBool(current >= prev, true,
                    "内容高度 " + heights[i] + " 时滚动范围不缩小", ref failures, ref total);
                prev = current;
            }

            // ── 标签切换检测 ──
            CheckBool(ConfigTabLayout.ShouldResetScroll((int)Tab.None, (int)Tab.Person), true,
                "首次打开（缓存为 None）需复位", ref failures, ref total);
            CheckBool(ConfigTabLayout.ShouldResetScroll((int)Tab.Person, (int)Tab.Work), true,
                "切到其他标签需复位", ref failures, ref total);
            CheckBool(ConfigTabLayout.ShouldResetScroll((int)Tab.Work, (int)Tab.Person), true,
                "切回人员自动需复位", ref failures, ref total);
            CheckBool(ConfigTabLayout.ShouldResetScroll((int)Tab.Person, (int)Tab.Person), false,
                "同一标签不重复复位", ref failures, ref total);

            Console.WriteLine($"[ConfigTabLayoutTests] 累计 {total - failures}/{total} passed");
            return failures;
        }

        // 与 MainTabWindow_AutoEverything.ConfigTab 取值一致的标签标识（测试不加载窗口类型）
        private enum Tab
        {
            None = -1,
            Person = 0,
            Work = 1
        }

        private static void CheckFloat(float actual, float expected, string label,
            ref int failures, ref int total)
        {
            total++;
            if (Math.Abs(actual - expected) > 0.001f)
            {
                Console.WriteLine($"  FAIL {label}: expected {expected}, got {actual}");
                failures++;
            }
        }

        private static void CheckBool(bool actual, bool expected, string label,
            ref int failures, ref int total)
        {
            total++;
            if (actual != expected)
            {
                Console.WriteLine($"  FAIL {label}: expected {expected}, got {actual}");
                failures++;
            }
        }
    }
}
