using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace AutoEquipment
{
    /// <summary>
    /// 殖民者战斗价值档次（用于全局重配优先级与 DEBUG 显示）。
    /// 设计：把"战斗价值"离散化为 6 档，便于玩家直观判断优先级。
    ///   X：无法从事暴力活动（如医疗特质 DisableViolent）
    ///   D：无火无战斗特质（基础农民）
    ///   C：无火但有战斗特质（如 Tough 农民）
    ///   B：单火/单 Major（任一射击或近战有兴趣，但非双 Major）
    ///   A：双火（射击+近战均为 Major 兴趣）
    ///   S：双火且带战斗特质（最高优先级）
    /// </summary>
    public enum CombatTier : byte
    {
        X = 0,
        D = 1,
        C = 2,
        B = 3,
        A = 4,
        S = 5
    }

    /// <summary>
    /// 副武器全局分配器：按战斗价值优先级为殖民者分配副武器。
    ///
    /// 设计目的：
    /// - 高战斗价值角色（双火高技能）优先获得副武器
    /// - 地图副武器数量不足时，低价值角色不抢占
    /// - 避免单 Pawn 各自拾取导致低价值角色先拿到武器
    ///
    /// 战斗价值分 = 射击等级 × 兴趣乘数 + 近战等级 × 兴趣乘数
    /// 兴趣乘数：无火 1.0，单火 1.5，双火 2.0
    /// 双火高技能角色得分最高，优先分配。
    /// </summary>
    public static class SidearmAllocator
    {
        // 全局分配间隔：比单 Pawn 评估间隔长，避免每个 Pawn 触发全局扫描
        // 2000 tick ≈ 33 秒，副武器非紧急操作，延迟可接受
        private const int AllocationInterval = 2000;
        private static int lastAllocationTick = -9999;

        // 候选缓存（Tick 路径禁止 new List，复用静态字段）
        private static readonly List<Pawn> candidatePawns = new List<Pawn>();
        private static readonly List<Thing> candidateWeapons = new List<Thing>();

        /// <summary>
        /// 为单个 Pawn 触发副武器分配。
        /// 受全局周期控制：仅当距离上次全局分配超过 AllocationInterval 时才重新分配。
        /// 第一个触发该周期的 Pawn 承担全局分配成本，其余 Pawn 跳过。
        /// </summary>
        public static void AllocateForPawn(Pawn pawn)
        {
            int tick = Find.TickManager.TicksGame;
            if (tick - lastAllocationTick < AllocationInterval) return;
            lastAllocationTick = tick;

            AllocateAllColonists();
        }

        /// <summary>
        /// 计算 Pawn 的战斗价值分（用于副武器分配优先级与全局重配顺序）。
        /// 公式：战斗价值 = (射击等级×射击兴趣乘数 + 近战等级×近战兴趣乘数) × 技能权重 + Σ特质加分
        /// 兴趣乘数、技能权重、特质加分均可在面板上由玩家调整。
        /// 命中自定义评级的 Pawn 不走公式，直接采用对应档次的代表分（保证排序稳定）。
        /// </summary>

        // 多 degree 特质：ShootingAccuracy 单一 defName，degree 区分乱开枪(-1)/冷枪手(+1)
        // 禁止把 degree 的 label 当作 defName 查询；Tough 不在原生 DefOf 中，需安全查询
        // Nimble/Bloodlust 同样不在原生 DefOf 中，与 WeaponTraitScorer 一致使用安全查询
        private static readonly TraitDef shootingAccuracyDef = DefDatabase<TraitDef>.GetNamed("ShootingAccuracy", false);
        private static readonly TraitDef toughDef = DefDatabase<TraitDef>.GetNamed("Tough", false);
        private static readonly TraitDef nimbleDef = DefDatabase<TraitDef>.GetNamed("Nimble", false);
        private static readonly TraitDef bloodlustDef = DefDatabase<TraitDef>.GetNamed("Bloodlust", false);

        // 自定义评级档次对应的"代表分"，用于排序时与公式计算值同尺度比较
        // 设计：自动公式下 D≈0~10，B≈10~30，A≈30~50，S≈50~80
        // 自定义评级分数取每档中段：D=5, C=15, B=25, A=50, S=80, X=-1
        // 这样自定义 S 档总能排到自动公式的 S 档之前（同档自定义优先）
        private static readonly float[] tierRepresentativeScore = new float[]
        {
            -1f,   // X
            5f,    // D
            15f,   // C
            25f,   // B
            50f,   // A
            80f    // S
        };

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
                if (toughDef != null && pawn.story.traits.HasTrait(toughDef))
                    total += AESettings.cvToughBonus;

                if (shootingAccuracyDef != null)
                {
                    int shootingAccDegree = pawn.story.traits.DegreeOfTrait(shootingAccuracyDef);
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
        /// 新规则（用户定义的全局价值评级）：
        ///   S：满足任一特殊组合
        ///     1. Tough + Melee 双 Major
        ///     2. ShootingAccuracy degree=-1（乱开枪）+ Shooting 双 Major
        ///     3. Industriousness degree=2（勤奋）OR Neurotic degree=2（严重神经质）
        ///        + Crafting/Construction/Artistic/Cooking/Plants/Mining 任一双 Major
        ///     4. 拥有任一"特殊天赋"特质（博闻强识/开心果/极致体能/痴迷虚空/神秘学者/怪诞不经）
        ///     5. Beauty degree=2（沉鱼落雁）+ Social 双 Major
        ///   A：不满足 S，但所有兴趣中至少 2 个双 Major + 1 个 Minor 以上
        ///   B：不满足以上，但所有兴趣中至少 1 个双 Major + 2 个 Minor 以上
        ///   D：拥有负面特质（纵火狂/脑子慢/脆弱/工作懒惰/工作怠惰）
        ///   C：其他情况
        ///   X：无法从事暴力活动（保留战斗维度判定，确保武器分配跳过）
        /// </summary>
        public static CombatTier GetAutoCombatTier(Pawn pawn)
        {
            if (pawn == null) return CombatTier.X;

            // X：无法从事暴力活动（医疗特质 DisableViolent、未成年限制等）
            // 注：S/A/B/C/D 判定与战斗维度正交，但 X 档保留——避免给不能战斗的 Pawn 装武器
            if (pawn.WorkTagIsDisabled(WorkTags.Violent)) return CombatTier.X;

            if (pawn.skills == null) return CombatTier.X;

            // 统计所有兴趣的双 Major/Minor 数量
            int majorCount = CountPassions(pawn, Passion.Major);
            int minorCount = CountPassions(pawn, Passion.Minor);

            // 取关键技能的 Major 状态
            bool shootingMajor = IsPassion(pawn, SkillDefOf.Shooting, Passion.Major);
            bool meleeMajor = IsPassion(pawn, SkillDefOf.Melee, Passion.Major);
            bool socialMajor = IsPassion(pawn, SkillDefOf.Social, Passion.Major);
            bool craftingMajor = IsPassion(pawn, SkillDefOf.Crafting, Passion.Major);
            bool constructionMajor = IsPassion(pawn, SkillDefOf.Construction, Passion.Major);
            bool artisticMajor = IsPassion(pawn, SkillDefOf.Artistic, Passion.Major);
            bool cookingMajor = IsPassion(pawn, SkillDefOf.Cooking, Passion.Major);
            bool plantsMajor = IsPassion(pawn, SkillDefOf.Plants, Passion.Major);
            bool miningMajor = IsPassion(pawn, SkillDefOf.Mining, Passion.Major);

            bool anyWorkMajor = craftingMajor || constructionMajor || artisticMajor
                                 || cookingMajor || plantsMajor || miningMajor;

            // S 条件 1：Tough + Melee 双火
            if (toughDef != null && pawn.story?.traits != null
                && pawn.story.traits.HasTrait(toughDef) && meleeMajor)
            {
                return CombatTier.S;
            }

            // S 条件 2：乱开枪（ShootingAccuracy degree=-1）+ Shooting 双火
            if (shootingAccuracyDef != null && pawn.story?.traits != null)
            {
                int shootingAccDegree = pawn.story.traits.DegreeOfTrait(shootingAccuracyDef);
                if (shootingAccDegree == -1 && shootingMajor)
                {
                    return CombatTier.S;
                }
            }

            // S 条件 3：勤奋（Industriousness degree=2）或严重神经质（Neurotic degree=2）
                //          + 手工/建造/艺术/烹饪/种植/采矿任一双火
            if (pawn.story?.traits != null && anyWorkMajor)
            {
                bool industrious2 = (industriousnessDef != null
                                      && pawn.story.traits.DegreeOfTrait(industriousnessDef) == 2);
                bool neurotic2 = (neuroticDef != null
                                   && pawn.story.traits.DegreeOfTrait(neuroticDef) == 2);
                if (industrious2 || neurotic2)
                {
                    return CombatTier.S;
                }
            }

            // S 条件 4：拥有特殊天赋特质之一
            //   博闻强识 TooSmart / 开心果 Joyous / 极致体能 BodyMastery
            //   痴迷虚空 VoidFascination / 神秘学者 Occultist / 怪诞不经 Disturbing
            if (HasSpecialTalentTrait(pawn))
            {
                return CombatTier.S;
            }

            // S 条件 5：沉鱼落雁（Beauty degree=2）+ Social 双火
            if (beautyDef != null && pawn.story?.traits != null
                && pawn.story.traits.DegreeOfTrait(beautyDef) == 2
                && socialMajor)
            {
                return CombatTier.S;
            }

            // D：负面特质（任一）
            //   纵火狂 Pyromaniac / 脑子慢 SlowLearner / 脆弱 Wimp
            //   工作懒惰 Industriousness degree=-1 / 工作怠惰 Industriousness degree=-2
            if (HasNegativeTrait(pawn))
            {
                return CombatTier.D;
            }

            // A：≥ 2 个双 Major + ≥ 1 个单 Minor
            if (majorCount >= 2 && (majorCount + minorCount) >= 3)
            {
                return CombatTier.A;
            }

            // B：≥ 1 个双 Major + ≥ 2 个单 Minor（合计 ≥ 3）
            if (majorCount >= 1 && (majorCount + minorCount) >= 3)
            {
                return CombatTier.B;
            }

            // C：其他
            return CombatTier.C;
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
        /// 检查是否拥有"特殊天赋"特质之一（用于 S 条件 4）。
        /// 博闻强识 TooSmart（原生）/ 开心果 Joyous（Anomaly）
        /// 极致体能 BodyMastery（Anomaly）/ 痴迷虚空 VoidFascination（Anomaly）
        /// 神秘学者 Occultist（Anomaly）/ 怪诞不经 Disturbing（Anomaly）
        /// 全部用 GetNamed(false) 安全查询，未加载 DLC 时返回 null 跳过。
        /// </summary>
        private static readonly TraitDef tooSmartDef = DefDatabase<TraitDef>.GetNamed("TooSmart", false);
        private static readonly TraitDef joyousDef = DefDatabase<TraitDef>.GetNamed("Joyous", false);
        private static readonly TraitDef bodyMasteryDef = DefDatabase<TraitDef>.GetNamed("BodyMastery", false);
        private static readonly TraitDef voidFascinationDef = DefDatabase<TraitDef>.GetNamed("VoidFascination", false);
        private static readonly TraitDef occultistDef = DefDatabase<TraitDef>.GetNamed("Occultist", false);
        private static readonly TraitDef disturbingDef = DefDatabase<TraitDef>.GetNamed("Disturbing", false);

        private static bool HasSpecialTalentTrait(Pawn pawn)
        {
            if (pawn.story?.traits == null) return false;
            if (tooSmartDef != null && pawn.story.traits.HasTrait(tooSmartDef)) return true;
            if (joyousDef != null && pawn.story.traits.HasTrait(joyousDef)) return true;
            if (bodyMasteryDef != null && pawn.story.traits.HasTrait(bodyMasteryDef)) return true;
            if (voidFascinationDef != null && pawn.story.traits.HasTrait(voidFascinationDef)) return true;
            if (occultistDef != null && pawn.story.traits.HasTrait(occultistDef)) return true;
            if (disturbingDef != null && pawn.story.traits.HasTrait(disturbingDef)) return true;
            return false;
        }

        /// <summary>
        /// 用于 D 档判定的特质查询（多 degree Industriousness + 单 degree Pyromaniac/SlowLearner/Wimp）。
        /// </summary>
        private static readonly TraitDef industriousnessDef = DefDatabase<TraitDef>.GetNamed("Industriousness", false);
        private static readonly TraitDef neuroticDef = DefDatabase<TraitDef>.GetNamed("Neurotic", false);
        private static readonly TraitDef beautyDef = DefDatabase<TraitDef>.GetNamed("Beauty", false);
        private static readonly TraitDef pyromaniacDef = DefDatabase<TraitDef>.GetNamed("Pyromaniac", false);
        private static readonly TraitDef slowLearnerDef = DefDatabase<TraitDef>.GetNamed("SlowLearner", false);
        private static readonly TraitDef wimpDef = DefDatabase<TraitDef>.GetNamed("Wimp", false);

        /// <summary>
        /// 检查是否拥有"负面"特质之一（用于 D 档判定）。
        /// 纵火狂 Pyromaniac / 脑子慢 SlowLearner / 脆弱 Wimp
        /// 工作懒惰 Industriousness degree=-1 / 工作怠惰 Industriousness degree=-2
        /// </summary>
        private static bool HasNegativeTrait(Pawn pawn)
        {
            if (pawn.story?.traits == null) return false;
            if (pyromaniacDef != null && pawn.story.traits.HasTrait(pyromaniacDef)) return true;
            if (slowLearnerDef != null && pawn.story.traits.HasTrait(slowLearnerDef)) return true;
            if (wimpDef != null && pawn.story.traits.HasTrait(wimpDef)) return true;
            if (industriousnessDef != null)
            {
                int deg = pawn.story.traits.DegreeOfTrait(industriousnessDef);
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
            return StripTierTagPrefixFromLabel(pawn.LabelShort ?? string.Empty);
        }

        /// <summary>
        /// 剥离 Label/Nick 上的评级前缀（格式：单字母 + #）。
        /// 若无前缀返回原值。与 SGSettings.StripTierTagPrefix 同语义，独立实现避免跨类耦合。
        /// </summary>
        private static string StripTierTagPrefixFromLabel(string label)
        {
            if (string.IsNullOrEmpty(label) || label.Length < 2) return label;
            char c = label[0];
            // 单字母（A-Z，覆盖 S/A/B/C/D/X）+ #
            if (c >= 'A' && c <= 'Z' && label[1] == '#')
            {
                return label.Substring(2);
            }
            return label;
        }

        /// <summary>
        /// 是否携带战斗相关特质（Brawler/Tough/Nimble/Bloodlust/ShootingAccuracy 任意 degree）。
        /// </summary>
        private static bool HasCombatTrait(Pawn pawn)
        {
            if (pawn.story?.traits == null) return false;

            // TraitDefOf.Brawler 始终存在
            if (pawn.story.traits.HasTrait(TraitDefOf.Brawler)) return true;
            if (toughDef != null && pawn.story.traits.HasTrait(toughDef)) return true;
            if (nimbleDef != null && pawn.story.traits.HasTrait(nimbleDef)) return true;
            if (bloodlustDef != null && pawn.story.traits.HasTrait(bloodlustDef)) return true;
            // ShootingAccuracy 任意 degree（乱开枪/冷枪手）都算战斗特质
            if (shootingAccuracyDef != null && pawn.story.traits.DegreeOfTrait(shootingAccuracyDef) != 0)
                return true;

            return false;
        }

        /// <summary>
        /// 全局分配：收集所有需要副武器的 Pawn 与可用武器，
        /// 按 Pawn 战斗价值降序排序后依次分配。
        /// </summary>
        private static void AllocateAllColonists()
        {
            candidatePawns.Clear();
            candidateWeapons.Clear();

            foreach (Map map in Find.Maps)
            {
                CollectCandidatePawns(map);
                CollectCandidateWeapons(map);
            }

            if (candidatePawns.Count == 0 || candidateWeapons.Count == 0) return;

            // 按战斗价值降序排序（高价值优先）——List.Sort 非 LINQ，Tick 路径允许
            // 预计算缓存：List.Sort 是 O(n log n) 次比较，避免每次比较重复调用
            // ComputeCombatValue（涉及技能查询与特质查询），50 人约省 300 次重复计算
            var combatValueCache = new Dictionary<Pawn, float>();
            for (int i = 0; i < candidatePawns.Count; i++)
            {
                combatValueCache[candidatePawns[i]] = ComputeCombatValue(candidatePawns[i]);
            }
            candidatePawns.Sort((a, b) => combatValueCache[b].CompareTo(combatValueCache[a]));

            // 依次为高价值 Pawn 分配，分配后从候选池移除（设为 null）
            for (int i = 0; i < candidatePawns.Count; i++)
            {
                Pawn pawn = candidatePawns[i];
                Thing primary = pawn.equipment?.Primary;
                if (primary == null) continue;

                bool needMelee = primary.def.IsRangedWeapon;
                bool needRanged = primary.def.IsMeleeWeapon;

                // 护盾腰带约束：护盾会阻挡所有远程武器射击
                // 带护盾的 Pawn 拿远程/EMP 副武器毫无意义，跳过远程副武器分配
                // 带消防背包的 Pawn 不受此限制，可正常配远程/EMP
                bool hasShieldBelt = IsWearingShieldBelt(pawn);
                if (needRanged && hasShieldBelt) continue;

                Thing best = null;
                float bestScore = 0f;
                int bestIdx = -1;

                for (int j = 0; j < candidateWeapons.Count; j++)
                {
                    Thing w = candidateWeapons[j];
                    if (w == null) continue;
                    if (needMelee && !w.def.IsMeleeWeapon) continue;
                    if (needRanged && !w.def.IsRangedWeapon) continue;

                    CompGearManager comp = pawn.GetComp<CompGearManager>();
                    if (comp == null) continue;

                    float score = GearScorer.ScoreSidearm(pawn, w, comp.CurrentRole);

                    // 纯近战角色（射击无火）：副武器优先 EMP，应对机械族/护盾
                    // 设计意图：纯近战小人远程射击天赋不足，普通远程武器收益低
                    // EMP 武器能瘫痪机械族与护盾，贴身近战时提供战术价值
                    // 注意：带护盾腰带的 Pawn 已在上方跳过，此处无需再判断
                    if (needRanged && PawnCombatProfile.IsPureMeleeShooter(pawn) && GearDefClassifier.IsEmpWeapon(w))
                    {
                        score += 1000f;
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = w;
                        bestIdx = j;
                    }
                }

                if (best != null)
                {
                    AssignSidearm(pawn, best, bestScore);
                    candidateWeapons[bestIdx] = null;
                }
            }
        }

        /// <summary>
        /// 检查 Pawn 是否穿戴护盾腰带。
        /// 护盾腰带会阻挡远程武器射击，带护盾的 Pawn 不应配远程副武器。
        /// </summary>
        private static bool IsWearingShieldBelt(Pawn pawn)
        {
            if (pawn.apparel?.WornApparel == null) return false;
            List<Apparel> worn = pawn.apparel.WornApparel;
            for (int i = 0; i < worn.Count; i++)
            {
                if (worn[i].def.apparel?.layers != null
                    && worn[i].def.apparel.layers.Contains(ApparelLayerDefOf.Belt)
                    && worn[i].def.defName.IndexOf("SHIELD", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static void CollectCandidatePawns(Map map)
        {
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                if (DLCCompat.IsGhoul(pawn)) continue;
                if (!PawnSuitabilityChecker.CanManageGear(pawn)) continue;
                if (pawn.WorkTagIsDisabled(WorkTags.Violent)) continue;
                if (DLCCompat.IsSlave(pawn) || DLCCompat.IsChild(pawn)) continue;

                CompGearManager comp = pawn.GetComp<CompGearManager>();
                if (comp == null || comp.locked) continue;

                // 必须有主武器才需要副武器
                Thing primary = pawn.equipment?.Primary;
                if (primary == null) continue;

                // 库存中已有副武器则跳过
                if (HasSidearm(pawn, primary)) continue;

                candidatePawns.Add(pawn);
            }
        }

        private static void CollectCandidateWeapons(Map map)
        {
            foreach (Thing weapon in map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon))
            {
                if (!IsCandidateSidearm(weapon)) continue;
                candidateWeapons.Add(weapon);
            }
        }

        /// <summary>
        /// 检查 Pawn 库存中是否已持有与主武器类型相反的副武器。
        /// </summary>
        private static bool HasSidearm(Pawn pawn, Thing primary)
        {
            bool primaryIsRanged = primary.def.IsRangedWeapon;
            foreach (Thing item in pawn.inventory.innerContainer)
            {
                if (primaryIsRanged && item.def.IsMeleeWeapon) return true;
                if (!primaryIsRanged && item.def.IsRangedWeapon) return true;
            }
            return false;
        }

        /// <summary>
        /// 判断武器是否为可用副武器候选。
        /// </summary>
        private static bool IsCandidateSidearm(Thing weapon)
        {
            if (weapon?.def == null) return false;
            if (!weapon.def.IsWeapon) return false;
            // 候选筛选留给分配阶段（needMelee/needRanged），此处仅排除非武器
            return true;
        }

        private static void AssignSidearm(Pawn pawn, Thing weapon, float score)
        {
            var job = JobMaker.MakeJob(JobDefOf.TakeCountToInventory, weapon);
            job.count = 1;
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);

            // 决策日志：玩家可见的换装反馈（低频，受全局周期控制）
            Log.Message($"[AutoEquipment] 副武器分配: {AEDebug.Label(pawn)} (战斗价值={ComputeCombatValue(pawn):F1}) ← {weapon.LabelShort} (score={score:F1})");
        }
    }
}
