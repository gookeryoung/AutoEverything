using System;
using System.Collections.Generic;
using AutoEverything.Core;
using AutoEverything.RoleEvaluation;
using RimWorld;
using Verse;
using Verse.AI;

namespace AutoEverything.AutoEquip
{
    /// <summary>
    /// 自动装备分配器：扫描地图上的可用武器/服装，为每个殖民者挑选并派发一个装备 Job。
    ///
    /// 安全边界：
    /// - 只取地图上已生成、未禁用、未被预留的物品，不扒其他殖民者身上的装备；
    /// - 每个 Pawn 每轮最多派发一个 Job，避免互相覆盖；
    /// - 战斗、医疗、仪式、睡眠、玩家强制 Job 等状态直接跳过。
    /// </summary>
    public static class EquipAllocator
    {
        private const int ErrorSalt = 0xA940;

        private static readonly List<ThingWithComps> WeaponCandidates = new List<ThingWithComps>();
        private static readonly List<Apparel> ApparelCandidates = new List<Apparel>();
        private static readonly List<Pawn> PawnBuffer = new List<Pawn>();
        private static readonly List<Apparel> ConflictBuffer = new List<Apparel>();
        private static readonly List<ThingDef> InventoryDefBuffer = new List<ThingDef>();
        private static readonly List<string> DetailLines = new List<string>();
        private static readonly Comparison<Pawn> PawnTierComparison = ComparePawnTier;

        /// <summary>本轮是否收集逐条变更说明（手动“立即装备”或调试日志开启时为 true）。</summary>
        private static bool detailCollecting;

        public static volatile bool IsDirty;

        /// <summary>最近一次 ReallocateAll 的逐条变更说明（“殖民者 + 做了什么”），便于玩家查看。</summary>
        public static IReadOnlyList<string> LastDetails { get { return DetailLines; } }

        /// <summary>物品生成/销毁时由 Harmony 调用，标记下一轮需要重算。</summary>
        public static void MarkDirty(Thing thing)
        {
            if (!AESettings.autoEquipEnabled || thing == null || thing.def == null) return;
            if (thing.def.IsWeapon || thing.def.IsApparel) IsDirty = true;
        }

        /// <summary>
        /// 返回本轮实际派发 Job 的 Pawn 数量。
        /// collectDetails=true 时把每个 Pawn 的具体变更（换了什么、从什么换成什么）写入 LastDetails。
        /// </summary>
        public static int ReallocateAll(bool force = false, bool collectDetails = false)
        {
            IsDirty = false;
            DetailLines.Clear();
            detailCollecting = collectDetails;
            if (!AESettings.autoEquipEnabled && !force) return 0;
            if (Find.Maps == null || Find.Maps.Count == 0) return 0;

            try
            {
                CollectCandidates();
                CollectPawns();
                if (PawnBuffer.Count == 0) return 0;

                ApparelScorer.BeginRound();
                PawnBuffer.Sort(PawnTierComparison);

                int dispatched = 0;
                bool apparelOnly = !force && CombatArmingActive;
                for (int i = 0; i < PawnBuffer.Count; i++)
                {
                    Pawn pawn = PawnBuffer[i];
                    if (apparelOnly && !CanArmForCombat(pawn)) continue;
                    if (TryEquipPawn(pawn, apparelOnly)) dispatched++;
                }

                if (dispatched > 0 && AEDebug.IsActive)
                    AEDebug.Log(() => "[EquipAllocator] 本轮派发装备 Job: " + dispatched);
                return dispatched;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] 自动装备分配失败: " + ex,
                    EquipErrorId(0));
                return 0;
            }
        }

        private static int EquipErrorId(int salt)
        {
            return ErrorSalt + salt;
        }

        private static void CollectCandidates()
        {
            WeaponCandidates.Clear();
            ApparelCandidates.Clear();

            List<Map> maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                Map map = maps[m];
                if (map == null) continue;

                List<Thing> weapons = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon);
                if (weapons != null)
                {
                    for (int i = 0; i < weapons.Count; i++)
                    {
                        ThingWithComps weapon = weapons[i] as ThingWithComps;
                        if (weapon != null && weapon.def != null && weapon.def.IsWeapon)
                            WeaponCandidates.Add(weapon);
                    }
                }

                List<Thing> apparel = map.listerThings.ThingsInGroup(ThingRequestGroup.Apparel);
                if (apparel != null)
                {
                    for (int i = 0; i < apparel.Count; i++)
                    {
                        Apparel item = apparel[i] as Apparel;
                        if (item != null && item.def != null && item.def.IsApparel)
                            ApparelCandidates.Add(item);
                    }
                }
            }
        }

        private static void CollectPawns()
        {
            PawnBuffer.Clear();
            List<Pawn> colonists = PawnsFinder.AllMaps_FreeColonistsSpawned;
            if (colonists == null) return;

            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn pawn = colonists[i];
                if (ShouldSkipPawn(pawn)) continue;
                PawnBuffer.Add(pawn);
            }
        }

        private static int ComparePawnTier(Pawn a, Pawn b)
        {
            int tierA = (int)CombatEvaluator.GetCombatTier(a);
            int tierB = (int)CombatEvaluator.GetCombatTier(b);
            return tierB.CompareTo(tierA);
        }

        private static bool ShouldSkipPawn(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Map == null) return true;
            if (!PawnSuitabilityChecker.CanManageGear(pawn)) return true;
            if (!pawn.IsColonist) return true;
            // 难民/逃兵等任务临时成员离开时会带走装备，默认不为他们派发任何 Job
            if (EquipPolicy.ShouldSkipQuestLodger(
                QuestUtility.IsQuestLodger(pawn), AESettings.equipAllowQuestLodgers)) return true;
            // 奴隶/囚犯本轮不纳入（D6）：其着装通常由玩家政策或囚犯管理单独负责
            if (pawn.IsPrisonerOfColony || pawn.IsSlaveOfColony) return true;
            if (DLCCompat.IsGhoul(pawn)) return true;
            if (pawn.Drafted || pawn.Downed || pawn.InMentalState) return true;
            if (pawn.DevelopmentalStage != DevelopmentalStage.Adult) return true;
            if (PawnJobGuard.ShouldSkipForMedical(pawn)) return true;
            if (pawn.CurJob != null && pawn.CurJob.playerForced) return true;
            if (pawn.jobs != null && pawn.jobs.curDriver != null && pawn.jobs.curDriver.asleep) return true;
            return false;
        }

        // ===== 战斗/工作双档案（默认关闭）=====

        private const int ProfileSwitchCooldownTicks = 2500;
        private const int CombatSwapMinImprovementPct = 15;
        private const int MaxCombatSwitchesPerRaid = 2;
        private const float HostileSafeRadius = 15f;

        private static bool raidActive;
        private static int lastHostileTick = -99999;
        private static readonly Dictionary<int, int> LastProfileSwitchTick = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> CombatSwitchCount = new Dictionary<int, int>();
        private static readonly Dictionary<int, ApparelProfile> ActiveProfile =
            new Dictionary<int, ApparelProfile>();

        /// <summary>当前是否处于"袭击中、允许在征召前自动换战斗护甲"的窗口。</summary>
        public static bool CombatArmingActive { get; private set; }

        /// <summary>由 AutoExecutor 每 60 tick 汇报一次"地图上是否存在敌对单位"。</summary>
        public static void NotifyRaidState(bool active, int tick)
        {
            if (active)
            {
                if (!raidActive) CombatSwitchCount.Clear();
                lastHostileTick = tick;
            }
            raidActive = active;
            CombatArmingActive = active && AESettings.equipCombatProfileEnabled;
        }

        /// <summary>
        /// 该 Pawn 此刻是否可以安全地进入战斗档案：
        /// 未被征召、附近没有敌人、本轮袭击换装预算未用完。
        /// </summary>
        private static bool CanArmForCombat(Pawn pawn)
        {
            if (!CombatArmingActive || pawn.Drafted) return false;
            int count;
            if (CombatSwitchCount.TryGetValue(pawn.thingIDNumber, out count)
                && count >= MaxCombatSwitchesPerRaid) return false;
            return !AnyHostileNear(pawn, HostileSafeRadius);
        }

        private static bool AnyHostileNear(Pawn pawn, float radius)
        {
            Map map = pawn.Map;
            if (map == null || map.mapPawns == null) return true;
            IReadOnlyList<Pawn> all = map.mapPawns.AllPawnsSpawned;
            float radiusSquared = radius * radius;
            for (int i = 0; i < all.Count; i++)
            {
                Pawn other = all[i];
                if (other == null || other.Dead || other.Downed) continue;
                if (!other.HostileTo(Faction.OfPlayer)) continue;
                if (other.Position.DistanceToSquared(pawn.Position) <= radiusSquared) return true;
            }
            return false;
        }

        /// <summary>档案切换滞回：切换后 2500 tick 内不再改变档案，避免装备栏来回抖。</summary>
        private static bool CanUseProfile(Pawn pawn, ApparelProfile profile, int tick)
        {
            if (!AESettings.equipCombatProfileEnabled) return true;
            ApparelProfile stored;
            if (!ActiveProfile.TryGetValue(pawn.thingIDNumber, out stored) || stored == profile) return true;
            int last;
            if (LastProfileSwitchTick.TryGetValue(pawn.thingIDNumber, out last)
                && tick - last < ProfileSwitchCooldownTicks) return false;
            return true;
        }

        private static void NotifyProfileUsed(Pawn pawn, ApparelProfile profile, int tick)
        {
            int id = pawn.thingIDNumber;
            ApparelProfile stored;
            if (ActiveProfile.TryGetValue(id, out stored) && stored != profile)
                LastProfileSwitchTick[id] = tick;
            ActiveProfile[id] = profile;

            if (profile != ApparelProfile.Combat) return;
            int count;
            CombatSwitchCount.TryGetValue(id, out count);
            CombatSwitchCount[id] = count + 1;
        }

        private static bool TryEquipPawn(Pawn pawn, bool apparelOnly)
        {
            if (pawn == null || pawn.Map == null) return false;

            RoleGroup role = RoleDetector.DetectRoleGroup(pawn);
            EquipRoleRule rule = AESettings.GetEquipRoleRule((int)role);
            ApparelProfile profile = GetApparelProfile(pawn);

            // 袭击换装窗口：只处理服装，不换武器（避免临战跑去捡枪）
            if (apparelOnly) return TryWearApparel(pawn, role, ApparelProfile.Combat);

            // 1. 主武器
            if (TryEquipPrimaryWeapon(pawn, role, rule)) return true;
            // 2. 服装（护甲按整套评分）
            if (TryWearApparel(pawn, role, profile)) return true;
            // 3. 远程副武器
            if (TryTakeSidearm(pawn, rule)) return true;
            // 4. 投掷物
            if (TryTakeGrenade(pawn, rule)) return true;

            return false;
        }

        private static bool TryEquipPrimaryWeapon(Pawn pawn, RoleGroup role, EquipRoleRule rule)
        {
            if (!rule.AllowsAnyPrimary) return false;
            if (pawn.WorkTagIsDisabled(WorkTags.Violent)) return false;

            ThingWithComps current = pawn.equipment != null ? pawn.equipment.Primary : null;
            if (current != null && (WeaponClassifier.IsBondedPersona(current, pawn)
                || WeaponClassifier.HasJealousTrait(current)))
            {
                // 已绑定人格武器 / 嫉妒特性：不再尝试替换，避免无效 Job 与心情问题
                return false;
            }

            ThingWithComps best = null;
            WeaponScoreInput bestScore = new WeaponScoreInput();
            WeaponScoreInput currentScore = new WeaponScoreInput();
            bool hasCurrentScore = false;

            if (current != null)
            {
                WeaponKind currentKind = WeaponClassifier.Classify(current.def);
                if ((currentKind == WeaponKind.Melee && rule.Melee)
                    || (currentKind == WeaponKind.Ranged && rule.Ranged))
                {
                    currentScore = WeaponScorer.Build(current, pawn, role, currentKind);
                    hasCurrentScore = true;
                }
            }

            for (int i = 0; i < WeaponCandidates.Count; i++)
            {
                ThingWithComps candidate = WeaponCandidates[i];
                if (candidate == current || !IsCandidateAvailable(pawn, candidate)) continue;

                WeaponKind kind = WeaponClassifier.Classify(candidate.def);
                if (kind == WeaponKind.Melee)
                {
                    if (!rule.Melee) continue;
                }
                else if (kind == WeaponKind.Ranged)
                {
                    if (!rule.Ranged) continue;
                }
                else
                {
                    continue;
                }

                if (WeaponScorer.GetQualityRank(candidate) < (int)AESettings.equipMinQuality) continue;
                if (WeaponClassifier.IsLockedToOtherPawn(candidate, pawn)) continue;

                WeaponScoreInput score = WeaponScorer.Build(candidate, pawn, role, kind);
                if (best == null || EquipPolicy.CompareWeapon(score, bestScore) < 0)
                {
                    best = candidate;
                    bestScore = score;
                }
            }

            if (best == null) return false;

            if (hasCurrentScore)
            {
                float currentComparable = EquipPolicy.ComparableScore(currentScore);
                float bestComparable = EquipPolicy.ComparableScore(bestScore);
                if (!EquipPolicy.ShouldSwap(currentComparable, bestComparable, AESettings.equipMinImprovementPct))
                    return false;
            }

            string change = current != null
                ? "换主武器：" + DescribeWeapon(current) + " → " + DescribeWeapon(best)
                : "装备主武器：" + DescribeWeapon(best);
            if (!TryDispatch(pawn, JobDefOf.Equip, best)) return false;
            RecordChange(pawn, change);
            return true;
        }

        /// <summary>
        /// 服装档案：严寒/酷暑单独一套权重，其余按和平期工作档案。
        /// 战斗档案由 B4 的袭击状态机覆写（默认关闭）。
        /// </summary>
        private static ApparelProfile GetApparelProfile(Pawn pawn)
        {
            if (AESettings.equipCombatProfileEnabled)
            {
                int tick = Find.TickManager.TicksGame;
                if (CanArmForCombat(pawn) && CanUseProfile(pawn, ApparelProfile.Combat, tick))
                    return ApparelProfile.Combat;

                // 袭击结束后按滞回保持战斗档案，避免刚脱甲又来一波
                ApparelProfile stored;
                if (!CombatArmingActive
                    && tick - lastHostileTick < ProfileSwitchCooldownTicks
                    && ActiveProfile.TryGetValue(pawn.thingIDNumber, out stored)
                    && stored == ApparelProfile.Combat)
                    return ApparelProfile.Combat;
            }

            switch (ContextDetector.GetContext(pawn))
            {
                case GearContext.Cold:
                    return ApparelProfile.Cold;
                case GearContext.Hot:
                    return ApparelProfile.Hot;
                default:
                    return ApparelProfile.Work;
            }
        }

        /// <summary>
        /// 裸体主义者默认不自动穿任何衣物（与意识形态 -Nudism 心情惩罚冲突最小处置）；
        /// 开启"裸体主义者护甲例外"后，仅在寒冷/炎热档案（确有保暖或散热需求）时放行。
        /// </summary>
        private static bool IsBlockedByNudism(Pawn pawn, ApparelProfile profile)
        {
            TraitDef nudist = TraitDefOf.Nudist;
            if (nudist == null || pawn.story == null || pawn.story.traits == null) return false;
            if (!pawn.story.traits.HasTrait(nudist)) return false;
            if (!AESettings.equipArmorNudistException) return true;
            return profile != ApparelProfile.Cold && profile != ApparelProfile.Hot;
        }

        private static bool TryWearApparel(Pawn pawn, RoleGroup role, ApparelProfile profile)
        {
            if (pawn.apparel == null) return false;
            if (!AESettings.autoEquipArmor) return false;
            if (IsBlockedByNudism(pawn, profile)) return false;

            int tick = Find.TickManager.TicksGame;
            if (!CanUseProfile(pawn, profile, tick)) return false;

            ApparelWeights weights = ApparelWeights.For(AESettings.equipArmorPreset, profile);
            ApparelSetInput currentSet = ApparelScorer.BuildSet(pawn, null, null, profile);
            float currentScore = ApparelScoreCore.Evaluate(currentSet, weights, role);

            float durabilityFloor = AESettings.equipMinApparelDurabilityPct / 100f;
            float maxDistance = AESettings.equipArmorMaxDistance;

            Apparel bestItem = null;
            float bestScore = 0f;
            string bestChange = null;

            for (int i = 0; i < ApparelCandidates.Count; i++)
            {
                Apparel candidate = ApparelCandidates[i];
                if (candidate == null || candidate.Destroyed || !candidate.Spawned) continue;
                if (candidate.Map != pawn.Map) continue;
                if (candidate.IsForbidden(Faction.OfPlayer) || candidate.IsBurning()) continue;
                if (candidate.Position.Fogged(candidate.Map)) continue;
                Area allowedArea = pawn.playerSettings != null
                    ? pawn.playerSettings.EffectiveAreaRestrictionInPawnCurrentMap
                    : null;
                if (allowedArea != null && !allowedArea[candidate.Position]) continue;
                if (!IsReservable(pawn, candidate)) continue;

                if (!ApparelSelector.IsAllowedByPolicy(pawn, candidate.def)) continue;
                if (!ApparelSelector.HasPartsToWear(pawn, candidate.def)) continue;
                if (!ApparelSelector.IsWearableNow(pawn, candidate)) continue;
                if (WeaponClassifier.IsLockedToOtherPawn(candidate, pawn)) continue;
                if (maxDistance > 0f && pawn.Position.DistanceTo(candidate.Position) > maxDistance) continue;

                ApparelSelector.CollectConflicts(pawn, candidate.def, ConflictBuffer);
                if (ConflictBuffer.Count > ApparelScorer.MaxConflictDrops) continue;
                if (ApparelSelector.WouldBreakRequirement(pawn, ConflictBuffer)) continue;

                ApparelSetInput candidateSet = ApparelScorer.BuildSet(pawn, candidate, ConflictBuffer, profile);
                candidateSet.ConflictDropCount = ConflictBuffer.Count;

                // 低于耐久下限的候选只在"从更差换到更好"时放行，避免穿破布，也避免紧急情况无衣可换。
                if (candidateSet.Durability < durabilityFloor
                    && candidateSet.Durability <= currentSet.Durability) continue;

                float candidateScore = ApparelScoreCore.Evaluate(candidateSet, weights, role);
                bool emergency = currentSet.Durability < durabilityFloor
                    && candidateSet.Durability > currentSet.Durability;
                int minImprovement = profile == ApparelProfile.Combat
                    ? CombatSwapMinImprovementPct
                    : AESettings.equipMinImprovementPct;
                if (!emergency
                    && !EquipPolicy.IsBetterApparel(candidateScore, currentScore, minImprovement))
                    continue;

                if (bestItem == null || candidateScore > bestScore)
                {
                    bestItem = candidate;
                    bestScore = candidateScore;
                    if (detailCollecting || AEDebug.IsActive)
                        bestChange = BuildApparelChange(ConflictBuffer, candidate, currentScore, candidateScore);
                }
            }

            if (bestItem == null) return false;
            if (!TryDispatch(pawn, JobDefOf.Wear, bestItem)) return false;
            NotifyProfileUsed(pawn, profile, tick);
            RecordChange(pawn, bestChange ?? ("装备服装：" + DescribeThing(bestItem)));
            return true;
        }

        private static string BuildApparelChange(List<Apparel> drops, Apparel candidate,
            float currentScore, float candidateScore)
        {
            string from;
            if (drops == null || drops.Count == 0)
            {
                from = "无";
            }
            else
            {
                from = DescribeThing(drops[0]);
                for (int i = 1; i < drops.Count; i++)
                    from += " + " + DescribeThing(drops[i]);
            }

            return "换装：" + from + " → " + DescribeThing(candidate)
                + "（评分 " + currentScore.ToString("F0") + "→" + candidateScore.ToString("F0") + "）";
        }

        private static bool TryTakeSidearm(Pawn pawn, EquipRoleRule rule)
        {
            if (rule.Sidearms <= 0) return false;
            if (pawn.WorkTagIsDisabled(WorkTags.Violent)) return false;

            int existing = CountInventoryWeapons(pawn, WeaponKind.Ranged);
            if (existing >= rule.Sidearms) return false;

            ThingWithComps primary = pawn.equipment != null ? pawn.equipment.Primary : null;
            ThingWithComps best = null;
            WeaponScoreInput bestScore = new WeaponScoreInput();

            for (int i = 0; i < WeaponCandidates.Count; i++)
            {
                ThingWithComps candidate = WeaponCandidates[i];
                if (candidate == primary || !IsCandidateAvailable(pawn, candidate)) continue;
                if (WeaponClassifier.Classify(candidate.def) != WeaponKind.Ranged) continue;
                if (WeaponScorer.GetQualityRank(candidate) < (int)AESettings.equipMinQuality) continue;
                if (WeaponClassifier.IsLockedToOtherPawn(candidate, pawn)) continue;
                if (InventoryContainsDef(pawn, candidate.def)) continue;
                if (primary != null && primary.def == candidate.def) continue;
                // 主武器与已有副武器全部互斥同类：避免泵动霰弹枪 + 链式霰弹枪这种定位重复的搭配
                if (SharesTypeWithCarriedRanged(pawn, primary, candidate.def)) continue;

                WeaponScoreInput score = WeaponScorer.Build(candidate, pawn,
                    RoleDetector.DetectRoleGroup(pawn), WeaponKind.Ranged);
                if (best == null || EquipPolicy.CompareWeapon(score, bestScore) < 0)
                {
                    best = candidate;
                    bestScore = score;
                }
            }

            if (best == null) return false;
            string change = "携带副武器(" + (existing + 1) + "/" + rule.Sidearms + ")：" + DescribeWeapon(best);
            if (!TryDispatch(pawn, JobDefOf.TakeInventory, best, 1)) return false;
            RecordChange(pawn, change);
            return true;
        }

        /// <summary>
        /// 候选项是否与已携带的远程武器（主武器 + 背包内副武器）同类型。
        /// 按 1.6 武器类别判定（如 ShortShots），主武器与所有副武器两两互斥；
        /// 不主动卸下背包里已有的同类副武器，只阻止后续再选。
        /// </summary>
        private static bool SharesTypeWithCarriedRanged(Pawn pawn, ThingWithComps primary, ThingDef candidateDef)
        {
            if (candidateDef == null) return false;
            WeaponTypeKeys candidate = WeaponClassifier.GetTypeKeys(candidateDef);

            if (primary != null && primary.def != null
                && WeaponClassifier.Classify(primary.def) == WeaponKind.Ranged
                && IsSameWeaponType(WeaponClassifier.GetTypeKeys(primary.def), candidate))
                return true;

            if (pawn.inventory == null || pawn.inventory.innerContainer == null) return false;
            ThingOwner container = pawn.inventory.innerContainer;
            for (int i = 0; i < container.Count; i++)
            {
                Thing thing = container[i];
                if (thing == null || thing.def == null) continue;
                if (WeaponClassifier.Classify(thing.def) != WeaponKind.Ranged) continue;
                if (IsSameWeaponType(WeaponClassifier.GetTypeKeys(thing.def), candidate)) return true;
            }
            return false;
        }

        private static bool IsSameWeaponType(WeaponTypeKeys a, WeaponTypeKeys b)
        {
            if (a == null || b == null) return false;
            return WeaponTypePolicy.IsSameType(a.Classes, a.Tags, b.Classes, b.Tags);
        }

        private static bool TryTakeGrenade(Pawn pawn, EquipRoleRule rule)
        {
            if (!rule.Thrown) return false;
            if (pawn.WorkTagIsDisabled(WorkTags.Violent)) return false;

            int kinds = CountInventoryWeaponKinds(pawn, WeaponKind.Thrown);
            if (kinds >= AESettings.equipMaxThrownKinds) return false;

            ThingWithComps best = null;
            float bestScore = 0f;
            for (int i = 0; i < WeaponCandidates.Count; i++)
            {
                ThingWithComps candidate = WeaponCandidates[i];
                if (!IsCandidateAvailable(pawn, candidate)) continue;
                if (WeaponClassifier.Classify(candidate.def) != WeaponKind.Thrown) continue;
                if (WeaponClassifier.IsLockedToOtherPawn(candidate, pawn)) continue;
                if (InventoryContainsDef(pawn, candidate.def)) continue;

                // 按杀伤力而非市价选投掷物，避免高价材质/装饰特性干扰
                float score = WeaponScorer.ComputeThrownScore(candidate);
                if (best == null || score > bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }

            if (best == null) return false;
            string change = "携带投掷物(" + (kinds + 1) + "/" + AESettings.equipMaxThrownKinds + ")：" + DescribeThing(best);
            if (!TryDispatch(pawn, JobDefOf.TakeInventory, best, 1)) return false;
            RecordChange(pawn, change);
            return true;
        }

        /// <summary>
        /// 记录一次成功派发的装备变更：
        /// - detailCollecting=true 时写入 LastDetails，供“立即装备”逐条展示；
        /// - 调试日志开启时同时输出到日志。
        /// 格式：殖民者 + 具体动作（装备/更换，从什么到什么）。
        /// </summary>
        private static void RecordChange(Pawn pawn, string text)
        {
            if (pawn == null) return;
            if (!detailCollecting && !AEDebug.IsActive) return;

            string line = AEDebug.Label(pawn) + " " + text;
            if (detailCollecting) DetailLines.Add(line);
            if (AEDebug.IsActive) Log.Message("[EquipAllocator] " + line);
        }

        /// <summary>武器描述：Label 已含品质/材质前缀，再补特性名以说明特化武器的优势来源。</summary>
        private static string DescribeWeapon(ThingWithComps weapon)
        {
            if (weapon == null) return "无";
            string traits = WeaponClassifier.DescribeTraits(weapon);
            return string.IsNullOrEmpty(traits)
                ? weapon.Label
                : weapon.Label + "[特性:" + traits + "]";
        }

        /// <summary>通用物品描述：使用 Label，自动带品质前缀。</summary>
        private static string DescribeThing(Thing thing)
        {
            return thing == null ? "无" : thing.Label;
        }

        private static bool IsCandidateAvailable(Pawn pawn, ThingWithComps thing)
        {
            if (thing == null || thing.Destroyed || !thing.Spawned) return false;
            if (thing.Map != pawn.Map) return false;
            if (thing.IsForbidden(Faction.OfPlayer)) return false;
            if (thing.IsBurning()) return false;
            if (thing.Position.Fogged(thing.Map)) return false;

            Area allowedArea = pawn.playerSettings?.EffectiveAreaRestrictionInPawnCurrentMap;
            if (allowedArea != null && !allowedArea[thing.Position]) return false;
            return IsReservable(pawn, thing);
        }

        private static bool IsReservable(Pawn pawn, Thing thing)
        {
            Map map = pawn.Map;
            if (map == null || map.reservationManager == null) return false;
            return map.reservationManager.CanReserve(pawn, thing, 1, -1, null, false);
        }

        private static int CountInventoryWeapons(Pawn pawn, WeaponKind kind)
        {
            int count = 0;
            if (pawn.inventory == null || pawn.inventory.innerContainer == null) return 0;
            ThingOwner container = pawn.inventory.innerContainer;
            for (int i = 0; i < container.Count; i++)
            {
                Thing thing = container[i];
                if (thing == null || thing.def == null) continue;
                if (WeaponClassifier.Classify(thing.def) == kind) count++;
            }
            return count;
        }

        private static int CountInventoryWeaponKinds(Pawn pawn, WeaponKind kind)
        {
            InventoryDefBuffer.Clear();
            if (pawn.inventory == null || pawn.inventory.innerContainer == null) return 0;
            ThingOwner container = pawn.inventory.innerContainer;
            for (int i = 0; i < container.Count; i++)
            {
                Thing thing = container[i];
                if (thing == null || thing.def == null) continue;
                if (WeaponClassifier.Classify(thing.def) != kind) continue;
                if (!InventoryDefBuffer.Contains(thing.def)) InventoryDefBuffer.Add(thing.def);
            }
            int count = InventoryDefBuffer.Count;
            InventoryDefBuffer.Clear();
            return count;
        }

        private static bool InventoryContainsDef(Pawn pawn, ThingDef def)
        {
            if (def == null || pawn.inventory == null || pawn.inventory.innerContainer == null) return false;
            ThingOwner container = pawn.inventory.innerContainer;
            for (int i = 0; i < container.Count; i++)
            {
                Thing thing = container[i];
                if (thing != null && thing.def == def) return true;
            }
            return false;
        }

        private static bool TryDispatch(Pawn pawn, JobDef jobDef, Thing target, int count = 0)
        {
            if (pawn == null || pawn.Map == null || jobDef == null || target == null) return false;

            Job job = JobMaker.MakeJob(jobDef, target);
            if (count > 0) job.count = count;

            Map map = pawn.Map;
            if (!map.reservationManager.Reserve(pawn, job, target, 1, -1, null, false, false, true))
                return false;

            if (!pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc))
            {
                map.reservationManager.Release(target, pawn, job);
                return false;
            }
            return true;
        }
    }
}
