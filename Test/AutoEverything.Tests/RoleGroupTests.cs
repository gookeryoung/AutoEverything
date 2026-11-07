using System;
using System.Collections.Generic;
using AutoEverything.Core;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.Tests
{
    /// <summary>
    /// RoleDetector.EvaluateRoleGroupCore 五类角色分组判定的单元测试。
    ///
    /// 覆盖点：
    /// 1. 强制判定条件（前排/射手/工人/贸易逐角色勾选，默认全部勾选），
    ///    含乱开枪特质、工作狂特质、生产/社交兴趣双火、俊俏/沉鱼落雁特质；
    /// 2. 强制条件的优先级顺序（无法施暴 &gt; 食尸鬼 &gt; 格斗者 &gt; 格斗专家 &gt; 射击专家 &gt; 乱开枪 &gt; 猎人
    ///    &gt; 生产专家 &gt; 工作狂 &gt; 生产双火 &gt; 道德家 &gt; 俊俏 &gt; 社交双火）；
    /// 3. 关闭开关后不应生效，回落到技能/兴趣补充判定，且新开关彼此独立；
    /// 4. 特质 degree 与兴趣 tier 的边界判定；
    /// 5. 技能/兴趣补充判定的各分支与兜底（其他）。
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
            Check(rules, new RoleGroupInput { IsTriggerHappy = true, Shooting = 0, Melee = 0 },
                RoleGroup.Shooter, "乱开枪特性 → 射手", ref failures, ref total);
            Check(rules, new RoleGroupInput { HasIndustrious = true, Shooting = 20 },
                RoleGroup.Worker, "工作狂特质 → 工人", ref failures, ref total);
            Check(rules, new RoleGroupInput { CraftingPassionMajor = true, Shooting = 20 },
                RoleGroup.Worker, "生产兴趣双火 → 工人", ref failures, ref total);
            Check(rules, new RoleGroupInput { HasBeauty = true, Shooting = 20, Melee = 20 },
                RoleGroup.Trade, "俊俏/沉鱼落雁 → 贸易", ref failures, ref total);
            Check(rules, new RoleGroupInput { SocialPassionMajor = true, Shooting = 20, Melee = 20 },
                RoleGroup.Trade, "社交兴趣双火 → 贸易", ref failures, ref total);

            // ── 新增条件与既有条件的优先级交叉 ──
            Check(rules, new RoleGroupInput { ViolentDisabled = true, HasBeauty = true },
                RoleGroup.Worker, "无法施暴优先于俊俏特质", ref failures, ref total);
            Check(rules, new RoleGroupInput { HasBrawlerTrait = true, HasIndustrious = true },
                RoleGroup.Front, "格斗者特质优先于工作狂", ref failures, ref total);
            Check(rules, new RoleGroupInput { IsTriggerHappy = true, HasBeauty = true },
                RoleGroup.Shooter, "乱开枪优先于俊俏特质（射手先于贸易）", ref failures, ref total);
            Check(rules, new RoleGroupInput { HasIndustrious = true, IsSocialSpecialist = true },
                RoleGroup.Worker, "工作狂优先于道德家（工人先于贸易）", ref failures, ref total);
            Check(rules, new RoleGroupInput { CraftingPassionMajor = true, SocialPassionMajor = true },
                RoleGroup.Worker, "生产双火优先于社交双火", ref failures, ref total);
            Check(rules, new RoleGroupInput { HasBeauty = true, SocialPassionMajor = true },
                RoleGroup.Trade, "俊俏与社交双火同归贸易", ref failures, ref total);
            Check(rules, new RoleGroupInput { IsShootingSpecialist = true, IsTriggerHappy = true },
                RoleGroup.Shooter, "射击专家与乱开枪同归射手", ref failures, ref total);

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

            // 新增补充条件可独立关闭，关闭后回落到技能/兴趣判定
            RoleGroupRules noTriggerHappy = RoleGroupRules.Default;
            noTriggerHappy.TriggerHappyToShooter = false;
            Check(noTriggerHappy, new RoleGroupInput { IsTriggerHappy = true, Shooting = 0, Melee = 0 },
                RoleGroup.Worker, "关闭乱开枪开关后按低战斗技能归工人", ref failures, ref total);

            RoleGroupRules noIndustrious = RoleGroupRules.Default;
            noIndustrious.IndustriousToWorker = false;
            Check(noIndustrious, new RoleGroupInput { HasIndustrious = true, Shooting = 20, Melee = 0 },
                RoleGroup.Shooter, "关闭工作狂开关后回落到射手", ref failures, ref total);

            RoleGroupRules noCraftingMajor = RoleGroupRules.Default;
            noCraftingMajor.CraftingMajorToWorker = false;
            Check(noCraftingMajor, new RoleGroupInput { CraftingPassionMajor = true, Shooting = 20, Melee = 0 },
                RoleGroup.Shooter, "关闭生产双火开关后回落到射手", ref failures, ref total);

            RoleGroupRules noBeauty = RoleGroupRules.Default;
            noBeauty.BeautyToTrade = false;
            Check(noBeauty, new RoleGroupInput { HasBeauty = true, Shooting = 20, Melee = 0 },
                RoleGroup.Shooter, "关闭俊俏开关后回落到射手", ref failures, ref total);

            RoleGroupRules noSocialMajor = RoleGroupRules.Default;
            noSocialMajor.SocialMajorToTrade = false;
            Check(noSocialMajor, new RoleGroupInput { SocialPassionMajor = true, Shooting = 20, Melee = 0 },
                RoleGroup.Shooter, "关闭社交双火开关后回落到射手", ref failures, ref total);

            // 新开关彼此独立：关闭一个不应影响另一个条件
            RoleGroupRules noBeautyKeepSocial = RoleGroupRules.Default;
            noBeautyKeepSocial.BeautyToTrade = false;
            Check(noBeautyKeepSocial, new RoleGroupInput { HasBeauty = true, SocialPassionMajor = true, Shooting = 20 },
                RoleGroup.Trade, "关闭俊俏开关后社交双火仍归贸易", ref failures, ref total);

            RoleGroupRules noIndustriousKeepCrafting = RoleGroupRules.Default;
            noIndustriousKeepCrafting.IndustriousToWorker = false;
            Check(noIndustriousKeepCrafting, new RoleGroupInput { HasIndustrious = true, CraftingPassionMajor = true },
                RoleGroup.Worker, "关闭工作狂开关后生产双火仍归工人", ref failures, ref total);

            RoleGroupRules noCraftingKeepIndustrious = RoleGroupRules.Default;
            noCraftingKeepIndustrious.CraftingMajorToWorker = false;
            Check(noCraftingKeepIndustrious, new RoleGroupInput { HasIndustrious = true, CraftingPassionMajor = true },
                RoleGroup.Worker, "关闭生产双火开关后工作狂仍归工人", ref failures, ref total);

            RoleGroupRules noTriggerHappyKeepSpecialist = RoleGroupRules.Default;
            noTriggerHappyKeepSpecialist.TriggerHappyToShooter = false;
            Check(noTriggerHappyKeepSpecialist, new RoleGroupInput { IsTriggerHappy = true, IsShootingSpecialist = true },
                RoleGroup.Shooter, "关闭乱开枪开关后射击专家仍归射手", ref failures, ref total);

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
            failures += RunDegreeRuleTests(ref total);
            failures += RunLayoutTests(ref total);
            failures += RunGroupAssignTests(ref total);
            Console.WriteLine($"[RoleGroupTests] 含顺序/变更检测/布局契约共 {total - failures}/{total} passed");
            return failures;
        }

        /// <summary>
        /// AssignGroupIds 数据契约（殖民者栏重编组的纯函数）：
        /// 1. 同原生分组、同角色序的相邻条目合并为同一组；
        /// 2. 角色序变化、原生分组变化（跨地图/远行队）都会开新组；
        /// 3. 空占位条目（NoRoleOrder）各自独立成组；
        /// 4. 返回值必须覆盖所有写入的 group id —— 这是防止原生
        ///    CalculateColonistsInGroup 以 group 为下标取数组时越界（表现为屏幕闪烁）的硬约束。
        /// </summary>
        public static int RunGroupAssignTests(ref int total)
        {
            int failures = 0;
            int noRole = RoleGroupLayout.NoRoleOrder;

            CheckAssign(new int[0], new int[0], new int[0], 0, "空列表", ref failures, ref total);
            CheckAssign(new[] { 0 }, new[] { 0 }, new[] { 0 }, 1, "单条目成一组", ref failures, ref total);
            CheckAssign(new[] { 0, 0, 0 }, new[] { 0, 0, 0 }, new[] { 0, 0, 0 }, 1,
                "同图同角色合并", ref failures, ref total);
            CheckAssign(new[] { 0, 0, 0 }, new[] { 0, 1, 2 }, new[] { 0, 1, 2 }, 3,
                "角色变化开新组", ref failures, ref total);
            CheckAssign(new[] { 0, 0, 0 }, new[] { 0, 1, 0 }, new[] { 0, 1, 2 }, 3,
                "同角色不相邻不合并", ref failures, ref total);
            CheckAssign(new[] { 0, 0, 1, 1 }, new[] { 0, 0, 0, 0 }, new[] { 0, 0, 1, 1 }, 2,
                "跨地图同角色分组", ref failures, ref total);
            CheckAssign(new[] { 0, 0, 1 }, new[] { 0, noRole, 0 }, new[] { 0, 1, 2 }, 3,
                "空占位条目独立成组", ref failures, ref total);
            CheckAssign(new[] { 0, 1 }, new[] { noRole, noRole }, new[] { 0, 1 }, 2,
                "连续空占位也各自成组", ref failures, ref total);

            // 回归：多地图 + 连续同角色时组数应等于"跨图段数"，且必须覆盖所有 id
            int[] orig = { 0, 0, 1, 1, 2, 2 };
            int[] order = { 0, 0, 1, 1, 2, 2 };
            int[] assigned = new int[orig.Length];
            int groups = RoleGroupLayout.AssignGroupIds(orig, order, assigned);
            CheckInt(groups, 3, "多地图组数", ref failures, ref total);
            CheckGroupIds(assigned, groups, "多地图组数覆盖所有 id", ref failures, ref total);

            // 任意混合序列都必须满足 id ∈ [0, groupsCount)
            int[] mixedOrig = { 0, 0, 0, 1, 1, 2, 2, 2, 3 };
            int[] mixedOrder = { 4, 4, noRole, 1, 2, 0, 0, noRole, 3 };
            int[] mixedAssigned = new int[mixedOrig.Length];
            int mixedGroups = RoleGroupLayout.AssignGroupIds(mixedOrig, mixedOrder, mixedAssigned);
            CheckGroupIds(mixedAssigned, mixedGroups, "混合序列 id 落在组数范围内", ref failures, ref total);
            CheckInt(mixedAssigned[0], mixedAssigned[1], "混合序列首两项应同组", ref failures, ref total);
            CheckBool(mixedAssigned[1] != mixedAssigned[2], true,
                "空占位条目应另起一组", ref failures, ref total);

            Console.WriteLine($"[RoleGroupAssignTests] 累计 {total - failures}/{total} passed");
            return failures;
        }

        private static void CheckAssign(int[] origGroups, int[] roleOrders, int[] expectedIds,
            int expectedGroups, string label, ref int failures, ref int total)
        {
            int[] assigned = new int[origGroups.Length];
            int groups = RoleGroupLayout.AssignGroupIds(origGroups, roleOrders, assigned);
            CheckInt(groups, expectedGroups, label + "：组数", ref failures, ref total);
            for (int i = 0; i < expectedIds.Length; i++)
            {
                CheckInt(assigned[i], expectedIds[i], label + "：第 " + i + " 项 id", ref failures, ref total);
            }
            CheckGroupIds(assigned, groups, label + "：id 范围", ref failures, ref total);
        }

        /// <summary>硬约束：所有 id 必须落在 [0, groupsCount)，否则原生按下标取数组会越界。</summary>
        private static void CheckGroupIds(int[] assigned, int groupsCount, string label,
            ref int failures, ref int total)
        {
            total++;
            for (int i = 0; i < assigned.Length; i++)
            {
                if (assigned[i] < 0 || assigned[i] >= groupsCount)
                {
                    Console.WriteLine($"  FAIL {label}: id[{i}]={assigned[i]} 越界, groupsCount={groupsCount}");
                    failures++;
                    return;
                }
            }
        }

        /// <summary>
        /// 特质 degree / 兴趣 tier 边界判定：
        /// 1. 乱开枪仅认 degree=-1，冷枪手(+1)不得命中；
        /// 2. 工作狂/俊俏认 >=1，懒惰/丑陋等负值不得命中；
        /// 3. 双火兴趣认 Major 及以上，单火及以下不得命中。
        /// 这些边界直接决定 Pawn → RoleGroupInput 的布尔值，错一位就会误分组。
        /// </summary>
        public static int RunDegreeRuleTests(ref int total)
        {
            int failures = 0;

            CheckBool(RoleGroupRules.IsTriggerHappyDegree(-1), true, "乱开枪 degree=-1 命中", ref failures, ref total);
            CheckBool(RoleGroupRules.IsTriggerHappyDegree(0), false, "乱开枪 degree=0 不命中", ref failures, ref total);
            CheckBool(RoleGroupRules.IsTriggerHappyDegree(1), false, "冷枪手 degree=1 不命中乱开枪", ref failures, ref total);
            CheckBool(RoleGroupRules.IsTriggerHappyDegree(2), false, "乱开枪 degree=2 不命中", ref failures, ref total);

            CheckBool(RoleGroupRules.IsIndustriousDegree(2), true, "工作狂 degree=2 命中", ref failures, ref total);
            CheckBool(RoleGroupRules.IsIndustriousDegree(1), true, "工作狂 degree=1 命中", ref failures, ref total);
            CheckBool(RoleGroupRules.IsIndustriousDegree(0), false, "工作狂 degree=0 不命中", ref failures, ref total);
            CheckBool(RoleGroupRules.IsIndustriousDegree(-1), false, "懒惰 degree=-1 不命中", ref failures, ref total);
            CheckBool(RoleGroupRules.IsIndustriousDegree(-2), false, "极度懒惰 degree=-2 不命中", ref failures, ref total);

            CheckBool(RoleGroupRules.IsBeautyDegree(2), true, "沉鱼落雁 degree=2 命中", ref failures, ref total);
            CheckBool(RoleGroupRules.IsBeautyDegree(1), true, "俊俏 degree=1 命中", ref failures, ref total);
            CheckBool(RoleGroupRules.IsBeautyDegree(0), false, "俊俏 degree=0 不命中", ref failures, ref total);
            CheckBool(RoleGroupRules.IsBeautyDegree(-1), false, "丑陋 degree=-1 不命中", ref failures, ref total);
            CheckBool(RoleGroupRules.IsBeautyDegree(-2), false, "奇丑无比 degree=-2 不命中", ref failures, ref total);

            CheckBool(RoleGroupRules.IsMajorPassionTier(PassionHelper.PassionTier.Critical), true,
                "Critical 兴趣命中双火", ref failures, ref total);
            CheckBool(RoleGroupRules.IsMajorPassionTier(PassionHelper.PassionTier.Major), true,
                "Major 兴趣命中双火", ref failures, ref total);
            CheckBool(RoleGroupRules.IsMajorPassionTier(PassionHelper.PassionTier.Minor), false,
                "Minor 兴趣不命中双火", ref failures, ref total);
            CheckBool(RoleGroupRules.IsMajorPassionTier(PassionHelper.PassionTier.None), false,
                "None 兴趣不命中双火", ref failures, ref total);
            CheckBool(RoleGroupRules.IsMajorPassionTier(PassionHelper.PassionTier.Apathy), false,
                "Apathy 兴趣不命中双火", ref failures, ref total);

            Console.WriteLine($"[RoleGroupDegreeTests] 累计 {total - failures}/{total} passed");
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
        /// 否则玩家在"人员自动"标签页调整后殖民者栏不会刷新。
        /// </summary>
        public static int RunDiffTests(ref int total)
        {
            int failures = 0;
            RoleGroupRules baseRules = RoleGroupRules.Default;

            // 相同规则不应误判为变化（每次绘制都会执行一次对比）
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

            r = RoleGroupRules.Default;
            r.TriggerHappyToShooter = false;
            CheckBool(r.DiffersFrom(baseRules), true, "乱开枪开关变化被检测", ref failures, ref total);

            r = RoleGroupRules.Default;
            r.IndustriousToWorker = false;
            CheckBool(r.DiffersFrom(baseRules), true, "工作狂开关变化被检测", ref failures, ref total);

            r = RoleGroupRules.Default;
            r.CraftingMajorToWorker = false;
            CheckBool(r.DiffersFrom(baseRules), true, "生产双火开关变化被检测", ref failures, ref total);

            r = RoleGroupRules.Default;
            r.BeautyToTrade = false;
            CheckBool(r.DiffersFrom(baseRules), true, "俊俏开关变化被检测", ref failures, ref total);

            r = RoleGroupRules.Default;
            r.SocialMajorToTrade = false;
            CheckBool(r.DiffersFrom(baseRules), true, "社交双火开关变化被检测", ref failures, ref total);

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
