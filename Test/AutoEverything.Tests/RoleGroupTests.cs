using System;
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

            Console.WriteLine($"[RoleGroupTests] {total - failures}/{total} passed");
            return failures;
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
