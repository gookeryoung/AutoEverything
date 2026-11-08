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

                PawnBuffer.Sort(PawnTierComparison);

                int dispatched = 0;
                for (int i = 0; i < PawnBuffer.Count; i++)
                {
                    Pawn pawn = PawnBuffer[i];
                    if (TryEquipPawn(pawn)) dispatched++;
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
            if (pawn.IsPrisonerOfColony || pawn.IsSlaveOfColony) return true;
            if (DLCCompat.IsGhoul(pawn)) return true;
            if (pawn.Drafted || pawn.Downed || pawn.InMentalState) return true;
            if (pawn.DevelopmentalStage != DevelopmentalStage.Adult) return true;
            if (PawnJobGuard.ShouldSkipForMedical(pawn)) return true;
            if (pawn.CurJob != null && pawn.CurJob.playerForced) return true;
            if (pawn.jobs != null && pawn.jobs.curDriver != null && pawn.jobs.curDriver.asleep) return true;
            return false;
        }

        private static bool TryEquipPawn(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null) return false;

            RoleGroup role = RoleDetector.DetectRoleGroup(pawn);
            EquipRoleRule rule = AESettings.GetEquipRoleRule((int)role);

            // 1. 主武器
            if (TryEquipPrimaryWeapon(pawn, role, rule)) return true;
            // 2. 服装
            if (TryWearApparel(pawn, role)) return true;
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

        private static bool TryWearApparel(Pawn pawn, RoleGroup role)
        {
            if (pawn.apparel == null) return false;

            Apparel bestItem = null;
            Apparel bestCurrent = null;
            float bestDelta = 0f;

            for (int i = 0; i < ApparelCandidates.Count; i++)
            {
                Apparel candidate = ApparelCandidates[i];
                if (candidate == null || candidate.Destroyed || !candidate.Spawned) continue;
                if (candidate.Map != pawn.Map) continue;
                if (candidate.IsForbidden(Faction.OfPlayer) || candidate.IsBurning()) continue;
                if (!IsReservable(pawn, candidate)) continue;
                if (!ApparelSelector.IsAllowedByPolicy(pawn, candidate.def)) continue;
                if (!ApparelSelector.HasPartsToWear(pawn, candidate.def)) continue;

                ApparelLayerDef layer;
                if (!ApparelSelector.TryGetFirstLayer(candidate.def, out layer)) continue;

                Apparel current = ApparelSelector.FindWornInLayer(pawn, layer);
                if (ApparelSelector.HasConflict(pawn, candidate.def, current)) continue;
                if (WeaponClassifier.IsLockedToOtherPawn(candidate, pawn)) continue;

                float candidateScore = ApparelSelector.Score(pawn, candidate, role);
                if (candidateScore <= 0f) continue;

                float currentScore = current != null ? ApparelSelector.Score(pawn, current, role) : 0f;
                if (!EquipPolicy.IsBetterApparel(candidateScore, currentScore, AESettings.equipMinImprovementPct))
                    continue;

                float delta = candidateScore - currentScore;
                if (bestItem == null || delta > bestDelta)
                {
                    bestItem = candidate;
                    bestCurrent = current;
                    bestDelta = delta;
                }
            }

            if (bestItem == null) return false;

            string change = bestCurrent != null
                ? "换装：" + DescribeThing(bestCurrent) + " → " + DescribeThing(bestItem)
                : "装备服装：" + DescribeThing(bestItem);
            if (!TryDispatch(pawn, JobDefOf.Wear, bestItem)) return false;
            RecordChange(pawn, change);
            return true;
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
