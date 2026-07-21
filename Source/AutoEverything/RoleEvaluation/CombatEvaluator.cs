using RimWorld;
using Verse;
using AutoEverything.Core;

namespace AutoEverything.RoleEvaluation
{
    /// <summary>
    /// 殖民者战斗价值评估器：计算战斗价值分、评级档次、综合价值评分。
    ///
    /// 设计目的：
    /// - 把"战斗价值"离散化为 SSS/SS/S/A/B/C/D/X 八档，便于玩家直观判断优先级
    /// - 战斗价值分用于精确排序（同档内精排）
    /// - 命中自定义评级的 Pawn 不走公式，直接采用对应档次的代表分
    ///
    /// 战斗价值分 = (射击等级×兴趣乘数 + 近战等级×兴趣乘数) × 技能权重 + Σ特质加分
    /// 兴趣乘数：无火 1.0，单火 1.5，双火 2.0
    /// </summary>
    public static class CombatEvaluator
    {
        // 自定义评级档次对应的"代表分"，用于排序时与公式计算值同尺度比较
        // 设计：自动公式下 D≈0~10，B≈10~30，A≈30~50，S≈50~80，SS≈80~95，SSS≈95~110
        // 自定义评级分数取每档中段：D=5, C=15, B=25, A=50, S=80, SS=95, SSS=110, X=-1
        // 这样自定义 SSS 档总能排到自动公式的 SSS 档之前（同档自定义优先）
        private static readonly float[] tierRepresentativeScore = new float[]
        {
            -1f,   // X
            5f,    // D
            15f,   // C
            25f,   // B
            50f,   // A
            80f,   // S
            95f,   // SS
            110f   // SSS
        };

        // 9 大核心技能：评级 Major/Minor 计数与综合价值评分共用
        // 包含 Shooting/Melee（战斗）+ Social（社交）+ 6 大专业工作技能
        private static readonly SkillDef[] coreSkills =
        {
            SkillDefOf.Shooting, SkillDefOf.Melee, SkillDefOf.Social,
            SkillDefOf.Crafting, SkillDefOf.Construction, SkillDefOf.Artistic,
            SkillDefOf.Cooking, SkillDefOf.Plants, SkillDefOf.Mining
        };

        // 6 大专业工作技能：工作狂+神经质系列评级判定使用（coreSkills 子集，排除 Shooting/Melee/Social）
        private static readonly SkillDef[] workSkills =
        {
            SkillDefOf.Crafting, SkillDefOf.Construction, SkillDefOf.Artistic,
            SkillDefOf.Cooking, SkillDefOf.Plants, SkillDefOf.Mining
        };

        // TraitDef 查询统一由 TraitDefCache 提供（集中管理，避免重复定义）
        // Brawler（格斗者）是原生 DefOf 始终存在，直接引用 TraitDefOf.Brawler

        /// <summary>
        /// 计算 Pawn 的战斗价值分（用于评级排序与高价值标记判定）。
        /// 公式：战斗价值 = (射击等级×射击兴趣乘数 + 近战等级×近战兴趣乘数) × 技能权重 + Σ特质加分
        /// 兴趣乘数、技能权重、特质加分均可在面板上由玩家调整。
        /// 命中自定义评级的 Pawn 不走公式，直接采用对应档次的代表分（保证排序稳定）。
        /// </summary>
        public static float ComputeCombatValue(Pawn pawn)
        {
            if (pawn == null) return 0f;

            // 命中自定义评级：不走公式，直接采用代表分（加 0.5 微量偏向，让自定义档略优先于同档自动）
            if (AESettings.TryGetCustomTier(GetPawnLookupName(pawn), out CombatTier customTier))
            {
                return tierRepresentativeScore[(int)customTier] + 0.5f;
            }

            // 配偶评级豁免：与 S+ 配偶结婚的殖民者，用 S 代表分保证排序一致
            if (HasSpouseTierAtLeast(pawn, CombatTier.S))
            {
                return tierRepresentativeScore[(int)CombatTier.S] + 0.5f;
            }

            if (pawn.skills == null) return 0f;

            float total = 0f;
            SkillRecord shooting = pawn.skills.GetSkill(SkillDefOf.Shooting);
            SkillRecord melee = pawn.skills.GetSkill(SkillDefOf.Melee);

            if (shooting != null)
                total += shooting.Level * GetPassionMult(shooting.passion);

            if (melee != null)
                total += melee.Level * GetPassionMult(melee.passion);

            // 技能整体权重
            total *= AESettings.cvSkillWeight;

            // 特质加分
            // Tough：减伤 50%，对所有战斗均有价值（不在原生 DefOf 中，需 null 检查）
            // ShootingAccuracy degree=-1 是乱开枪（精度大幅下降）
            // ShootingAccuracy degree=+1 是冷枪手（精度提升但冷却慢）
            if (pawn.story?.traits != null)
            {
                if (TraitDefCache.Tough != null && pawn.story.traits.HasTrait(TraitDefCache.Tough))
                    total += AESettings.cvToughBonus;

                if (TraitDefCache.ShootingAccuracy != null)
                {
                    int shootingAccDegree = pawn.story.traits.DegreeOfTrait(TraitDefCache.ShootingAccuracy);
                    if (shootingAccDegree < 0)
                        total += AESettings.cvTriggerHappyPenalty;
                    else if (shootingAccDegree > 0)
                        total += AESettings.cvCarefulShooterBonus;
                }
            }

            return total;
        }

        private static float GetPassionMult(Passion passion)
        {
            // 兼容 VSE：通过 PassionTier 统一处理 6 种 passion 类型
            var tier = PassionHelper.GetPassionTier(passion);
            switch (tier)
            {
                case PassionHelper.PassionTier.Apathy:
                    return AESettings.cvPassionNoneMult * 0.5f;
                case PassionHelper.PassionTier.Minor:
                    return AESettings.cvPassionMinorMult;
                case PassionHelper.PassionTier.Major:
                    return AESettings.cvPassionMajorMult;
                case PassionHelper.PassionTier.Critical:
                    return AESettings.cvPassionMajorMult * 1.5f;
                default: // None
                    return AESettings.cvPassionNoneMult;
            }
        }

        /// <summary>
        /// 计算 Pawn 的战斗价值档次（用于 DEBUG 显示与重配优先级离散判断）。
        /// 档次与战斗价值公式协同工作：档次用于人眼可读的分级展示，
        /// 战斗价值分数用于精确排序。
        /// 命中自定义评级的 Pawn 直接采用指定档次，跳过自动判定。
        /// </summary>
        public static CombatTier GetCombatTier(Pawn pawn)
        {
            if (pawn == null) return CombatTier.X;

            // 自定义评级优先：玩家可在面板上指定档次，覆盖自动判定
            if (AESettings.TryGetCustomTier(GetPawnLookupName(pawn), out CombatTier customTier))
                return customTier;

            return GetSystemTier(pawn);
        }

        /// <summary>
        /// 计算系统评级（含配偶豁免，不含自定义评级覆盖）。
        /// 用于评级标签显示：玩家手动设置的自定义评级不在 Nick 上重复显示，
        ///   但配偶豁免属于系统自动评级，需同步反映。
        /// </summary>
        public static CombatTier GetSystemTier(Pawn pawn)
        {
            if (pawn == null) return CombatTier.X;

            CombatTier autoTier = GetAutoCombatTier(pawn);

            // 配偶评级豁免：与 S+ 人员结婚的殖民者，评级至少为 S（不降级 SS/SSS）
            // 用 GetAutoCombatTier 计算配偶评级，避免递归（A 配偶 B，B 配偶 A）
            if (autoTier < CombatTier.S && HasSpouseTierAtLeast(pawn, CombatTier.S))
            {
                return CombatTier.S;
            }

            return autoTier;
        }

        /// <summary>
        /// 计算 Pawn 的综合价值评分，用于同档内精排。
        /// 评分由三部分组成（玩家可读，单位为"价值分"）：
        ///   1. 特质数量：每条特质 +5 分（玩家培养投入越多价值越高）
        ///   2. 兴趣总分：9 大核心技能求和，Major=2, Minor=1, None=0
        ///   3. 技能等级总分：9 大核心技能 Level 求和
        ///
        /// 设计意图：评级 CombatTier 是离散分级（S/A/B/C/D），
        ///   但同档内仍有差异——同为 S 档，Tough+Melee 双火 与 双火带特殊天赋
        ///   价值并不相同；该分数提供档内精排依据。
        ///   排序时先按 CombatTier 降序，同档内再用此分数精排。
        /// </summary>
        public static float ComputePawnValueScore(Pawn pawn)
        {
            if (pawn == null) return 0f;

            // 命中自定义评级：用代表分 +0.5，让自定义档略优先于同档自动
            if (AESettings.TryGetCustomTier(GetPawnLookupName(pawn), out CombatTier customTier))
            {
                return tierRepresentativeScore[(int)customTier] + 0.5f;
            }

            // 配偶评级豁免：与 S+ 配偶结婚的殖民者，用 S 代表分保证排序一致
            if (HasSpouseTierAtLeast(pawn, CombatTier.S))
            {
                return tierRepresentativeScore[(int)CombatTier.S] + 0.5f;
            }

            float score = 0f;

            // 1. 特质数量：每条 +5
            if (pawn.story?.traits != null)
            {
                // 遍历 traits.allTraits 是 O(traits.Count)，通常 ≤3 条
                int traitCount = pawn.story.traits.allTraits.Count;
                score += traitCount * 5f;
            }

            if (pawn.skills == null) return score;

            // 2 & 3. 兴趣总分 + 技能等级总分（9 大核心技能）
            for (int i = 0; i < coreSkills.Length; i++)
            {
                AddSkillScore(pawn, coreSkills[i], ref score);
            }

            return score;
        }

        /// <summary>
        /// 累加单技能的分数到 score：
        ///   兴趣分：Major=2, Minor=1, None=0
        ///   等级分：直接加 Level（0-20）
        /// </summary>
        private static void AddSkillScore(Pawn pawn, SkillDef skillDef, ref float score)
        {
            SkillRecord s = pawn.skills?.GetSkill(skillDef);
            if (s == null) return;
            // 兴趣分：按 tier 累加（兼容 VSE：Natural=2 等同双火；Critical=3 最高；Apathy 不加分）
            var tier = PassionHelper.GetPassionTier(s.passion);
            switch (tier)
            {
                case PassionHelper.PassionTier.Minor: score += 1f; break;
                case PassionHelper.PassionTier.Major: score += 2f; break;
                case PassionHelper.PassionTier.Critical: score += 3f; break;
                    // Apathy 与 None 不加分
            }
            // 等级分
            score += s.Level;
        }

        /// <summary>
        /// 获取自动计算的战斗价值档次（忽略自定义评级覆盖）。
        /// 供面板显示与"自动档"对比使用。
        ///
        /// 新评级规则（三大维度取最高档 + 原S条件4/5 + A/B + 降档）：
        ///   SSS：顶级组合
        ///     1. 乱开枪 + 坚韧 + 射击双火
        ///     2. 坚韧 + 格斗双火 + 敏捷或格斗者
        ///     3. 工作狂（任意 degree）+ 神经质（任意 degree）+ 3 个专业工作双火
        ///   SS：强化组合
        ///     1. 乱开枪 + 射击双火
        ///     2. 坚韧 + 格斗双火
        ///     3. 工作狂（任意 degree）+ 神经质（任意 degree）+ 2 个专业工作双火
        ///   S：全局高价值
        ///     1. 乱开枪 + 射击单火
        ///     2. 坚韧 + 格斗有火（Minor 或 Major）
        ///     3. 工作狂（任意 degree）+ 神经质（任意 degree）+ 1 个专业工作双火
        ///     4. 拥有任一"特殊天赋"特质（博闻强识/开心果/极致体能/痴迷虚空/神秘学者/怪诞不经）
        ///     5. 沉鱼落雁（Beauty degree=2）+ 社交双火
        ///   A：≥ 2 个双 Major + ≥ 1 个 Minor 以上
        ///   B：≥ 1 个双 Major + ≥ 2 个 Minor 以上（合计 ≥ 3）
        ///   C：其他情况
        ///   D：有负面特质且原档 > D 时降一档（纵火狂/脑子慢/脆弱/工作懒惰/工作怠惰）
        ///   X：无法从事暴力活动（先于一切判定，不受降档影响）
        ///
        /// 用户决策（2026-07-21）：「神经质+工作狂」组合应给高评级，
        /// degree 要求从 == 2 放宽到 >= 1（含 degree=1 努力/轻度神经质 + degree=2 勤奋/严重神经质）。
        /// </summary>
        public static CombatTier GetAutoCombatTier(Pawn pawn)
        {
            if (pawn == null) return CombatTier.X;
            if (pawn.WorkTagIsDisabled(WorkTags.Violent)) return CombatTier.X;
            if (pawn.skills == null) return CombatTier.X;

            return EvaluateAutoTierCore(CollectTierInput(pawn));
        }

        /// <summary>
        /// 评级输入参数（从 Pawn 收集，用于纯逻辑核心 EvaluateAutoTierCore）。
        /// 提取为 struct 便于单元测试：测试只需构造输入，无需 mock Pawn。
        /// </summary>
        internal struct TierEvaluationInput
        {
            // 特质状态
            public bool IsTriggerHappy;   // 乱开枪 ShootingAccuracy degree=-1
            public bool IsTough;           // 坚韧 Tough
            public bool IsNimble;          // 敏捷 Nimble
            public bool IsBrawler;         // 格斗者 Brawler
            // 工作狂/神经质：放宽到 degree >= 1（含 degree=1 努力/轻度神经质 + degree=2 勤奋/严重神经质）
            // 用户决策（2026-07-21）：「神经质+工作狂」组合应给高评级，放宽 degree 要求
            public bool HasIndustrious;    // 工作狂 Industriousness degree >= 1（努力/勤奋）
            public bool HasNeurotic;       // 神经质 Neurotic degree >= 1（轻度/严重）
            public bool Beauty2;           // 沉鱼落雁 Beauty degree=2
            public bool HasSpecialTalent;  // 特殊天赋特质之一
            public bool HasNegativeTrait;  // 负面特质（用于降档）

            // 技能兴趣状态
            public bool ShootingMajor;
            public bool ShootingMinor;
            public bool MeleeMajor;
            public bool MeleeMinor;
            public bool SocialMajor;

            // 统计计数
            public int MajorCount;   // 9 大技能 Major 总数
            public int MinorCount;   // 9 大技能 Minor 总数
            public int WorkMajors;   // 6 大专业工作技能 Major 数
        }

        /// <summary>
        /// 评级纯逻辑核心（不依赖 Pawn，便于单元测试）。
        /// 从 C 档开始，按三大维度取最高档，再处理特殊天赋/沉鱼落雁/A-B 判定/负面特质降档。
        /// X 档（无法从事暴力活动）在 GetAutoCombatTier 入口判定，不在此函数处理。
        /// </summary>
        internal static CombatTier EvaluateAutoTierCore(TierEvaluationInput input)
        {
            CombatTier tier = CombatTier.C;

            // 维度1（乱开枪系列）：triggerHappy + shooting
            //   SSS: triggerHappy + tough + shootingMajor
            //   SS:  triggerHappy + shootingMajor
            //   S:   triggerHappy + shootingMinor
            if (input.IsTriggerHappy)
            {
                if (input.IsTough && input.ShootingMajor)
                    tier = MaxTier(tier, CombatTier.SSS);
                else if (input.ShootingMajor)
                    tier = MaxTier(tier, CombatTier.SS);
                else if (input.ShootingMinor)
                    tier = MaxTier(tier, CombatTier.S);
            }

            // 维度2（坚韧格斗系列）：tough + melee
            //   SSS: tough + meleeMajor + (nimble || brawler)
            //   SS:  tough + meleeMajor
            //   S:   tough + meleeAnyPassion
            bool meleeAnyPassion = input.MeleeMajor || input.MeleeMinor;
            if (input.IsTough)
            {
                if (input.MeleeMajor && (input.IsNimble || input.IsBrawler))
                    tier = MaxTier(tier, CombatTier.SSS);
                else if (input.MeleeMajor)
                    tier = MaxTier(tier, CombatTier.SS);
                else if (meleeAnyPassion)
                    tier = MaxTier(tier, CombatTier.S);
            }

            // 维度3（工作狂神经质系列）：industrious AND neurotic + workMajors
            //   用户决策（2026-07-21）：「神经质+工作狂」组合应给高评级
            //   degree 要求放宽到 >= 1（含 degree=1 努力/轻度神经质 + degree=2 勤奋/严重神经质）
            //   SSS: hasIndustrious && hasNeurotic && workMajors >= 3
            //   SS:  hasIndustrious && hasNeurotic && workMajors >= 2
            //   S:   hasIndustrious && hasNeurotic && workMajors >= 1
            if (input.HasIndustrious && input.HasNeurotic)
            {
                if (input.WorkMajors >= 3)
                    tier = MaxTier(tier, CombatTier.SSS);
                else if (input.WorkMajors >= 2)
                    tier = MaxTier(tier, CombatTier.SS);
                else if (input.WorkMajors >= 1)
                    tier = MaxTier(tier, CombatTier.S);
            }

            // 原 S 条件 4：拥有特殊天赋特质之一
            //   博闻强识 TooSmart / 开心果 Joyous / 极致体能 BodyMastery
            //   痴迷虚空 VoidFascination / 神秘学者 Occultist / 怪诞不经 Disturbing
            if (input.HasSpecialTalent)
            {
                tier = MaxTier(tier, CombatTier.S);
            }

            // 原 S 条件 5：沉鱼落雁（Beauty degree=2）+ Social 双火
            if (input.Beauty2 && input.SocialMajor)
            {
                tier = MaxTier(tier, CombatTier.S);
            }

            // A/B 判定：仅在三大维度+原S条件均未触达（tier==C）时进行
            if (tier == CombatTier.C)
            {
                // A：≥ 2 个双 Major + ≥ 1 个单 Minor
                if (input.MajorCount >= 2 && (input.MajorCount + input.MinorCount) >= 3)
                {
                    tier = CombatTier.A;
                }
                // B：≥ 1 个双 Major + ≥ 2 个单 Minor（合计 ≥ 3）
                else if (input.MajorCount >= 1 && (input.MajorCount + input.MinorCount) >= 3)
                {
                    tier = CombatTier.B;
                }
            }

            // 降档：有负面特质且 tier > D 时降一档
            //   纵火狂 Pyromaniac / 脑子慢 SlowLearner / 脆弱 Wimp
            //   工作懒惰 Industriousness degree=-1 / 工作怠惰 Industriousness degree=-2
            // D 不再降；X 先于一切判定不受影响
            if (tier > CombatTier.D && input.HasNegativeTrait)
            {
                tier = (CombatTier)(tier - 1);
            }

            return tier;
        }

        /// <summary>
        /// 从 Pawn 收集评级所需的所有输入参数。
        /// VSE 兼容：Major 判定用 tier >= Major（含 Natural/Critical），
        /// Minor 判定用 tier == Minor（不含 Major 及以上，避免双计数）。
        /// </summary>
        private static TierEvaluationInput CollectTierInput(Pawn pawn)
        {
            TierEvaluationInput input = default;

            input.MajorCount = CountPassions(pawn, PassionHelper.PassionTier.Major, atLeast: true, coreSkills);
            input.MinorCount = CountPassions(pawn, PassionHelper.PassionTier.Minor, atLeast: false, coreSkills);

            input.ShootingMajor = IsPassionAtLeast(pawn, SkillDefOf.Shooting, PassionHelper.PassionTier.Major);
            input.ShootingMinor = IsPassionExactly(pawn, SkillDefOf.Shooting, PassionHelper.PassionTier.Minor);
            input.MeleeMajor = IsPassionAtLeast(pawn, SkillDefOf.Melee, PassionHelper.PassionTier.Major);
            input.MeleeMinor = IsPassionExactly(pawn, SkillDefOf.Melee, PassionHelper.PassionTier.Minor);
            input.SocialMajor = IsPassionAtLeast(pawn, SkillDefOf.Social, PassionHelper.PassionTier.Major);

            bool hasTraits = pawn.story?.traits != null;
            input.IsTough = hasTraits && TraitDefCache.Tough != null && pawn.story.traits.HasTrait(TraitDefCache.Tough);
            input.IsNimble = hasTraits && TraitDefCache.Nimble != null && pawn.story.traits.HasTrait(TraitDefCache.Nimble);
            input.IsBrawler = hasTraits && pawn.story.traits.HasTrait(TraitDefOf.Brawler);
            if (hasTraits && TraitDefCache.ShootingAccuracy != null)
            {
                input.IsTriggerHappy = pawn.story.traits.DegreeOfTrait(TraitDefCache.ShootingAccuracy) == -1;
            }
            input.HasIndustrious = hasTraits && TraitDefCache.Industriousness != null
                                  && pawn.story.traits.DegreeOfTrait(TraitDefCache.Industriousness) >= 1;
            input.HasNeurotic = hasTraits && TraitDefCache.Neurotic != null
                                && pawn.story.traits.DegreeOfTrait(TraitDefCache.Neurotic) >= 1;
            input.Beauty2 = hasTraits && TraitDefCache.Beauty != null
                            && pawn.story.traits.DegreeOfTrait(TraitDefCache.Beauty) == 2;

            input.WorkMajors = CountWorkMajors(pawn);
            input.HasSpecialTalent = HasSpecialTalentTrait(pawn);
            input.HasNegativeTrait = HasNegativeTrait(pawn);

            return input;
        }

        /// <summary>
        /// 统计 Pawn 在指定技能集合中 tier 达到/恰好等于指定层级的数量。
        /// atLeast=true：含该层级及以上（用于 Major 计数，含 Natural/Critical）；
        /// atLeast=false：仅该层级（用于 Minor 计数，不含 Major 及以上避免双计数）。
        /// </summary>
        private static int CountPassions(Pawn pawn, PassionHelper.PassionTier targetTier, bool atLeast, SkillDef[] skills)
        {
            int count = 0;
            for (int i = 0; i < skills.Length; i++)
            {
                SkillRecord s = pawn.skills?.GetSkill(skills[i]);
                if (s == null) continue;
                var tier = PassionHelper.GetPassionTier(s.passion);
                bool hit = atLeast ? (int)tier >= (int)targetTier : tier == targetTier;
                if (hit) count++;
            }
            return count;
        }

        /// <summary>
        /// 判定技能 tier 是否达到指定层级（含该层级及以上）。
        /// 用于 Major 判定：tier >= Major 含 Natural/Critical。
        /// </summary>
        private static bool IsPassionAtLeast(Pawn pawn, SkillDef skillDef, PassionHelper.PassionTier minTier)
        {
            SkillRecord s = pawn.skills?.GetSkill(skillDef);
            return s != null && (int)PassionHelper.GetPassionTier(s.passion) >= (int)minTier;
        }

        /// <summary>
        /// 判定技能 tier 是否恰好等于指定层级。
        /// 用于 Minor 判定：tier == Minor，不含 Major 及以上。
        /// </summary>
        private static bool IsPassionExactly(Pawn pawn, SkillDef skillDef, PassionHelper.PassionTier tier)
        {
            SkillRecord s = pawn.skills?.GetSkill(skillDef);
            return s != null && PassionHelper.GetPassionTier(s.passion) == tier;
        }

        /// <summary>
        /// 返回两档中较高的一档（枚举值大的更高：SSS>SS>S>A>B>C>D>X）。
        /// 用于三大维度取最高档，避免互斥 if-else 提前退出。
        /// </summary>
        private static CombatTier MaxTier(CombatTier a, CombatTier b)
        {
            return (int)a >= (int)b ? a : b;
        }

        /// <summary>
        /// 统计 6 大专业工作技能的双火（Major）数量。
        /// 用于工作狂+神经质系列的 S/SS/SSS 判定。
        /// VSE 兼容：tier >= Major 含 Natural/Critical。
        /// </summary>
        private static int CountWorkMajors(Pawn pawn)
        {
            return CountPassions(pawn, PassionHelper.PassionTier.Major, atLeast: true, workSkills);
        }

        private static bool HasSpecialTalentTrait(Pawn pawn)
        {
            if (pawn.story?.traits == null) return false;
            if (TraitDefCache.TooSmart != null && pawn.story.traits.HasTrait(TraitDefCache.TooSmart)) return true;
            if (TraitDefCache.Joyous != null && pawn.story.traits.HasTrait(TraitDefCache.Joyous)) return true;
            if (TraitDefCache.BodyMastery != null && pawn.story.traits.HasTrait(TraitDefCache.BodyMastery)) return true;
            if (TraitDefCache.VoidFascination != null && pawn.story.traits.HasTrait(TraitDefCache.VoidFascination)) return true;
            if (TraitDefCache.Occultist != null && pawn.story.traits.HasTrait(TraitDefCache.Occultist)) return true;
            if (TraitDefCache.Disturbing != null && pawn.story.traits.HasTrait(TraitDefCache.Disturbing)) return true;
            return false;
        }

        /// <summary>
        /// 检查是否拥有"负面"特质之一（用于 D 档判定）。
        /// 纵火狂 Pyromaniac / 脑子慢 SlowLearner / 脆弱 Wimp
        /// 工作懒惰 Industriousness degree=-1 / 工作怠惰 Industriousness degree=-2
        /// </summary>
        private static bool HasNegativeTrait(Pawn pawn)
        {
            if (pawn.story?.traits == null) return false;
            if (TraitDefCache.Pyromaniac != null && pawn.story.traits.HasTrait(TraitDefCache.Pyromaniac)) return true;
            if (TraitDefCache.SlowLearner != null && pawn.story.traits.HasTrait(TraitDefCache.SlowLearner)) return true;
            if (TraitDefCache.Wimp != null && pawn.story.traits.HasTrait(TraitDefCache.Wimp)) return true;
            if (TraitDefCache.Industriousness != null)
            {
                int deg = pawn.story.traits.DegreeOfTrait(TraitDefCache.Industriousness);
                if (deg == -1 || deg == -2) return true;
            }
            return false;
        }

        /// <summary>
        /// 获取用于自定义评级字典的 Pawn 查询键。
        /// 优先使用 LabelShort（如 "王五"），保留昵称与原名映射。
        /// </summary>
        public static string GetPawnLookupName(Pawn pawn)
        {
            if (pawn == null) return string.Empty;
            // LabelShort 包含昵称，对玩家可读且稳定
            // 但若玩家点了"全局人物评级"按钮把 Nick 改为 "S#王五" 格式，
            //   需剥离评级前缀返回纯净名 "王五"，否则：
            //   1) customTierMap 查询失配（玩家设置时用的是原名）
            //   2) 面板拼接会变成 "S#S#王五" 双重前缀
            return TierTagHelper.Strip(pawn.LabelShort ?? string.Empty);
        }

        /// <summary>
        /// 获取 Pawn 的存活配偶（Spouse 关系），无配偶或配偶已死返回 null。
        /// </summary>
        private static Pawn GetSpouse(Pawn pawn)
        {
            if (pawn.relations == null) return null;
            var directRelations = pawn.relations.DirectRelations;
            for (int i = 0; i < directRelations.Count; i++)
            {
                DirectPawnRelation rel = directRelations[i];
                if (rel.def == PawnRelationDefOf.Spouse && rel.otherPawn != null && !rel.otherPawn.Dead)
                    return rel.otherPawn;
            }
            return null;
        }

        /// <summary>
        /// 检查 Pawn 的配偶（自动评级）是否 >= 指定档次。
        /// 用 GetAutoCombatTier 计算配偶评级，避免 GetCombatTier 递归（A 配偶 B，B 配偶 A 死循环）。
        /// </summary>
        private static bool HasSpouseTierAtLeast(Pawn pawn, CombatTier minTier)
        {
            Pawn spouse = GetSpouse(pawn);
            return spouse != null && GetAutoCombatTier(spouse) >= minTier;
        }
    }
}
