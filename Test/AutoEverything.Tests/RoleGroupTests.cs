using System;
using System.Collections.Generic;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.Tests
{
    /// <summary>
    /// RoleDetector.EvaluateRoleGroupCore 五类角色分组判定的单元测试。
    ///
    /// 覆盖点：
    /// 1. 强制判定条件（前排/射手/工人/贸易逐角色勾选，默认全部勾选）；
    /// 2. 强制条件的优先级顺序（无法施暴 &gt; 食尸鬼 &gt; 格斗者 &gt; 格斗专家 &gt; 射击专家 &gt; 猎人 &gt; 生产专家 &gt; 道德家）；
    /// 3. 关闭开关后不应生效，回落到技能/兴趣补充判定；
    /// 4. 技能/兴趣补充判定的各分支与兜底（其他）。
    ///
    /// 注：这里只使用 RoleGroupRules.Default，不读取 AESettings，测试进程无需加载游戏程序集。
    /// </summary>
    public static class RoleGroupTests
    {
        public static int RunAll()
        {
            int failures = 0;
            int total = 0;
            RoleGroupRules rules = RoleGroupRules.Default;

            // ── 强制判定：默认勾选时命中即归入对应角色 ──
            Check(rules, new RoleGroupInput { IsGhoul = true, Shooting = 20, Melee = 0 },
                RoleGroup.Front, "食尸鬼 → 前排", ref failures, ref total);
            Check(rules, new RoleGroupInput { HasBrawlerTrait = true, Shooting = 20, Melee = 0 },
                RoleGroup.Front, "格斗者特质 → 前排", ref failures, ref total);
            Check(rules, new RoleGroupInput { IsMeleeSpecialist = true, Shooting = 20, Melee = 0 },
                RoleGroup.Front, "格斗专家职位 → 前排", ref failures, ref total);
            Check(rules, new RoleGroupInput { IsShootingSpecialist = true, Melee = 20, Shooting = 0 },
                RoleGroup.Shooter, "射击专家职位 → 射手", ref failures, ref total);
            Check(rules, new RoleGroupInput { IsHunter = true, Melee = 20, Shooting = 0 },
                RoleGroup.Shooter, "猎人 → 射手", ref failures, ref total);
            Check(rules, new RoleGroupInput { ViolentDisabled = true, Melee = 20 },
                RoleGroup.Worker, "无法施暴 → 工人", ref failures, ref total);
            Check(rules, new RoleGroupInput { IsProductionSpecialist = true, Shooting = 20 },
                RoleGroup.Worker, "生产专家职位 → 工人", ref failures, ref total);
            Check(rules, new RoleGroupInput { IsSocialSpecialist = true, Shooting = 20, Melee = 20 },
                RoleGroup.Trade, "道德家职位 → 贸易", ref failures, ref total);

            // ── 强制条件优先级 ──
            Check(rules, new RoleGroupInput { ViolentDisabled = true, HasBrawlerTrait = true },
                RoleGroup.Worker, "无法施暴优先于格斗者特质", ref failures, ref total);
            Check(rules, new RoleGroupInput { HasBrawlerTrait = true, IsShootingSpecialist = true },
                RoleGroup.Front, "前排强制条件优先于射手", ref failures, ref total);
            Check(rules, new RoleGroupInput { IsShootingSpecialist = true, IsHunter = true },
                RoleGroup.Shooter, "射击专家与猎人同为射手", ref failures, ref total);

            // ── 关闭开关后不生效，回落到技能判定 ──
            RoleGroupRules noGhoul = RoleGroupRules.Default;
            noGhoul.GhoulToFront = false;
            Check(noGhoul, new RoleGroupInput { IsGhoul = true, Shooting = 20, Melee = 10 },
                RoleGroup.Shooter, "关闭食尸鬼开关后回落到射手", ref failures, ref total);

            RoleGroupRules noPacifist = RoleGroupRules.Default;
            noPacifist.PacifistToWorker = false;
            Check(noPacifist, new RoleGroupInput { ViolentDisabled = true, Melee = 9, Shooting = 1 },
                RoleGroup.Front, "关闭无法施暴开关后回落到前排", ref failures, ref total);

            RoleGroupRules noHunter = RoleGroupRules.Default;
            noHunter.HunterAsShooter = false;
            Check(noHunter, new RoleGroupInput { IsHunter = true, Shooting = 0, Melee = 0 },
                RoleGroup.Worker, "关闭猎人开关后按低战斗技能归工人", ref failures, ref total);

            RoleGroupRules noMoralist = RoleGroupRules.Default;
            noMoralist.SocialSpecialistToTrade = false;
            Check(noMoralist, new RoleGroupInput { IsSocialSpecialist = true, Shooting = 0, Melee = 0, Social = 0 },
                RoleGroup.Worker, "关闭道德家开关后不再归贸易", ref failures, ref total);

            // ── 技能/兴趣补充判定 ──
            Check(rules, new RoleGroupInput { Melee = 9, Shooting = 3, MeleePassion = true },
                RoleGroup.Front, "近战兴趣优先 → 前排", ref failures, ref total);
            Check(rules, new RoleGroupInput { Melee = 3, Shooting = 3, ShootingPassion = true },
                RoleGroup.Shooter, "射击兴趣优先 → 射手", ref failures, ref total);
            Check(rules, new RoleGroupInput { Melee = 20, Shooting = 5, MeleePassion = true, ShootingPassion = true },
                RoleGroup.Front, "双兴趣时近战等级更高 → 前排", ref failures, ref total);
            Check(rules, new RoleGroupInput { Shooting = 9, Melee = 3 },
                RoleGroup.Shooter, "射击阈值高于近战 → 射手", ref failures, ref total);
            Check(rules, new RoleGroupInput { Social = 9, Shooting = 0, Melee = 0 },
                RoleGroup.Trade, "社交阈值达标 → 贸易", ref failures, ref total);
            Check(rules, new RoleGroupInput { Shooting = 4, Melee = 4 },
                RoleGroup.Worker, "双低战斗技能 → 工人", ref failures, ref total);
            Check(rules, new RoleGroupInput { Shooting = 6, Melee = 6 },
                RoleGroup.Other, "未命中任何规则 → 其他", ref failures, ref total);

            Console.WriteLine($"[RoleGroupTests] 判定规则 {total - failures}/{total} passed");

            failures += RunOrderTests(ref total);
            failures += RunDiffTests(ref total);
            failures += RunLayoutTests(ref total);
            Console.WriteLine($"[RoleGroupTests] 含顺序/变更检测/布局契约共 {total - failures}/{total} passed");
            return failures;
        }

        /// <summary>
        /// 分组框布局契约：
        /// 1. 头部带高度 = 标签订位 + 标签高度 + 安全间距；
        /// 2. 下移距离保证头像纹理顶部不低于头部带底边（任意缩放下均成立）；
        /// 3. 布局已足够时不下移（负值截断为 0）；
        /// 4. 标签宽度按分组框实际宽度收缩，过窄返回 0，图标过挤时只显示文字；
        /// 5. 分组框可见顶边与标签位置不随缩放漂移。
        /// </summary>
        public static int RunLayoutTests(ref int total)
        {
            int failures = 0;

            CheckFloat(RoleGroupLayout.HeaderHeight, 22f, "头部带高度", ref failures, ref total);
            CheckFloat(RoleGroupLayout.HeaderHeight - (RoleGroupLayout.TagMargin + RoleGroupLayout.TagHeight),
                RoleGroupLayout.TagGap, "头部带底部安全间距", ref failures, ref total);

            // 原版 minDrawLocY = 21，头像纹理上探 27*scale
            CheckFloat(RoleGroupLayout.ComputeHeaderOffset(21f, 27f), 28f,
                "scale=1 下移距离", ref failures, ref total);
            CheckFloat(RoleGroupLayout.ComputeHeaderOffset(21f, 13.5f), 14.5f,
                "scale=0.5 下移距离", ref failures, ref total);
            CheckFloat(RoleGroupLayout.ComputeHeaderOffset(100f, 27f), 0f,
                "空间充足时不下移", ref failures, ref total);
            CheckFloat(RoleGroupLayout.ComputeHeaderOffset(0f, 0f), 22f,
                "无上探时仍预留头部带", ref failures, ref total);

            // 任意缩放下：槽位下移后，头像纹理顶部（slotY - 27*scale）不低于头部带底边
            for (float scale = 0.4f; scale <= 1.001f; scale += 0.1f)
            {
                float overhang = 27f * scale;
                float offset = RoleGroupLayout.ComputeHeaderOffset(21f, overhang);
                float textureTop = 21f + offset - overhang;
                CheckBool(textureTop >= RoleGroupLayout.HeaderHeight - 0.001f, true,
                    "缩放 " + scale.ToString("0.0") + " 头像不越过头部带", ref failures, ref total);
            }

            // 标签宽度：期望值 = 内边距 + 图标 + 间距 + 文字，且不超过分组框
            CheckFloat(RoleGroupLayout.ComputeTagWidth(24f, 200f), 46f,
                "宽松分组框取期望宽度", ref failures, ref total);
            CheckFloat(RoleGroupLayout.ComputeTagWidth(24f, 30f), 26f,
                "窄分组框按可用宽度收缩", ref failures, ref total);
            CheckFloat(RoleGroupLayout.ComputeTagWidth(24f, 4f), 0f,
                "分组框过窄时放弃标签", ref failures, ref total);
            CheckFloat(RoleGroupLayout.ComputeTagWidth(24f, 2f), 0f,
                "分组框宽度非正时放弃标签", ref failures, ref total);

            CheckBool(RoleGroupLayout.CanShowTagIcon(46f), true, "宽度足够显示图标", ref failures, ref total);
            CheckBool(RoleGroupLayout.CanShowTagIcon(26f), false, "过窄时只显示文字", ref failures, ref total);

            // 可见顶边与标签位置：原版 rect.y = -12*scale，加上边距后应回到栏基线
            for (float scale = 0.4f; scale <= 1.001f; scale += 0.2f)
            {
                CheckFloat(RoleGroupLayout.ComputeFrameVisibleTop(-12f * scale, scale), 0f,
                    "缩放 " + scale.ToString("0.0") + " 可见顶边为基线", ref failures, ref total);
                CheckFloat(RoleGroupLayout.ComputeTagY(-12f * scale, scale), RoleGroupLayout.TagMargin,
                    "缩放 " + scale.ToString("0.0") + " 标签贴在可见顶边下方", ref failures, ref total);
            }

            Console.WriteLine($"[RoleGroupLayoutTests] 累计 {total - failures}/{total} passed");
            return failures;
        }

        /// <summary>
        /// RoleGroupRules.DiffersFrom 变更检测：
        /// 面板在绘制前后做快照对比，任一项配置变化都必须触发重新分组，
        /// 否则玩家在"人员自动/工作自动"标签页调整后殖民者栏不会刷新。
        /// </summary>
        public static int RunDiffTests(ref int total)
        {
            int failures = 0;
            RoleGroupRules baseRules = RoleGroupRules.Default;

            // 相同规则不应误判为变化（两个标签页每帧都会执行一次对比）
            CheckBool(baseRules.DiffersFrom(baseRules), false,
                "相同规则不应视为变化", ref failures, ref total);
            RoleGroupRules copy = RoleGroupRules.Default;
            CheckBool(copy.DiffersFrom(baseRules), false,
                "同值副本不应视为变化", ref failures, ref total);

            // 逐字段变更：任一字段变化必须被检测到
            RoleGroupRules r = RoleGroupRules.Default;
            r.FrontSkillMin = 9;
            CheckBool(r.DiffersFrom(baseRules), true, "前排阈值变化被检测", ref failures, ref total);

            r = RoleGroupRules.Default;
            r.ShooterSkillMin = 9;
            CheckBool(r.DiffersFrom(baseRules), true, "射手阈值变化被检测", ref failures, ref total);

            r = RoleGroupRules.Default;
            r.TradeSkillMin = 9;
            CheckBool(r.DiffersFrom(baseRules), true, "贸易阈值变化被检测", ref failures, ref total);

            r = RoleGroupRules.Default;
            r.WorkerMaxCombatSkill = 6;
            CheckBool(r.DiffersFrom(baseRules), true, "工人战斗阈值变化被检测", ref failures, ref total);

            r = RoleGroupRules.Default;
            r.PassionPriority = false;
            CheckBool(r.DiffersFrom(baseRules), true, "兴趣优先开关变化被检测", ref failures, ref total);

            r = RoleGroupRules.Default;
            r.BrawlerTraitToFront = false;
            CheckBool(r.DiffersFrom(baseRules), true, "格斗者开关变化被检测", ref failures, ref total);

            r = RoleGroupRules.Default;
            r.HunterAsShooter = false;
            CheckBool(r.DiffersFrom(baseRules), true, "猎人开关变化被检测", ref failures, ref total);

            r = RoleGroupRules.Default;
            r.GhoulToFront = false;
            CheckBool(r.DiffersFrom(baseRules), true, "食尸鬼开关变化被检测", ref failures, ref total);

            r = RoleGroupRules.Default;
            r.MeleeSpecialistToFront = false;
            CheckBool(r.DiffersFrom(baseRules), true, "格斗专家开关变化被检测", ref failures, ref total);

            r = RoleGroupRules.Default;
            r.ShootingSpecialistToShooter = false;
            CheckBool(r.DiffersFrom(baseRules), true, "射击专家开关变化被检测", ref failures, ref total);

            r = RoleGroupRules.Default;
            r.PacifistToWorker = false;
            CheckBool(r.DiffersFrom(baseRules), true, "无法施暴开关变化被检测", ref failures, ref total);

            r = RoleGroupRules.Default;
            r.ProductionSpecialistToWorker = false;
            CheckBool(r.DiffersFrom(baseRules), true, "生产专家开关变化被检测", ref failures, ref total);

            r = RoleGroupRules.Default;
            r.SocialSpecialistToTrade = false;
            CheckBool(r.DiffersFrom(baseRules), true, "道德家开关变化被检测", ref failures, ref total);

            Console.WriteLine($"[RoleGroupDiffTests] 累计 {total - failures}/{total} passed");
            return failures;
        }

        /// <summary>
        /// 分组显示顺序（右键拖拽换序的数据契约）：
        /// 顺序数组必须始终是 0..4 的完整排列，"插入到目标分组之前"，
        /// 且可视序号取数组下标——重新分组与排序都依赖这一点。
        /// </summary>
        public static int RunOrderTests(ref int total)
        {
            int failures = 0;

            List<int> order = RoleDetector.DefaultRoleGroupOrder();
            CheckOrder(order, new[] { 0, 1, 2, 3, 4 }, "默认顺序", ref failures, ref total);
            CheckBool(RoleDetector.IsValidRoleGroupOrder(order), true, "默认顺序合法", ref failures, ref total);

            // 前排拖到贸易上：插入到贸易之前 → [射手, 工人, 前排, 贸易, 其他]
            CheckBool(RoleDetector.MoveRoleGroupInOrder(order, (int)RoleGroup.Front, (int)RoleGroup.Trade),
                true, "前排拖到贸易应发生变化", ref failures, ref total);
            CheckOrder(order, new[] { 1, 2, 0, 3, 4 }, "前排插入到贸易之前", ref failures, ref total);
            CheckInt(RoleDetector.GetRoleGroupOrderIn(order, (int)RoleGroup.Front), 2,
                "前排可视序号应为 2", ref failures, ref total);
            CheckBool(RoleDetector.IsValidRoleGroupOrder(order), true, "换序后仍是合法排列", ref failures, ref total);

            // 其他拖到射手：插入到射手之前 → [其他, 射手, 工人, 前排, 贸易]
            RoleDetector.MoveRoleGroupInOrder(order, (int)RoleGroup.Other, (int)RoleGroup.Shooter);
            CheckOrder(order, new[] { 4, 1, 2, 0, 3 }, "其他插入到射手之前", ref failures, ref total);

            // 拖到自己身上不应变化
            CheckBool(RoleDetector.MoveRoleGroupInOrder(order, (int)RoleGroup.Worker, (int)RoleGroup.Worker),
                false, "拖到自身不变化", ref failures, ref total);
            CheckOrder(order, new[] { 4, 1, 2, 0, 3 }, "拖到自身后顺序不变", ref failures, ref total);

            // 非法顺序必须被识别（缺失、重复、越界、空）
            CheckBool(RoleDetector.IsValidRoleGroupOrder(null), false, "null 非法", ref failures, ref total);
            CheckBool(RoleDetector.IsValidRoleGroupOrder(new List<int> { 0, 1, 2, 3 }), false,
                "数量不足非法", ref failures, ref total);
            CheckBool(RoleDetector.IsValidRoleGroupOrder(new List<int> { 0, 1, 2, 3, 3 }), false,
                "重复值非法", ref failures, ref total);
            CheckBool(RoleDetector.IsValidRoleGroupOrder(new List<int> { 0, 1, 2, 3, 5 }), false,
                "越界值非法", ref failures, ref total);

            Console.WriteLine($"[RoleGroupOrderTests] 累计 {total - failures}/{total} passed");
            return failures;
        }

        private static void CheckOrder(List<int> actual, int[] expected, string label,
            ref int failures, ref int total)
        {
            total++;
            bool ok = actual != null && actual.Count == expected.Length;
            if (ok)
            {
                for (int i = 0; i < expected.Length; i++)
                {
                    if (actual[i] != expected[i]) { ok = false; break; }
                }
            }
            if (!ok)
            {
                Console.WriteLine($"  FAIL order {label}: expected [{string.Join(",", expected)}], " +
                                  $"got [{(actual == null ? "null" : string.Join(",", actual))}]");
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

        private static void CheckInt(int actual, int expected, string label,
            ref int failures, ref int total)
        {
            total++;
            if (actual != expected)
            {
                Console.WriteLine($"  FAIL {label}: expected {expected}, got {actual}");
                failures++;
            }
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

        private static void Check(RoleGroupRules rules, RoleGroupInput input, RoleGroup expected,
            string label, ref int failures, ref int total)
        {
            total++;
            RoleGroup actual = RoleDetector.EvaluateRoleGroupCore(input, rules);
            if (actual != expected)
            {
                Console.WriteLine($"  FAIL EvaluateRoleGroupCore: {label}: expected {expected}, got {actual}");
                failures++;
            }
        }
    }
}
