using RimWorld;
using Verse;
using AutoEverything.Core;

namespace AutoEverything.RoleEvaluation
{
    /// <summary>
    /// 殖民者评级评估器：计算 CombatTier 档次与同档精排所用的核心技能等级和。
    ///
    /// 设计目的：
    /// - 把全局价值离散化为 SSS/SS/S/A/B/C/D/X 八档，便于玩家直观判断优先级
    /// - 档内精排只用 9 大核心技能等级和，不再维护独立的价值公式
    /// - 命中自定义评级的 Pawn 不参与自动判定，直接采用指定档次
    /// </summary>
    public static class CombatEvaluator
    {
        // 9 大核心技能：评级 Major/Minor 计数与同档精排共用
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
        /// 计算 Pawn 的评级档次（用于 DEBUG 显示与重配优先级离散判断）。
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

            // 配偶评级豁免：与高价值人员结婚的殖民者评级至少为豁免档位（不降级更高档）
            // 用 GetAutoCombatTier 计算配偶评级，避免递归（A 配偶 B，B 配偶 A）
            CombatTier spouseTier = AESettings.spouseExemptionTier;
            if (AESettings.spouseExemptionEnabled
                && autoTier < spouseTier
                && HasSpouseTierAtLeast(pawn, spouseTier))
            {
                return spouseTier;
            }

            return autoTier;
        }

        /// <summary>
        /// 计算 Pawn 在 9 大核心技能上的等级总和，用于同档内精排。
        /// 档内排序只反映培养深度，不引入独立的价值公式。
        /// </summary>
        public static int ComputeCoreSkillLevelSum(Pawn pawn)
        {
            if (pawn?.skills == null) return 0;

            int sum = 0;
            for (int i = 0; i < coreSkills.Length; i++)
            {
                SkillRecord s = pawn.skills.GetSkill(coreSkills[i]);
                if (s != null) sum += s.Level;
            }
            return sum;
        }

        /// <summary>
        /// 获取自动计算的评级档次（忽略自定义评级覆盖）。
        /// 供面板显示与"自动档"对比使用。
        ///
        /// 新评级规则（三大维度取最高档 + 原S条件4/5 + A/B）：
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
        ///     3. 工作狂（任意 degree）+ 神经质（任意 degree）（组合即 S，无需工作双火）
        ///     4. 拥有任一"特殊天赋"特质（博闻强识/开心果/极致体能/痴迷虚空/神秘学者/怪诞不经）
        ///     5. 沉鱼落雁（Beauty degree=2）+ 社交双火
        ///   A：≥ 2 个双 Major + ≥ 1 个 Minor 以上
        ///   B：≥ 1 个双 Major + ≥ 2 个 Minor 以上（合计 ≥ 3）
        ///   C：其他情况
        ///   D：自动评级不产生此档（仅供玩家自定义评级使用）
        ///   X：无法从事暴力活动（先于一切判定）
        ///
        /// 用户决策（2026-07-21）：「神经质+工作狂」组合应给高评级，
        /// degree 要求从 == 2 放宽到 >= 1（含 degree=1 努力/轻度神经质 + degree=2 勤奋/严重神经质）。
        ///
        /// 用户决策（2026-07-26）：移除负面特质降档逻辑，S/SS/SSS 一律按命中档次标记，
        /// 不再因纵火狂/脑子慢/脆弱/工作懒惰怠惰等负面特质降一档。
        /// </summary>
        public static CombatTier GetAutoCombatTier(Pawn pawn)
        {
            if (pawn == null) return CombatTier.X;
            if (pawn.WorkTagIsDisabled(WorkTags.Violent)) return CombatTier.X;
            if (pawn.skills == null) return CombatTier.X;

            return EvaluateAutoTierCore(CollectTierInput(pawn), TierRules.FromSettings());
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
        /// 评级判定阈值集合（纯值类型，不依赖 ModSettings）。
        /// 生产路径由 <see cref="FromSettings"/> 从【自动】面板读取；
        /// 单元测试路径使用 <see cref="Default"/>，避免测试进程加载 Assembly-CSharp。
        /// </summary>
        internal struct TierRules
        {
            public int AMajorMin;
            public int APassionTotal;
            public int BMajorMin;
            public int BPassionTotal;
            public int SSWorkMajors;
            public int SSSWorkMajors;
            public bool SpecialTalentToS;
            public bool BeautySocialToS;
            // 逐条规则开关：false 表示该条组合不参与评级（面板"评级判定规则表"勾选框）
            public bool TriggerHappySSS;
            public bool TriggerHappySS;
            public bool TriggerHappyS;
            public bool ToughMeleeSSS;
            public bool ToughMeleeSS;
            public bool ToughMeleeS;
            public bool WorkaholicSSS;
            public bool WorkaholicSS;
            public bool WorkaholicS;

            public static TierRules Default
            {
                get
                {
                    TierRules rules = new TierRules();
                    rules.AMajorMin = 2;
                    rules.APassionTotal = 3;
                    rules.BMajorMin = 1;
                    rules.BPassionTotal = 3;
                    rules.SSWorkMajors = 2;
                    rules.SSSWorkMajors = 3;
                    rules.SpecialTalentToS = true;
                    rules.BeautySocialToS = true;
                    rules.TriggerHappySSS = true;
                    rules.TriggerHappySS = true;
                    rules.TriggerHappyS = true;
                    rules.ToughMeleeSSS = true;
                    rules.ToughMeleeSS = true;
                    rules.ToughMeleeS = true;
                    rules.WorkaholicSSS = true;
                    rules.WorkaholicSS = true;
                    rules.WorkaholicS = true;
                    return rules;
                }
            }

            /// <summary>读取【自动】面板当前配置（仅在游戏进程内调用）。</summary>
            public static TierRules FromSettings()
            {
                TierRules rules = new TierRules();
                rules.AMajorMin = AESettings.tierAMajorMin;
                rules.APassionTotal = AESettings.tierAPassionTotal;
                rules.BMajorMin = AESettings.tierBMajorMin;
                rules.BPassionTotal = AESettings.tierBPassionTotal;
                rules.SSWorkMajors = AESettings.tierSSWorkMajors;
                rules.SSSWorkMajors = AESettings.tierSSSWorkMajors;
                rules.SpecialTalentToS = AESettings.tierSpecialTalentToS;
                rules.BeautySocialToS = AESettings.tierBeautySocialToS;
                rules.TriggerHappySSS = AESettings.tierRuleTriggerHappySSS;
                rules.TriggerHappySS = AESettings.tierRuleTriggerHappySS;
                rules.TriggerHappyS = AESettings.tierRuleTriggerHappyS;
                rules.ToughMeleeSSS = AESettings.tierRuleToughMeleeSSS;
                rules.ToughMeleeSS = AESettings.tierRuleToughMeleeSS;
                rules.ToughMeleeS = AESettings.tierRuleToughMeleeS;
                rules.WorkaholicSSS = AESettings.tierRuleWorkaholicSSS;
                rules.WorkaholicSS = AESettings.tierRuleWorkaholicSS;
                rules.WorkaholicS = AESettings.tierRuleWorkaholicS;
                return rules;
            }
        }

        /// <summary>
        /// 评级纯逻辑核心（不依赖 Pawn，便于单元测试）。
        /// 从 C 档开始，按三大维度取最高档，再处理特殊天赋/沉鱼落雁/A-B 判定。
        /// X 档（无法从事暴力活动）在 GetAutoCombatTier 入口判定，不在此函数处理。
        /// 注：负面特质不再触发降档（用户决策 2026-07-26），S/SS/SSS 一律按命中档次返回。
        /// </summary>
        internal static CombatTier EvaluateAutoTierCore(TierEvaluationInput input)
        {
            return EvaluateAutoTierCore(input, TierRules.Default);
        }

        /// <summary>
        /// 评级纯逻辑核心（可指定阈值）。阈值来自【自动】面板时由上层传入。
        /// </summary>
        internal static CombatTier EvaluateAutoTierCore(TierEvaluationInput input, TierRules rules)
        {
            CombatTier tier = CombatTier.C;

            // 维度1（乱开枪系列）：triggerHappy + shooting
            //   SSS: triggerHappy + tough + shootingMajor
            //   SS:  triggerHappy + shootingMajor
            //   S:   triggerHappy + shootingMinor
            // 每条规则独立判定（勾选框关闭即跳过），MaxTier 保证高条件命中时低条件不反向覆盖
            if (input.IsTriggerHappy)
            {
                if (rules.TriggerHappySSS && input.IsTough && input.ShootingMajor)
                    tier = MaxTier(tier, CombatTier.SSS);
                if (rules.TriggerHappySS && input.ShootingMajor)
                    tier = MaxTier(tier, CombatTier.SS);
                if (rules.TriggerHappyS && input.ShootingMinor)
                    tier = MaxTier(tier, CombatTier.S);
            }

            // 维度2（坚韧格斗系列）：tough + melee
            //   SSS: tough + meleeMajor + (nimble || brawler)
            //   SS:  tough + meleeMajor
            //   S:   tough + meleeAnyPassion
            bool meleeAnyPassion = input.MeleeMajor || input.MeleeMinor;
            if (input.IsTough)
            {
                if (rules.ToughMeleeSSS && input.MeleeMajor && (input.IsNimble || input.IsBrawler))
                    tier = MaxTier(tier, CombatTier.SSS);
                if (rules.ToughMeleeSS && input.MeleeMajor)
                    tier = MaxTier(tier, CombatTier.SS);
                if (rules.ToughMeleeS && meleeAnyPassion)
                    tier = MaxTier(tier, CombatTier.S);
            }

            // 维度3（工作狂神经质系列）：industrious AND neurotic → 组合即 S，workMajors 决定升档
            //   用户决策（2026-07-21）：「神经质+工作狂」组合应给高评级（至少 S）
            //   degree 要求放宽到 >= 1（含 degree=1 努力/轻度神经质 + degree=2 勤奋/严重神经质）
            //   S:   hasIndustrious && hasNeurotic（组合即 S，无需 workMajors）
            //   SS:  hasIndustrious && hasNeurotic && workMajors >= 2
            //   SSS: hasIndustrious && hasNeurotic && workMajors >= 3
            //   修复（2026-07-21）：原实现把 S 档也要求 workMajors>=1，导致无工作双火的组合
            //     落入 A/B 判定路径（如 1 Major+2 Minor → B），违背"组合即 S"决策
            if (input.HasIndustrious && input.HasNeurotic)
            {
                if (rules.WorkaholicS)
                    tier = MaxTier(tier, CombatTier.S);
                if (rules.WorkaholicSS && input.WorkMajors >= rules.SSWorkMajors)
                    tier = MaxTier(tier, CombatTier.SS);
                if (rules.WorkaholicSSS && input.WorkMajors >= rules.SSSWorkMajors)
                    tier = MaxTier(tier, CombatTier.SSS);
            }

            // 原 S 条件 4：拥有特殊天赋特质之一（可在【自动】面板关闭）
            //   博闻强识 TooSmart / 开心果 Joyous / 极致体能 BodyMastery
            //   痴迷虚空 VoidFascination / 神秘学者 Occultist / 怪诞不经 Disturbing
            if (rules.SpecialTalentToS && input.HasSpecialTalent)
            {
                tier = MaxTier(tier, CombatTier.S);
            }

            // 原 S 条件 5：沉鱼落雁（Beauty degree=2）+ Social 双火（可在【自动】面板关闭）
            if (rules.BeautySocialToS && input.Beauty2 && input.SocialMajor)
            {
                tier = MaxTier(tier, CombatTier.S);
            }

            // A/B 判定：仅在三大维度+原S条件均未触达（tier==C）时进行
            // 阈值来自【自动】面板，默认 A：2 双火+合计 3；B：1 双火+合计 3
            if (tier == CombatTier.C)
            {
                int passionTotal = input.MajorCount + input.MinorCount;
                if (input.MajorCount >= rules.AMajorMin && passionTotal >= rules.APassionTotal)
                {
                    tier = CombatTier.A;
                }
                else if (input.MajorCount >= rules.BMajorMin && passionTotal >= rules.BPassionTotal)
                {
                    tier = CombatTier.B;
                }
            }

            // 注：原负面特质降档逻辑已移除（用户决策 2026-07-26）。
            //   S/SS/SSS 一律按命中档次返回，纵火狂/脑子慢/脆弱/工作懒惰怠惰不再降档。
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

            input.ShootingMajor = IsPassion(pawn, SkillDefOf.Shooting, PassionHelper.PassionTier.Major, atLeast: true);
            input.ShootingMinor = IsPassion(pawn, SkillDefOf.Shooting, PassionHelper.PassionTier.Minor, atLeast: false);
            input.MeleeMajor = IsPassion(pawn, SkillDefOf.Melee, PassionHelper.PassionTier.Major, atLeast: true);
            input.MeleeMinor = IsPassion(pawn, SkillDefOf.Melee, PassionHelper.PassionTier.Minor, atLeast: false);
            input.SocialMajor = IsPassion(pawn, SkillDefOf.Social, PassionHelper.PassionTier.Major, atLeast: true);

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
        /// 判定技能 tier 是否满足指定条件。
        /// atLeast=true：含该层级及以上（用于 Major 判定，含 Natural/Critical）。
        /// atLeast=false：仅该层级（用于 Minor 判定，不含 Major 及以上避免双计数）。
        /// 与 <see cref="CountPassions"/> 的 atLeast 参数语义一致。
        /// </summary>
        private static bool IsPassion(Pawn pawn, SkillDef skillDef, PassionHelper.PassionTier tier, bool atLeast)
        {
            SkillRecord s = pawn.skills?.GetSkill(skillDef);
            if (s == null) return false;
            var actual = PassionHelper.GetPassionTier(s.passion);
            return atLeast ? (int)actual >= (int)tier : actual == tier;
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
