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
        /// </summary>

        // 多 degree 特质：ShootingAccuracy 单一 defName，degree 区分乱开枪(-1)/冷枪手(+1)
        // 禁止把 degree 的 label 当作 defName 查询；Tough 不在原生 DefOf 中，需安全查询
        // Nimble/Bloodlust 同样不在原生 DefOf 中，与 WeaponTraitScorer 一致使用安全查询
        private static readonly TraitDef shootingAccuracyDef = DefDatabase<TraitDef>.GetNamed("ShootingAccuracy", false);
        private static readonly TraitDef toughDef = DefDatabase<TraitDef>.GetNamed("Tough", false);
        private static readonly TraitDef nimbleDef = DefDatabase<TraitDef>.GetNamed("Nimble", false);
        private static readonly TraitDef bloodlustDef = DefDatabase<TraitDef>.GetNamed("Bloodlust", false);

        public static float ComputeCombatValue(Pawn pawn)
        {
            if (pawn?.skills == null) return 0f;

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
        /// </summary>
        public static CombatTier GetCombatTier(Pawn pawn)
        {
            if (pawn == null) return CombatTier.X;

            // X：无法从事暴力活动（医疗特质 DisableViolent、未成年限制等）
            if (pawn.WorkTagIsDisabled(WorkTags.Violent)) return CombatTier.X;

            if (pawn.skills == null) return CombatTier.X;

            SkillRecord shooting = pawn.skills.GetSkill(SkillDefOf.Shooting);
            SkillRecord melee = pawn.skills.GetSkill(SkillDefOf.Melee);

            bool shootingMajor = shooting != null && shooting.passion == Passion.Major;
            bool meleeMajor = melee != null && melee.passion == Passion.Major;
            bool shootingMinor = shooting != null && shooting.passion == Passion.Minor;
            bool meleeMinor = melee != null && melee.passion == Passion.Minor;

            bool bothMajor = shootingMajor && meleeMajor;
            bool anyPassion = shootingMajor || meleeMajor || shootingMinor || meleeMinor;

            bool hasCombatTrait = HasCombatTrait(pawn);

            // S：双火带特质
            if (bothMajor && hasCombatTrait) return CombatTier.S;
            // A：双火
            if (bothMajor) return CombatTier.A;
            // B：单火/单 Major（任一射击或近战有兴趣，但非双 Major）
            if (anyPassion) return CombatTier.B;
            // C：无火但有战斗特质
            if (hasCombatTrait) return CombatTier.C;
            // D：无火无特质
            return CombatTier.D;
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
            candidatePawns.Sort(ComparePawnByCombatValueDesc);

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
                    if (needRanged && IsPureMeleeShooter(pawn) && IsEmpWeapon(w))
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
                    && worn[i].def.defName.ToUpperInvariant().Contains("SHIELD"))
                {
                    return true;
                }
            }
            return false;
        }

        private static int ComparePawnByCombatValueDesc(Pawn a, Pawn b)
        {
            return ComputeCombatValue(b).CompareTo(ComputeCombatValue(a));
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

        /// <summary>
        /// 纯近战角色判定：射击技能无火（passion=None）。
        /// 此类殖民者远程射击天赋不足，副武器应优先 EMP 而非普通远程武器。
        /// </summary>
        private static bool IsPureMeleeShooter(Pawn pawn)
        {
            if (pawn?.skills == null) return false;
            SkillRecord shooting = pawn.skills.GetSkill(SkillDefOf.Shooting);
            if (shooting == null) return true;
            return shooting.passion == Passion.None;
        }

        /// <summary>
        /// EMP 武器判定：通过 defName/label 启发式识别。
        /// 覆盖原生 EMP 手榴弹、EMP 炮及 MOD 扩展的 EMP 武器。
        /// </summary>
        private static bool IsEmpWeapon(Thing weapon)
        {
            if (weapon?.def == null) return false;
            return weapon.def.defName.ToUpperInvariant().Contains("EMP")
                || weapon.def.label.ToUpperInvariant().Contains("EMP");
        }

        private static void AssignSidearm(Pawn pawn, Thing weapon, float score)
        {
            var job = JobMaker.MakeJob(JobDefOf.TakeCountToInventory, weapon);
            job.count = 1;
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);

            // 决策日志：玩家可见的换装反馈（低频，受全局周期控制）
            Log.Message($"[AutoEquipment] 副武器分配: {pawn.LabelShort} (战斗价值={ComputeCombatValue(pawn):F1}) ← {weapon.LabelShort} (score={score:F1})");
        }
    }
}
