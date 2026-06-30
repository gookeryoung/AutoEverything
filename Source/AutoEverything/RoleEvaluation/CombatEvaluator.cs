using RimWorld;
using Verse;
using AutoEverything.Core;

namespace AutoEverything.RoleEvaluation
{
    /// <summary>
    /// 殖民者战斗价值评估器：计算战斗价值分、评级档次、综合价值评分。
    ///
    /// 设计目的：
    /// - 把"战斗价值"离散化为 S/A/B/C/D/X 六档，便于玩家直观判断优先级
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

        // TraitDef 查询统一由 TraitDefCache 提供（集中管理，避免与 WeaponTraitScorer 重复定义）
        // Brawler（格斗者）是原生 DefOf 始终存在，直接引用 TraitDefOf.Brawler

        /// <summary>
        /// 计算 Pawn 的战斗价值分（用于副武器分配优先级与全局重配顺序）。
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
            switch (passion)
            {
                case Passion.Minor: return AESettings.cvPassionMinorMult;
                case Passion.Major: return AESettings.cvPassionMajorMult;
                default: return AESettings.cvPassionNoneMult;
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

            return GetAutoCombatTier(pawn);
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
        ///   护甲分配时先按 CombatTier 降序，同档内再用此分数精排。
        ///   武器分配不使用此分数，仍用 ComputeCombatValue（仅战斗维度）。
        /// </summary>
        public static float ComputePawnValueScore(Pawn pawn)
        {
            if (pawn == null) return 0f;

            // 命中自定义评级：用代表分 +0.5，让自定义档略优先于同档自动
            if (AESettings.TryGetCustomTier(GetPawnLookupName(pawn), out CombatTier customTier))
            {
                return tierRepresentativeScore[(int)customTier] + 0.5f;
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
            AddSkillScore(pawn, SkillDefOf.Shooting, ref score);
            AddSkillScore(pawn, SkillDefOf.Melee, ref score);
            AddSkillScore(pawn, SkillDefOf.Social, ref score);
            AddSkillScore(pawn, SkillDefOf.Crafting, ref score);
            AddSkillScore(pawn, SkillDefOf.Construction, ref score);
            AddSkillScore(pawn, SkillDefOf.Artistic, ref score);
            AddSkillScore(pawn, SkillDefOf.Cooking, ref score);
            AddSkillScore(pawn, SkillDefOf.Plants, ref score);
            AddSkillScore(pawn, SkillDefOf.Mining, ref score);

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
            // 兴趣分
            switch (s.passion)
            {
                case Passion.Minor: score += 1f; break;
                case Passion.Major: score += 2f; break;
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
        ///     3. 勤奋且严重神经质 + 3 个专业工作双火
        ///   SS：强化组合
        ///     1. 乱开枪 + 射击双火
        ///     2. 坚韧 + 格斗双火
        ///     3. 勤奋且严重神经质 + 2 个专业工作双火
        ///   S：全局高价值
        ///     1. 乱开枪 + 射击单火
        ///     2. 坚韧 + 格斗有火（Minor 或 Major）
        ///     3. 勤奋且严重神经质 + 1 个专业工作双火
        ///     4. 拥有任一"特殊天赋"特质（博闻强识/开心果/极致体能/痴迷虚空/神秘学者/怪诞不经）
        ///     5. 沉鱼落雁（Beauty degree=2）+ 社交双火
        ///   A：≥ 2 个双 Major + ≥ 1 个 Minor 以上
        ///   B：≥ 1 个双 Major + ≥ 2 个 Minor 以上（合计 ≥ 3）
        ///   C：其他情况
        ///   D：有负面特质且原档 > D 时降一档（纵火狂/脑子慢/脆弱/工作懒惰/工作怠惰）
        ///   X：无法从事暴力活动（先于一切判定，不受降档影响）
        /// </summary>
        public static CombatTier GetAutoCombatTier(Pawn pawn)
        {
            if (pawn == null) return CombatTier.X;

            // X：无法从事暴力活动（医疗特质 DisableViolent、未成年限制等）
            // 先于一切判定：X 档不受降档影响，避免给不能战斗的 Pawn 装武器
            if (pawn.WorkTagIsDisabled(WorkTags.Violent)) return CombatTier.X;

            if (pawn.skills == null) return CombatTier.X;

            // 统计所有兴趣的双 Major/Minor 数量（用于 A/B 判定）
            int majorCount = CountPassions(pawn, Passion.Major);
            int minorCount = CountPassions(pawn, Passion.Minor);

            // 取关键技能的 Passion 状态
            bool shootingMajor = IsPassion(pawn, SkillDefOf.Shooting, Passion.Major);
            bool shootingMinor = IsPassion(pawn, SkillDefOf.Shooting, Passion.Minor);
            bool meleeMajor = IsPassion(pawn, SkillDefOf.Melee, Passion.Major);
            bool meleeMinor = IsPassion(pawn, SkillDefOf.Melee, Passion.Minor);
            bool socialMajor = IsPassion(pawn, SkillDefOf.Social, Passion.Major);

            // 预计算特质状态（避免重复 DegreeOfTrait 调用）
            bool hasTraits = pawn.story?.traits != null;
            bool isTough = hasTraits && TraitDefCache.Tough != null && pawn.story.traits.HasTrait(TraitDefCache.Tough);
            bool isNimble = hasTraits && TraitDefCache.Nimble != null && pawn.story.traits.HasTrait(TraitDefCache.Nimble);
            bool isBrawler = hasTraits && pawn.story.traits.HasTrait(TraitDefOf.Brawler);
            bool isTriggerHappy = false;
            if (hasTraits && TraitDefCache.ShootingAccuracy != null)
            {
                isTriggerHappy = pawn.story.traits.DegreeOfTrait(TraitDefCache.ShootingAccuracy) == -1;
            }
            bool industrious2 = hasTraits && TraitDefCache.Industriousness != null
                                && pawn.story.traits.DegreeOfTrait(TraitDefCache.Industriousness) == 2;
            bool neurotic2 = hasTraits && TraitDefCache.Neurotic != null
                             && pawn.story.traits.DegreeOfTrait(TraitDefCache.Neurotic) == 2;
            bool beauty2 = hasTraits && TraitDefCache.Beauty != null
                           && pawn.story.traits.DegreeOfTrait(TraitDefCache.Beauty) == 2;

            int workMajors = CountWorkMajors(pawn);
            bool meleeAnyPassion = meleeMajor || meleeMinor;

            // 三大维度取最高档（MaxTier 累积，不互斥）
            CombatTier tier = CombatTier.C;

            // 维度1（乱开枪系列）：triggerHappy + shooting
            //   SSS: triggerHappy + tough + shootingMajor
            //   SS:  triggerHappy + shootingMajor
            //   S:   triggerHappy + shootingMinor
            if (isTriggerHappy)
            {
                if (isTough && shootingMajor)
                    tier = MaxTier(tier, CombatTier.SSS);
                else if (shootingMajor)
                    tier = MaxTier(tier, CombatTier.SS);
                else if (shootingMinor)
                    tier = MaxTier(tier, CombatTier.S);
            }

            // 维度2（坚韧格斗系列）：tough + melee
            //   SSS: tough + meleeMajor + (nimble || brawler)
            //   SS:  tough + meleeMajor
            //   S:   tough + meleeAnyPassion
            if (isTough)
            {
                if (meleeMajor && (isNimble || isBrawler))
                    tier = MaxTier(tier, CombatTier.SSS);
                else if (meleeMajor)
                    tier = MaxTier(tier, CombatTier.SS);
                else if (meleeAnyPassion)
                    tier = MaxTier(tier, CombatTier.S);
            }

            // 维度3（工作狂神经质系列）：industrious2 AND neurotic2 + workMajors
            //   SSS: industrious2 && neurotic2 && workMajors >= 3
            //   SS:  industrious2 && neurotic2 && workMajors >= 2
            //   S:   industrious2 && neurotic2 && workMajors >= 1
            if (industrious2 && neurotic2)
            {
                if (workMajors >= 3)
                    tier = MaxTier(tier, CombatTier.SSS);
                else if (workMajors >= 2)
                    tier = MaxTier(tier, CombatTier.SS);
                else if (workMajors >= 1)
                    tier = MaxTier(tier, CombatTier.S);
            }

            // 原 S 条件 4：拥有特殊天赋特质之一
            //   博闻强识 TooSmart / 开心果 Joyous / 极致体能 BodyMastery
            //   痴迷虚空 VoidFascination / 神秘学者 Occultist / 怪诞不经 Disturbing
            if (HasSpecialTalentTrait(pawn))
            {
                tier = MaxTier(tier, CombatTier.S);
            }

            // 原 S 条件 5：沉鱼落雁（Beauty degree=2）+ Social 双火
            if (beauty2 && socialMajor)
            {
                tier = MaxTier(tier, CombatTier.S);
            }

            // A/B 判定：仅在三大维度+原S条件均未触达（tier==C）时进行
            if (tier == CombatTier.C)
            {
                // A：≥ 2 个双 Major + ≥ 1 个单 Minor
                if (majorCount >= 2 && (majorCount + minorCount) >= 3)
                {
                    tier = CombatTier.A;
                }
                // B：≥ 1 个双 Major + ≥ 2 个单 Minor（合计 ≥ 3）
                else if (majorCount >= 1 && (majorCount + minorCount) >= 3)
                {
                    tier = CombatTier.B;
                }
            }

            // 降档：有负面特质且 tier > D 时降一档
            //   纵火狂 Pyromaniac / 脑子慢 SlowLearner / 脆弱 Wimp
            //   工作懒惰 Industriousness degree=-1 / 工作怠惰 Industriousness degree=-2
            // D 不再降；X 先于一切判定不受影响
            if (tier > CombatTier.D && HasNegativeTrait(pawn))
            {
                tier = (CombatTier)(tier - 1);
            }

            return tier;
        }

        /// <summary>
        /// 统计 Pawn 拥有指定 Passion 等级的技能数量。
        /// 仅遍历 9 个核心可兴趣技能：射击/近战/社交/手工/建造/艺术/烹饪/种植/采矿
        /// 避免遍历 pawn.skills.skills 以减少 GC。
        /// </summary>
        private static int CountPassions(Pawn pawn, Passion passion)
        {
            int count = 0;
            if (IsPassion(pawn, SkillDefOf.Shooting, passion)) count++;
            if (IsPassion(pawn, SkillDefOf.Melee, passion)) count++;
            if (IsPassion(pawn, SkillDefOf.Social, passion)) count++;
            if (IsPassion(pawn, SkillDefOf.Crafting, passion)) count++;
            if (IsPassion(pawn, SkillDefOf.Construction, passion)) count++;
            if (IsPassion(pawn, SkillDefOf.Artistic, passion)) count++;
            if (IsPassion(pawn, SkillDefOf.Cooking, passion)) count++;
            if (IsPassion(pawn, SkillDefOf.Plants, passion)) count++;
            if (IsPassion(pawn, SkillDefOf.Mining, passion)) count++;
            return count;
        }

        private static bool IsPassion(Pawn pawn, SkillDef skillDef, Passion passion)
        {
            SkillRecord s = pawn.skills?.GetSkill(skillDef);
            return s != null && s.passion == passion;
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
        /// </summary>
        private static int CountWorkMajors(Pawn pawn)
        {
            int count = 0;
            if (IsPassion(pawn, SkillDefOf.Crafting, Passion.Major)) count++;
            if (IsPassion(pawn, SkillDefOf.Construction, Passion.Major)) count++;
            if (IsPassion(pawn, SkillDefOf.Artistic, Passion.Major)) count++;
            if (IsPassion(pawn, SkillDefOf.Cooking, Passion.Major)) count++;
            if (IsPassion(pawn, SkillDefOf.Plants, Passion.Major)) count++;
            if (IsPassion(pawn, SkillDefOf.Mining, Passion.Major)) count++;
            return count;
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
    }
}
