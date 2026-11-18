using System;
using AutoEverything.AutoEquip;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.Tests
{
    /// <summary>
    /// 服装评分内核测试。不依赖 RimWorld 运行时：
    /// 只验证原版护甲结算口径、权重取向、绝缘/惩罚/耐久折算与阈值行为。
    /// </summary>
    public static class ApparelScoreTests
    {
        public static int RunAll()
        {
            int failures = 0;
            int total = 0;

            failures += RunLayerSurvivalTests(ref total);
            failures += RunArmorComponentTests(ref total);
            failures += RunComfortTests(ref total);
            failures += RunPenaltyTests(ref total);
            failures += RunWeightTests(ref total);
            failures += RunEvaluateTests(ref total);
            failures += RunSwapThresholdTests(ref total);

            Console.WriteLine($"[ApparelScore] {total - failures}/{total} passed");
            return failures;
        }

        private static int RunLayerSurvivalTests(ref int total)
        {
            int failures = 0;
            // 无护甲：完全不减伤
            CheckFloat(ApparelScoreCore.LayerSurvival(0f), 1f, 0.0001f, "护甲0 → 剩余伤害1", ref failures, ref total);
            // 单层期望值：1 - 0.75a
            CheckFloat(ApparelScoreCore.LayerSurvival(0.4f), 0.7f, 0.0001f, "护甲0.4 → 剩余0.7", ref failures, ref total);
            CheckFloat(ApparelScoreCore.LayerSurvival(1f), 0.25f, 0.0001f, "护甲1.0 → 剩余0.25", ref failures, ref total);
            // a>1 后剩余按 0.5-0.25a 递减
            CheckFloat(ApparelScoreCore.LayerSurvival(1.5f), 0.125f, 0.0001f, "护甲1.5 → 剩余0.125", ref failures, ref total);
            CheckFloat(ApparelScoreCore.LayerSurvival(2f), 0f, 0.0001f, "护甲2.0 → 完全偏转", ref failures, ref total);
            CheckFloat(ApparelScoreCore.LayerSurvival(3f), 0f, 0.0001f, "护甲超上限仍完全偏转", ref failures, ref total);
            CheckFloat(ApparelScoreCore.LayerSurvival(-1f), 1f, 0.0001f, "负护甲按无护甲处理", ref failures, ref total);

            // 多层乘性叠加：两层分别 1.0 与 0.4
            float stacked = ApparelScoreCore.LayerSurvival(1f) * ApparelScoreCore.LayerSurvival(0.4f);
            CheckFloat(stacked, 0.175f, 0.0001f, "两层期望剩余伤害为乘积 0.175", ref failures, ref total);
            return failures;
        }

        private static int RunArmorComponentTests(ref int total)
        {
            int failures = 0;
            ApparelSetInput bare = new ApparelSetInput { SharpSurvival = 1f, BluntSurvival = 1f, HeatSurvival = 1f };
            CheckFloat(ApparelScoreCore.ArmorComponent(bare, RoleGroup.Front), 0f, 0.0001f,
                "无护甲 → 护甲分量0", ref failures, ref total);

            // 只有锐器护甲，前排权重 1.0/0.7/0.3 → 剩余 (0*1+1*0.7+1*0.3)/2 = 0.5
            ApparelSetInput sharpOnly = new ApparelSetInput { SharpSurvival = 0f, BluntSurvival = 1f, HeatSurvival = 1f };
            CheckFloat(ApparelScoreCore.ArmorComponent(sharpOnly, RoleGroup.Front), 0.5f, 0.0001f,
                "前排锐器护甲 → 0.5", ref failures, ref total);
            // 射手权重 0.6/0.4/0.3 → 剩余 0.7/1.3
            CheckFloat(ApparelScoreCore.ArmorComponent(sharpOnly, RoleGroup.Shooter),
                1f - 0.7f / 1.3f, 0.0001f, "射手锐器护甲权重更低", ref failures, ref total);
            // 工人权重最低
            CheckFloat(ApparelScoreCore.ArmorComponent(sharpOnly, RoleGroup.Worker),
                1f - 0.45f / 0.85f, 0.0001f, "工人锐器护甲权重最低", ref failures, ref total);

            ApparelSetInput full = new ApparelSetInput { SharpSurvival = 0f, BluntSurvival = 0f, HeatSurvival = 0f };
            CheckFloat(ApparelScoreCore.ArmorComponent(full, RoleGroup.Other), 1f, 0.0001f,
                "三类护甲全免疫 → 护甲分量1", ref failures, ref total);
            return failures;
        }

        private static int RunComfortTests(ref int total)
        {
            int failures = 0;
            ApparelSetInput mild = new ApparelSetInput
            {
                Profile = ApparelProfile.Work,
                InsulationCold = 40f,
                InsulationHeat = 25f,
                NeedCold = 0f,
                NeedHeat = 0f
            };
            CheckFloat(ApparelScoreCore.ComfortComponent(mild), 0f, 0.0001f,
                "温和天气绝缘不计分", ref failures, ref total);

            mild.NeedCold = 1f;
            CheckFloat(ApparelScoreCore.ComfortComponent(mild), 1f, 0.0001f,
                "严寒缺口下高冷绝缘满分", ref failures, ref total);

            ApparelSetInput cold = new ApparelSetInput
            {
                Profile = ApparelProfile.Cold,
                InsulationCold = 20f,
                InsulationHeat = 0f
            };
            CheckFloat(ApparelScoreCore.ComfortComponent(cold), 0.5f, 0.0001f,
                "严寒档案按冷绝缘折算", ref failures, ref total);

            ApparelSetInput hot = new ApparelSetInput
            {
                Profile = ApparelProfile.Hot,
                InsulationCold = 40f,
                InsulationHeat = 12.5f
            };
            CheckFloat(ApparelScoreCore.ComfortComponent(hot), 0.5f, 0.0001f,
                "酷暑档案只看热绝缘", ref failures, ref total);

            ApparelSetInput over = new ApparelSetInput { Profile = ApparelProfile.Cold, InsulationCold = 200f };
            CheckFloat(ApparelScoreCore.ComfortComponent(over), 1f, 0.0001f,
                "绝缘超量封顶为1", ref failures, ref total);

            float needCold;
            float needHeat;
            ApparelScoreCore.TemperatureNeed(16f, 26f, 0f, out needCold, out needHeat);
            CheckFloat(needCold, 0.8f, 0.0001f, "低于舒适下限16度 → 冷需求0.8", ref failures, ref total);
            CheckFloat(needHeat, 0f, 0.0001f, "低温不产生热需求", ref failures, ref total);
            ApparelScoreCore.TemperatureNeed(16f, 26f, 100f, out needCold, out needHeat);
            CheckFloat(needHeat, 1f, 0.0001f, "极热封顶为1", ref failures, ref total);
            return failures;
        }

        private static int RunPenaltyTests(ref int total)
        {
            int failures = 0;
            CheckFloat(ApparelScoreCore.MobilityComponent(new ApparelSetInput { MoveSpeedOffset = 0f }), 0f, 0.0001f,
                "无移速惩罚", ref failures, ref total);
            CheckFloat(ApparelScoreCore.MobilityComponent(new ApparelSetInput { MoveSpeedOffset = -0.15f }), 0.5f, 0.0001f,
                "移速-0.15 → 惩罚0.5", ref failures, ref total);
            CheckFloat(ApparelScoreCore.MobilityComponent(new ApparelSetInput { MoveSpeedOffset = -0.6f }), 1f, 0.0001f,
                "移速惩罚封顶为1", ref failures, ref total);
            CheckFloat(ApparelScoreCore.MobilityComponent(new ApparelSetInput { MoveSpeedOffset = 0.2f }), 0f, 0.0001f,
                "正移速不产生惩罚", ref failures, ref total);
            CheckFloat(ApparelScoreCore.WorkComponent(new ApparelSetInput { WorkSpeedOffset = -0.3f }), 1f, 0.0001f,
                "工作惩罚-0.3 → 1", ref failures, ref total);
            return failures;
        }

        private static int RunWeightTests(ref int total)
        {
            int failures = 0;
            ApparelProfile[] profiles =
            {
                ApparelProfile.Work, ApparelProfile.Combat, ApparelProfile.Cold, ApparelProfile.Hot
            };
            ApparelPreset[] presets =
            {
                ApparelPreset.Balanced, ApparelPreset.Heavy, ApparelPreset.Light
            };

            for (int p = 0; p < profiles.Length; p++)
            {
                for (int s = 0; s < presets.Length; s++)
                {
                    ApparelWeights w = ApparelWeights.For(presets[s], profiles[p]);
                    float sum = w.Armor + w.Comfort + w.Mobility + w.Work + w.Durability;
                    CheckFloat(sum, 1f, 0.0001f,
                        "权重和为1：" + profiles[p] + "/" + presets[s], ref failures, ref total);
                    if (w.Armor < 0f || w.Comfort < 0f || w.Mobility < 0f || w.Work < 0f || w.Durability < 0f)
                        CheckFloat(1f, 0f, 0f, "权重不应为负：" + profiles[p] + "/" + presets[s], ref failures, ref total);
                }
            }

            CheckTrue(ApparelWeights.For(ApparelPreset.Heavy, ApparelProfile.Work).Armor
                > ApparelWeights.For(ApparelPreset.Balanced, ApparelProfile.Work).Armor,
                "重甲预设提高护甲权重", ref failures, ref total);
            CheckTrue(ApparelWeights.For(ApparelPreset.Light, ApparelProfile.Work).Mobility
                > ApparelWeights.For(ApparelPreset.Balanced, ApparelProfile.Work).Mobility,
                "轻装预设提高移动权重", ref failures, ref total);
            CheckFloat(ApparelWeights.For(ApparelPreset.Balanced, ApparelProfile.Combat).Work, 0f, 0.0001f,
                "战斗档案不考核工作速度", ref failures, ref total);
            return failures;
        }

        private static int RunEvaluateTests(ref int total)
        {
            int failures = 0;
            ApparelWeights work = ApparelWeights.For(ApparelPreset.Balanced, ApparelProfile.Work);
            ApparelWeights combat = ApparelWeights.For(ApparelPreset.Balanced, ApparelProfile.Combat);

            ApparelSetInput armored = new ApparelSetInput
            {
                Profile = ApparelProfile.Work,
                SharpSurvival = 0.25f,
                BluntSurvival = 0.5f,
                HeatSurvival = 0.7f,
                Durability = 1f
            };
            ApparelSetInput naked = new ApparelSetInput
            {
                Profile = ApparelProfile.Work,
                SharpSurvival = 1f,
                BluntSurvival = 1f,
                HeatSurvival = 1f,
                Durability = 1f
            };
            CheckTrue(ApparelScoreCore.Evaluate(armored, work, RoleGroup.Front)
                > ApparelScoreCore.Evaluate(naked, work, RoleGroup.Front),
                "有甲优于无甲", ref failures, ref total);

            // 重甲：护甲高但移速-0.2（惩罚0.667）
            ApparelSetInput heavy = new ApparelSetInput
            {
                Profile = ApparelProfile.Work,
                SharpSurvival = 0.1f,
                BluntSurvival = 0.2f,
                HeatSurvival = 0.5f,
                MoveSpeedOffset = -0.1f,
                WorkSpeedOffset = -0.1f,
                Durability = 1f
            };
            ApparelSetInput light = new ApparelSetInput
            {
                Profile = ApparelProfile.Work,
                SharpSurvival = 0.55f,
                BluntSurvival = 0.7f,
                HeatSurvival = 0.8f,
                Durability = 1f
            };
            CheckTrue(ApparelScoreCore.Evaluate(heavy, work, RoleGroup.Front)
                > ApparelScoreCore.Evaluate(light, work, RoleGroup.Front),
                "工作档案下高护甲仍可抵消移速惩罚", ref failures, ref total);
            // 同理，战斗档案护甲权重更高，差值应更大
            float heavyCombat = ApparelScoreCore.Evaluate(heavy, combat, RoleGroup.Front);
            float lightCombat = ApparelScoreCore.Evaluate(light, combat, RoleGroup.Front);
            CheckTrue(heavyCombat - lightCombat
                > ApparelScoreCore.Evaluate(heavy, work, RoleGroup.Front) - ApparelScoreCore.Evaluate(light, work, RoleGroup.Front),
                "战斗档案放大护甲差距", ref failures, ref total);

            // 极端移速惩罚：战斗档案下轻装更优
            ApparelSetInput veryHeavy = heavy;
            veryHeavy.MoveSpeedOffset = -0.3f;
            veryHeavy.WorkSpeedOffset = -0.3f;
            CheckTrue(ApparelScoreCore.Evaluate(light, work, RoleGroup.Worker)
                > ApparelScoreCore.Evaluate(veryHeavy, work, RoleGroup.Worker),
                "极大速度惩罚下工作档案偏向轻装", ref failures, ref total);

            // 耐久直接进入评分
            ApparelSetInput worn = armored;
            worn.Durability = 0.2f;
            CheckTrue(ApparelScoreCore.Evaluate(armored, work, RoleGroup.Front)
                > ApparelScoreCore.Evaluate(worn, work, RoleGroup.Front),
                "耐久低者分数更低", ref failures, ref total);

            // 冲突丢弃成本：每件 -3 分
            float baseScore = ApparelScoreCore.Evaluate(armored, work, RoleGroup.Front);
            ApparelSetInput dropOne = armored;
            dropOne.ConflictDropCount = 1;
            CheckFloat(baseScore - ApparelScoreCore.Evaluate(dropOne, work, RoleGroup.Front), 3f, 0.0001f,
                "冲突丢弃一件扣3分", ref failures, ref total);

            ApparelSetInput dropThree = armored;
            dropThree.ConflictDropCount = 3;
            CheckFloat(baseScore - ApparelScoreCore.Evaluate(dropThree, work, RoleGroup.Front), 9f, 0.0001f,
                "冲突丢弃三件扣9分", ref failures, ref total);

            CheckFloat(ApparelScoreCore.Evaluate(naked, work, RoleGroup.Front),
                100f * work.Durability, 0.0001f,
                "裸装只拿耐久分", ref failures, ref total);
            return failures;
        }

        private static int RunSwapThresholdTests(ref int total)
        {
            int failures = 0;
            // 工作档案默认阈值 10%：必须严格超过
            CheckTrue(!EquipPolicy.IsBetterApparel(50f, 50f, 10f),
                "同分不换装", ref failures, ref total);
            CheckTrue(!EquipPolicy.IsBetterApparel(55f, 50f, 10f),
                "恰好10%提升不换装（严格大于）", ref failures, ref total);
            CheckTrue(EquipPolicy.IsBetterApparel(55.1f, 50f, 10f),
                "超过10%提升换装", ref failures, ref total);

            // 战斗档案阈值 15%（B4）：同一分数在 10% 档可换、在 15% 档不可换
            CheckTrue(!EquipPolicy.IsBetterApparel(57f, 50f, 15f),
                "战斗档案14%提升不换装", ref failures, ref total);
            CheckTrue(EquipPolicy.IsBetterApparel(57.6f, 50f, 15f),
                "战斗档案超过15%才换装", ref failures, ref total);

            // 边界：当前无装备 / 候选为 0
            CheckTrue(EquipPolicy.IsBetterApparel(1f, 0f, 15f),
                "当前无装备时任意正分都换", ref failures, ref total);
            CheckTrue(!EquipPolicy.IsBetterApparel(0f, 80f, 0f),
                "候选0分不换装", ref failures, ref total);
            return failures;
        }

        private static void CheckFloat(float actual, float expected, float tolerance, string name,
            ref int failures, ref int total)
        {
            total++;
            if (Math.Abs(actual - expected) > tolerance)
            {
                failures++;
                Console.WriteLine($"  FAIL {name}: expected {expected}, actual {actual}");
            }
        }

        private static void CheckTrue(bool condition, string name, ref int failures, ref int total)
        {
            total++;
            if (!condition)
            {
                failures++;
                Console.WriteLine($"  FAIL {name}");
            }
        }
    }
}
