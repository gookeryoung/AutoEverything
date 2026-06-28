using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using AutoEquipment.Scoring;

namespace AutoEquipment
{
    public class CompProperties_GearManager : CompProperties
    {
        public CompProperties_GearManager() { compClass = typeof(CompGearManager); }
    }

    /// <summary>
    /// 挂载在 Pawn 上的装备管理组件：周期性评估角色、情境与可用装备。
    /// </summary>
    public class CompGearManager : ThingComp
    {
        // 缓存 TraitDef 查找，避免 Tick 路径每次重复字典查询
        private static readonly TraitDef nudistDef = DefDatabase<TraitDef>.GetNamed("Nudist", false);

        // 缓存角色（周期性重算）
        public Role cachedRole = Role.Default;
        private int roleCacheTick = -9999;
        private const int RoleCacheInterval = 2500;

        // 上一次情境（用于检测情境变化）
        private GearContext lastContext = GearContext.Normal;

        // 副武器追踪
        public Thing sidearm;
        public Thing primaryWeapon;

        // 锁定：玩家可锁定 Pawn 以禁用自动装备
        public bool locked;

        // 冷却：防止反复拾取药品
        private int lastMedPickupTick = -9999;

        // 玩家可手动覆盖角色
        public bool overrideRole;
        public Role manualRole = Role.Default;

        private int tickOffset = -1;

        // 食尸鬼装备清理：首次 Tick 立即执行一次，覆盖游戏加载场景
        // 无需存档：每次加载游戏都重新检查，确保旧存档遗留装备被清理
        private bool initialGhoulCheckDone;
        // 食尸鬼装备复查间隔：5 分钟 = 18000 tick
        // 5 分钟足够低频避免性能损失，又能及时发现玩家手动装备的异常情况
        private const int GhoulCleanInterval = 18000;

        public Pawn Pawn => (Pawn)parent;

        public Role CurrentRole
        {
            get
            {
                if (overrideRole) return manualRole;
                int tick = Find.TickManager.TicksGame;
                if (tick - roleCacheTick > RoleCacheInterval)
                {
                    cachedRole = RoleDetector.DetectRole(Pawn);
                    roleCacheTick = tick;
                }
                return cachedRole;
            }
        }

        public override void CompTick()
        {
            if (!AESettings.enabled || locked) return;
            if (Pawn.Dead || Pawn.Downed || Pawn.Map == null) return;

            // 兜底防御：旧存档可能已注入动物/机械族等不适用类别的 Comp，
            // 在此一次性移除并返回，避免 Tick 路径持续空转
            if (!PawnSuitabilityChecker.CanManageGear(Pawn))
            {
                // 静默移除：动物等不适用 Pawn 不应在装备管理中
                if (parent.AllComps.Contains(this))
                    parent.AllComps.Remove(this);
                return;
            }

            // 仅管理玩家阵营的装备，访客不处理
            if (Pawn.Faction != Faction.OfPlayer) return;
            if (Pawn.IsPrisoner) return;
            if (QuestUtility.IsQuestLodger(Pawn)) return; // 临时任务成员

            // 食尸鬼（Anomaly DLC 变异体）无法使用武器装备，必须完全跳过装备管理
            // 但需周期性清理其身上残留的装备（玩家手动装备或旧存档遗留）
            if (DLCCompat.IsGhoul(Pawn))
            {
                // 首次 Tick 立即清理一次（覆盖游戏加载场景），之后每 5 分钟复查
                // 使用 thingIDNumber 分散检查时机，避免所有食尸鬼同 tick 触发造成卡顿
                if (!initialGhoulCheckDone)
                {
                    initialGhoulCheckDone = true;
                    CleanGhoulEquipment();
                    return;
                }
                if ((Find.TickManager.TicksGame + Pawn.thingIDNumber) % GhoulCleanInterval != 0) return;
                CleanGhoulEquipment();
                return;
            }

            bool isSlave = DLCCompat.IsSlave(Pawn);
            bool isChild = DLCCompat.IsChild(Pawn);

            // 快速路径：征召时副武器检查每 30 tick 执行一次（非 500 tick）
            // 战斗时间紧迫，敌人靠近时需立即切换近战
            if (Pawn.Drafted)
            {
                if (AESettings.sidearms && AESettings.autoMeleeSidearm && !isChild
                    && (Find.TickManager.TicksGame + Pawn.thingIDNumber) % 30 == 0)
                {
                    try
                    {
                        Log.Message($"[AutoEquipment] {Pawn.LabelShort} 征召副武器检查 (role={CurrentRole}, weapon={Pawn.equipment?.Primary?.LabelShort ?? "none"})");
                        CheckMeleeSidearm(CurrentRole);
                    }
                    catch (Exception ex)
                    {
                        Log.ErrorOnce("[AutoEquipment] 副武器检查失败 " + Pawn.LabelShort + ": " + ex.Message,
                            Pawn.thingIDNumber ^ 0x5348);
                    }
                }
                return;
            }

            if (tickOffset < 0)
                tickOffset = parent.thingIDNumber % AESettings.evaluateInterval;
            if ((Find.TickManager.TicksGame + tickOffset) % AESettings.evaluateInterval != 0) return;

            try
            {
                // 安全检查：检测并修复装备槽中的非武器物品
                FixBogusEquipment();

                GearContext context = ContextDetector.GetContext(Pawn);
                Role role = CurrentRole;

                // 情境变化触发立即装备评估
                GearContext prevContext = lastContext;
                bool contextChanged = context != prevContext;
                lastContext = context;

                Log.Message($"[AutoEquipment] {Pawn.LabelShort} 评估 tick: role={role}, context={context}, contextChanged={contextChanged}, weapon={Pawn.equipment?.Primary?.LabelShort ?? "none"}, isSlave={isSlave}, isChild={isChild}");

                // 不打断医疗工作：治疗、手术、救援
                // TryTakeOrderedJob 会取消当前工作，导致医生把药品装进口袋并陷入死循环
                if (IsDoingMedicalJob())
                {
                    Log.Message($"[AutoEquipment] {Pawn.LabelShort} 跳过评估：正在执行医疗工作 ({Pawn.CurJob?.def?.defName})");
                    return;
                }

                // 未征召：正常自动管理装备
                // 未成年：仅防具（无武器、副武器、药品）
                // 奴隶：武器+防具+药品，但无副武器（殖民地级分配优先级较低）
                // 关键修复：Pawn 无武器时也必须评估，否则格斗家永远拿不到近战武器
                // （情境变化 contextChanged 仅在 Combat/Work 切换时为 true，普通 colonist 默认 Work 不会变化）
                Thing currentWeapon = Pawn.equipment?.Primary;
                bool needWeaponEval = AESettings.autoWeapons && !isChild
                    && (contextChanged || currentWeapon == null);
                if (needWeaponEval)
                {
                    if (contextChanged)
                        Log.Message($"[AutoEquipment] {Pawn.LabelShort} 执行 EvaluateWeapon (情境变化 {prevContext}->{context})");
                    else
                        Log.Message($"[AutoEquipment] {Pawn.LabelShort} 执行 EvaluateWeapon (无武器，强制评估)");
                    EvaluateWeapon(role, context, contextChanged);
                }

                // 防具仅在显著情境变化时触发：
                // 进入/离开 Combat、Cold、Hot。Work<->Normal 切换不触发换装
                bool apparelContextChanged = contextChanged
                    && (context == GearContext.Combat || context == GearContext.Cold
                        || context == GearContext.Hot
                        || prevContext == GearContext.Combat || prevContext == GearContext.Cold
                        || prevContext == GearContext.Hot);
                if (AESettings.autoApparel)
                {
                    Log.Message($"[AutoEquipment] {Pawn.LabelShort} 执行 EvaluateApparel (apparelContextChanged={apparelContextChanged})");
                    EvaluateApparel(role, context, apparelContextChanged);
                }

                if (AESettings.autoInventory && !isChild)
                {
                    Log.Message($"[AutoEquipment] {Pawn.LabelShort} 执行 EvaluateInventory");
                    EvaluateInventory(role);
                }

                // 副武器仅对殖民者（非奴隶、非未成年）
                if (AESettings.sidearms && !isSlave && !isChild)
                {
                    Log.Message($"[AutoEquipment] {Pawn.LabelShort} 执行 EvaluateSidearm");
                    EvaluateSidearm(role);
                }

                // 副武器近战切换在上方征召块中处理
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEquipment] 装备评估失败 " + Pawn.LabelShort + ": " + ex.Message,
                    Pawn.thingIDNumber ^ 0x5347);
            }
        }

        // ===================== 医疗工作守卫 =====================

        private bool IsDoingMedicalJob()
        {
            var job = Pawn.CurJob;
            if (job == null) return false;
            var def = job.def;
            return def == JobDefOf.TendPatient
                || def == JobDefOf.TendEntity
                || def == JobDefOf.Rescue
                || def == JobDefOf.TakeToBedToOperate;
        }

        // ===================== 食尸鬼装备清理 =====================

        /// <summary>
        /// 清理食尸鬼身上的武器与防具。
        /// 食尸鬼无法有效使用装备，玩家手动装备或旧存档遗留的物品应当被自动卸下。
        /// 该方法低频调用（首次 Tick + 每 5 分钟），不影响 Tick 性能。
        /// </summary>
        private void CleanGhoulEquipment()
        {
            // 卸下主武器
            ThingWithComps primary = Pawn.equipment?.Primary;
            if (primary != null)
            {
                Pawn.equipment.TryDropEquipment(primary, out ThingWithComps dropped, Pawn.Position, false);
                Log.Message("[AutoEquipment] 食尸鬼 " + Pawn.LabelShort + " 自动卸下武器 " + primary.LabelShort);
            }

            // 脱下所有防具（从后往前遍历，移除时索引不错位）
            if (Pawn.apparel != null && Pawn.apparel.WornApparel.Count > 0)
            {
                for (int i = Pawn.apparel.WornApparel.Count - 1; i >= 0; i--)
                {
                    Apparel a = Pawn.apparel.WornApparel[i];
                    if (Pawn.apparel.TryDrop(a))
                    {
                        Log.Message("[AutoEquipment] 食尸鬼 " + Pawn.LabelShort + " 自动脱下 " + a.LabelShort);
                    }
                }
            }
        }

        // ===================== 手动触发换装 =====================

        /// <summary>
        /// 换装目标类型：决定强制评估哪一类装备。
        /// </summary>
        public enum ReloadTarget
        {
            All,        // 武器+防具+副武器+库存
            Weapon,     // 仅主武器
            Apparel,    // 仅防具
            Sidearm,    // 仅副武器
            Inventory   // 仅库存（药品）
        }

        /// <summary>
        /// 实例级强制评估：忽略情境变化与冷却，立即按当前 role/context 评估指定类型。
        /// 用于调试按钮即时验证评分逻辑。
        /// </summary>
        public void ForceEvaluate(ReloadTarget target)
        {
            if (Pawn == null || Pawn.Dead || Pawn.Map == null) return;
            if (DLCCompat.IsGhoul(Pawn)) return;

            try
            {
                Role role = CurrentRole;
                GearContext context = ContextDetector.GetContext(Pawn);
                lastContext = context;

                switch (target)
                {
                    case ReloadTarget.Weapon:
                        EvaluateWeapon(role, context, true);
                        break;
                    case ReloadTarget.Apparel:
                        EvaluateApparel(role, context, true);
                        break;
                    case ReloadTarget.Sidearm:
                        EvaluateSidearm(role);
                        break;
                    case ReloadTarget.Inventory:
                        EvaluateInventory(role);
                        break;
                    case ReloadTarget.All:
                        EvaluateWeapon(role, context, true);
                        EvaluateApparel(role, context, true);
                        EvaluateSidearm(role);
                        EvaluateInventory(role);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEquipment] 手动换装失败 " + Pawn.LabelShort + ": " + ex.Message,
                    Pawn.thingIDNumber ^ 0x5349);
            }
        }

        /// <summary>
        /// 全局手动触发换装：遍历所有地图的玩家阵营殖民者，
        /// 强制评估全部装备（武器+防具+副武器+库存）。
        /// 返回被触发的 Pawn 数量。
        /// </summary>
        public static int ReloadAllColonists()
        {
            int triggered = 0;
            int scanned = 0;
            int skippedGhoul = 0;
            int skippedComp = 0;
            int skippedUnsuitable = 0;

            foreach (Map map in Find.Maps)
            {
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (pawn.Faction != Faction.OfPlayer) continue;
                    if (!pawn.IsColonist) continue;
                    if (pawn.Dead || pawn.Downed) continue;
                    scanned++;

                    // 适配性过滤：动物/机械族/昆虫等不参与手动换装
                    if (!PawnSuitabilityChecker.CanManageGear(pawn))
                    {
                        skippedUnsuitable++;
                        continue;
                    }

                    if (DLCCompat.IsGhoul(pawn)) { skippedGhoul++; continue; }

                    var comp = pawn.GetComp<CompGearManager>();
                    if (comp == null) { skippedComp++; continue; }

                    comp.ForceEvaluate(ReloadTarget.All);
                    triggered++;
                }
            }

            Log.Message($"[AutoEquipment] 手动换装统计: 扫描={scanned}, 触发={triggered}, " +
                $"跳过(食尸鬼={skippedGhoul}, 无Comp={skippedComp}, 不适用类别={skippedUnsuitable})");
            return triggered;
        }

        // ===================== 异常装备修复 =====================

        /// <summary>
        /// 检测装备槽是否存放非武器物品（木材、钢铁、食物等）并移除。
        /// 此问题可能由搬运/库存异常导致。
        /// </summary>
        private void FixBogusEquipment()
        {
            ThingWithComps equipped = Pawn.equipment?.Primary;
            if (equipped == null) return;

            bool isRanged = equipped.def.IsRangedWeapon;
            bool isMelee = equipped.def.IsMeleeWeapon;
            bool isWeapon = equipped.def.IsWeapon;

            if (!isRanged && !isMelee || equipped.def.IsStuff)
            {
                // 这不是真实武器（或属于材料如木材），需移除
                AEDebug.Log("[AutoEquipment] WARN: 异常装备 " + Pawn.LabelShort
                    + ": '" + equipped.def.defName + "' (label=" + equipped.def.label
                    + " IsWeapon=" + isWeapon
                    + " IsRanged=" + isRanged
                    + " IsMelee=" + isMelee
                    + " category=" + equipped.def.category
                    + " thingClass=" + equipped.def.thingClass?.Name
                    + "). 立即丢弃. CurJob=" + (Pawn.CurJob?.def?.defName ?? "none")
                    + " LastJob=" + (Pawn.jobs?.curDriver?.GetType()?.Name ?? "none"));

                Pawn.equipment.TryDropEquipment(equipped, out ThingWithComps dropped, Pawn.Position, false);
            }
        }

        // ===================== 武器 =====================

        private void EvaluateWeapon(Role role, GearContext context, bool contextChanged)
        {
            Thing currentWeapon = Pawn.equipment?.Primary;
            // 监测路径：用 WithBreakdown 版本，否则用快速版本
            bool monitorWeapon = DebugMonitor.monitorEnabled && DebugMonitor.monitorWeaponScore;
            float currentScore;
            ScoreBreakdown currentBreakdown = null;
            if (monitorWeapon && currentWeapon != null)
            {
                currentBreakdown = GearScorer.ScoreWeaponWithBreakdown(Pawn, currentWeapon, role, context);
                currentScore = currentBreakdown.Vetoed ? currentBreakdown.VetoScore : currentBreakdown.Total;
            }
            else
            {
                currentScore = currentWeapon != null
                    ? GearScorer.ScoreWeapon(Pawn, currentWeapon, role, context) : -500f;
            }

            Log.Message($"[AutoEquipment] {Pawn.LabelShort} EvaluateWeapon: current={currentWeapon?.LabelShort ?? "none"} score={currentScore:F1}, role={role}, context={context}, contextChanged={contextChanged}");

            // 监测：当前武器评分
            if (monitorWeapon && currentWeapon != null && currentBreakdown != null)
            {
                DebugMonitor.ReportWeaponScore(Pawn, currentWeapon, currentBreakdown, null, 0f);
            }

            // 寻找地图上最佳武器
            Thing bestWeapon = null;
            float bestScore = currentScore;
            float threshold = contextChanged ? 0f : AESettings.upgradeThreshold;
            int candidatesChecked = 0;
            int candidatesSkipped = 0;

            // 扫描地图上的武器
            foreach (Thing thing in Pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon))
            {
                // 仅考虑真实武器，排除武器组中的材料或物品
                if (!thing.def.IsWeapon) continue;
                if (!thing.def.IsRangedWeapon && !thing.def.IsMeleeWeapon) continue;
                if (thing.def.IsStuff) continue; // 木材、钢铁等非武器
                if (thing.IsForbidden(Pawn)) { candidatesSkipped++; continue; }
                if (!Pawn.CanReserve(thing) || !Pawn.CanReach(thing, PathEndMode.ClosestTouch, Danger.Some)) { candidatesSkipped++; continue; }
                if (thing.def.IsRangedWeapon && Pawn.WorkTagIsDisabled(WorkTags.Violent)) { candidatesSkipped++; continue; }
                if (thing.def.IsMeleeWeapon && Pawn.WorkTagIsDisabled(WorkTags.Violent)) { candidatesSkipped++; continue; }

                candidatesChecked++;
                // 监测路径：用 WithBreakdown 版本
                float score;
                ScoreBreakdown breakdown = null;
                if (monitorWeapon)
                {
                    breakdown = GearScorer.ScoreWeaponWithBreakdown(Pawn, thing, role, context);
                    score = breakdown.Vetoed ? breakdown.VetoScore : breakdown.Total;
                }
                else
                {
                    score = GearScorer.ScoreWeapon(Pawn, thing, role, context);
                }

                float minDelta = Math.Max(bestScore * threshold, 10f);
                if (score > bestScore + minDelta)
                {
                    bestScore = score;
                    bestWeapon = thing;

                    // 监测：候选武器评分（仅记录成为最佳候选的）
                    if (monitorWeapon && breakdown != null)
                    {
                        DebugMonitor.ReportWeaponScore(Pawn, thing, breakdown, currentWeapon, currentScore);
                    }
                }
            }

            if (bestWeapon != null && bestWeapon != currentWeapon)
            {
                Log.Message($"[AutoEquipment] {Pawn.LabelShort} EvaluateWeapon 决策: 切换到 '{bestWeapon.LabelShort}' (score={bestScore:F1}) 从 '{currentWeapon?.LabelShort ?? "none"}' (score={currentScore:F1}). 检查 {candidatesChecked} 件武器, 跳过 {candidatesSkipped}");
                var job = JobMaker.MakeJob(JobDefOf.Equip, bestWeapon);
                Pawn.jobs.TryTakeOrderedJob(job, Verse.AI.JobTag.Misc);

                // 监测：上报换装事件
                DebugMonitor.ReportSwap(Pawn, "武器", currentWeapon, bestWeapon, currentScore, bestScore);
            }
            else
            {
                Log.Message($"[AutoEquipment] {Pawn.LabelShort} EvaluateWeapon: 保留当前武器. 检查 {candidatesChecked} 候选, 跳过 {candidatesSkipped}, 无超越阈值");
            }
        }

        // ===================== 防具 =====================

        private void EvaluateApparel(Role role, GearContext context, bool contextChanged)
        {
            if (Pawn.apparel == null) return;

            // 即使情境变化也不每 tick 评估，防具更换较慢
            if (!contextChanged && (Find.TickManager.TicksGame + tickOffset) % (AESettings.evaluateInterval * 3) != 0)
                return;

            // 检查意识形态裸体偏好
            bool prefersNudity = false;
            if (Pawn.Ideo != null)
            {
                foreach (var precept in Pawn.Ideo.PreceptsListForReading)
                {
                    if (precept.def.defName.Contains("Nudity") && precept.def.defName.Contains("Approved"))
                        prefersNudity = true;
                }
            }
            if (nudistDef != null && Pawn.story?.traits?.HasTrait(nudistDef) == true)
                prefersNudity = true;

            if (prefersNudity)
            {
                Log.Message($"[AutoEquipment] {Pawn.LabelShort} EvaluateApparel: 跳过（偏好裸体）");
                return;
            }

            int wornCount = Pawn.apparel.WornApparel.Count;
            Log.Message($"[AutoEquipment] {Pawn.LabelShort} EvaluateApparel: role={role}, context={context}, contextChanged={contextChanged}, 穿戴 {wornCount} 件");

            // 寻找最佳可用防具（非首个满足阈值的）
            Apparel bestApparel = null;
            float bestScore = -999f;
            float bestWornScore = 0f;
            int candidatesChecked = 0;

            foreach (Thing thing in Pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Apparel))
            {
                if (!(thing is Apparel apparel)) continue;
                if (apparel.IsForbidden(Pawn)) continue;
                if (!Pawn.CanReserve(apparel) || !Pawn.CanReach(apparel, PathEndMode.ClosestTouch, Danger.Some)) continue;

                // 检查 Pawn 是否能穿戴（身体部位、性别）
                if (!ApparelUtility.HasPartsToWear(Pawn, apparel.def)) continue;
                if (apparel.def.apparel?.gender != Gender.None && apparel.def.apparel.gender != Pawn.gender) continue;
                var bioApp = apparel.TryGetComp<CompBiocodable>();
                if (bioApp != null && bioApp.Biocoded && bioApp.CodedPawn != Pawn) continue;

                // 遵守装备策略限制
                if (Pawn.outfits?.CurrentApparelPolicy?.filter != null
                    && !Pawn.outfits.CurrentApparelPolicy.filter.Allows(apparel))
                    continue;

                candidatesChecked++;
                // 监测路径：用 WithBreakdown 版本
                bool monitorApparel = DebugMonitor.monitorEnabled && DebugMonitor.monitorApparelScore;
                float newScore;
                ScoreBreakdown newBreakdown = null;
                if (monitorApparel)
                {
                    newBreakdown = GearScorer.ScoreApparelWithBreakdown(Pawn, apparel, role, context);
                    newScore = newBreakdown.Vetoed ? newBreakdown.VetoScore : newBreakdown.Total;
                }
                else
                {
                    newScore = GearScorer.ScoreApparel(Pawn, apparel, role, context);
                }

                if (newScore <= 0f || newScore <= bestScore) continue;

                // 与同槽位已穿戴防具比较
                bool blocked = false;
                float conflictWornScore = 0f;
                Apparel conflictWorn = null;
                foreach (Apparel worn in Pawn.apparel.WornApparel)
                {
                    if (!ApparelUtility.CanWearTogether(worn.def, apparel.def, Pawn.RaceProps.body))
                    {
                        if (Pawn.apparel.IsLocked(worn)) { blocked = true; break; }
                        float ws = GearScorer.ScoreApparel(Pawn, worn, role, context);
                        if (ws > conflictWornScore)
                        {
                            conflictWornScore = ws;
                            conflictWorn = worn;
                        }
                    }
                }
                if (blocked) continue;

                // 必须以阈值超越已穿戴评分
                if (newScore <= conflictWornScore * (1f + AESettings.upgradeThreshold)) continue;

                bestApparel = apparel;
                bestScore = newScore;
                bestWornScore = conflictWornScore;

                // 监测：候选防具评分
                if (monitorApparel && newBreakdown != null)
                {
                    DebugMonitor.ReportApparelScore(Pawn, apparel, newBreakdown, conflictWorn, conflictWornScore);
                }
            }

            if (bestApparel != null)
            {
                // 构建冲突防具日志（避免 Tick 路径中 LINQ，改用 for 循环）
                string conflictInfo = BuildConflictApparelInfo(role, context, bestApparel);
                Log.Message($"[AutoEquipment] {Pawn.LabelShort} EvaluateApparel 决策: 切换到 {bestApparel.LabelShort} (score={bestScore:F1}) 替换穿戴 (score={bestWornScore:F1}, threshold={AESettings.upgradeThreshold:F2}). 检查 {candidatesChecked} 候选. 冲突防具: {conflictInfo}");
                var job = JobMaker.MakeJob(JobDefOf.Wear, bestApparel);
                Pawn.jobs.TryTakeOrderedJob(job, Verse.AI.JobTag.Misc);

                // 监测：上报换装事件
                DebugMonitor.ReportSwap(Pawn, "防具", null, bestApparel, bestWornScore, bestScore);
            }
            else
            {
                Log.Message($"[AutoEquipment] {Pawn.LabelShort} EvaluateApparel: 无升级. 检查 {candidatesChecked} 候选");
            }
        }

        /// <summary>
        /// 构建冲突防具日志字符串（使用 for 循环替代 LINQ，避免 Tick 路径闭包分配）。
        /// </summary>
        private string BuildConflictApparelInfo(Role role, GearContext context, Apparel bestApparel)
        {
            var sb = new System.Text.StringBuilder();
            var worn = Pawn.apparel.WornApparel;
            bool first = true;
            for (int i = 0; i < worn.Count; i++)
            {
                Apparel w = worn[i];
                if (!ApparelUtility.CanWearTogether(w.def, bestApparel.def, Pawn.RaceProps.body))
                {
                    if (!first) sb.Append(", ");
                    sb.Append(w.LabelShort).Append("=").Append(GearScorer.ScoreApparel(Pawn, w, role, context).ToString("F1"));
                    first = false;
                }
            }
            return sb.ToString();
        }

        // ===================== 库存 =====================

        private void EvaluateInventory(Role role)
        {
            if (!AESettings.carryMedicine) return;

            // 防止反复拾取药品——每次尝试后冷却
            if (Find.TickManager.TicksGame - lastMedPickupTick < 2500) return;

            // 不打断当前工作去拾取药品
            if (Pawn.CurJob != null && Pawn.CurJob.def == JobDefOf.TakeCountToInventory)
                return;

            // 医生与有医疗技能的战斗人员应携带药品
            int medSkill = Pawn.skills?.GetSkill(SkillDefOf.Medicine)?.Level ?? 0;
            bool shouldCarryMeds = role == Role.Doctor
                || (medSkill >= 4 && !Pawn.WorkTagIsDisabled(WorkTags.Caring));

            if (!shouldCarryMeds)
            {
                Log.Message($"[AutoEquipment] {Pawn.LabelShort} EvaluateInventory: 跳过 (role={role}, medSkill={medSkill}, shouldCarry=false)");
                return;
            }

            // 仅统计库存中的药品（不含手持——手持药品是临时的，
            // 用于治疗或搬运工作，统计会导致反复拾取/丢弃死循环）
            int medsInInventory = 0;
            foreach (Thing item in Pawn.inventory.innerContainer)
            {
                if (item.def.IsMedicine)
                    medsInInventory += item.stackCount;
            }

            // 携带过多时丢弃多余药品（如搬运时混入）
            if (medsInInventory > AESettings.medicineCount)
            {
                int excess = medsInInventory - AESettings.medicineCount;
                Log.Message($"[AutoEquipment] {Pawn.LabelShort} EvaluateInventory: 丢弃 {excess} 件多余药品 (持有 {medsInInventory}, 上限 {AESettings.medicineCount})");
                var inv = Pawn.inventory.innerContainer;
                for (int i = inv.Count - 1; i >= 0 && excess > 0; i--)
                {
                    if (inv[i].def.IsMedicine)
                    {
                        int drop = Math.Min(excess, inv[i].stackCount);
                        if (inv.TryDrop(inv[i], Pawn.Position, Pawn.Map, ThingPlaceMode.Near, drop, out _))
                        {
                            Log.Message($"[AutoEquipment] {Pawn.LabelShort} 丢弃 {drop}x {inv[i].def.label}");
                            excess -= drop;
                        }
                    }
                }
                return;
            }

            if (medsInInventory >= AESettings.medicineCount)
            {
                Log.Message($"[AutoEquipment] {Pawn.LabelShort} EvaluateInventory: 已满 ({medsInInventory}/{AESettings.medicineCount})");
                return;
            }

            int needed = AESettings.medicineCount - medsInInventory;
            if (needed <= 0) return;

            // 寻找药品拾取（不在自身库存中）
            Thing bestMed = GenClosest.ClosestThingReachable(
                Pawn.Position, Pawn.Map,
                ThingRequest.ForGroup(ThingRequestGroup.Medicine),
                PathEndMode.ClosestTouch,
                TraverseParms.For(Pawn),
                30f,
                t => !t.IsForbidden(Pawn) && Pawn.CanReserve(t) && t.stackCount > 0
                    && !Pawn.inventory.innerContainer.Contains(t));

            if (bestMed != null)
            {
                int pickupCount = Math.Min(needed, bestMed.stackCount);
                Log.Message($"[AutoEquipment] {Pawn.LabelShort} EvaluateInventory: 拾取 {pickupCount}x {bestMed.def.label} (持有 {medsInInventory}, 需 {needed})");

                var job = JobMaker.MakeJob(JobDefOf.TakeCountToInventory, bestMed);
                job.count = pickupCount;
                Pawn.jobs.TryTakeOrderedJob(job, Verse.AI.JobTag.Misc);

                lastMedPickupTick = Find.TickManager.TicksGame;
            }
            else
            {
                Log.Message($"[AutoEquipment] {Pawn.LabelShort} EvaluateInventory: 附近无药品");
            }
        }

        // ===================== 副武器 =====================

        /// <summary>
        /// 确保 Pawn 库存中持有副武器（与主武器类型相反）。
        /// 在常规装备评估时调用。
        /// </summary>
        private void EvaluateSidearm(Role role)
        {
            if (!AESettings.sidearms) return;
            if (Pawn.WorkTagIsDisabled(WorkTags.Violent)) return;

            Thing primary = Pawn.equipment?.Primary;
            if (primary == null)
            {
                Log.Message($"[AutoEquipment] {Pawn.LabelShort} EvaluateSidearm: 跳过（无主武器）");
                return;
            }

            // 检查库存中是否已持有副武器
            bool hasMeleeSidearm = false;
            bool hasRangedSidearm = false;
            foreach (Thing item in Pawn.inventory.innerContainer)
            {
                if (item.def.IsMeleeWeapon) hasMeleeSidearm = true;
                if (item.def.IsRangedWeapon) hasRangedSidearm = true;
            }

            // 确定需要的副武器类型
            bool needMelee = primary.def.IsRangedWeapon && !hasMeleeSidearm;
            bool needRanged = primary.def.IsMeleeWeapon && !hasRangedSidearm;

            if (!needMelee && !needRanged)
            {
                Log.Message($"[AutoEquipment] {Pawn.LabelShort} EvaluateSidearm: 已有副武器 (primary={primary.LabelShort}, hasMelee={hasMeleeSidearm}, hasRanged={hasRangedSidearm})");
                return;
            }

            Log.Message($"[AutoEquipment] {Pawn.LabelShort} EvaluateSidearm: 寻找 {(needMelee ? "近战" : "远程")} 副武器 (primary={primary.LabelShort})");

            // 寻找地图上最佳副武器
            Thing bestSidearm = null;
            float bestScore = 0f;
            int candidatesChecked = 0;

            foreach (Thing weapon in Pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon))
            {
                if (weapon.IsForbidden(Pawn)) continue;
                if (!Pawn.CanReserve(weapon)) continue;
                if (weapon.Position.DistanceTo(Pawn.Position) > 30f) continue;
                if (!Pawn.CanReach(weapon, PathEndMode.ClosestTouch, Danger.Some)) continue;

                if (needMelee && !weapon.def.IsMeleeWeapon) continue;
                if (needRanged && !weapon.def.IsRangedWeapon) continue;

                candidatesChecked++;
                float score = GearScorer.ScoreSidearm(Pawn, weapon, role);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestSidearm = weapon;
                }
            }

            if (bestSidearm != null)
            {
                Log.Message($"[AutoEquipment] {Pawn.LabelShort} EvaluateSidearm 决策: 拾取 '{bestSidearm.LabelShort}' 作为副武器 (score={bestScore:F1}, 检查 {candidatesChecked} 候选)");
                // 将副武器放入库存
                var job = JobMaker.MakeJob(JobDefOf.TakeCountToInventory, bestSidearm);
                job.count = 1;
                Pawn.jobs.TryTakeOrderedJob(job, Verse.AI.JobTag.Misc);
            }
            else
            {
                Log.Message($"[AutoEquipment] {Pawn.LabelShort} EvaluateSidearm: 无合适 {(needMelee ? "近战" : "远程")} 副武器 (检查 {candidatesChecked})");
            }
        }

        private void CheckMeleeSidearm(Role role)
        {
            if (!ContextDetector.IsUnderMeleeAttack(Pawn))
            {
                Log.Message($"[AutoEquipment] {Pawn.LabelShort} CheckMeleeSidearm: 未受近战攻击");
                return;
            }

            Thing currentWeapon = Pawn.equipment?.Primary;
            if (currentWeapon == null)
            {
                Log.Message($"[AutoEquipment] {Pawn.LabelShort} CheckMeleeSidearm: 受近战攻击但未装备武器");
                return;
            }

            // 已使用近战武器则无需切换
            if (currentWeapon.def.IsMeleeWeapon)
            {
                Log.Message($"[AutoEquipment] {Pawn.LabelShort} CheckMeleeSidearm: 已使用近战 ({currentWeapon.LabelShort})");
                return;
            }

            Log.Message($"[AutoEquipment] {Pawn.LabelShort} CheckMeleeSidearm: 持远程武器受近战攻击 ({currentWeapon.LabelShort}), 搜索库存近战副武器");

            // 在库存中寻找最佳近战武器
            Thing bestMelee = null;
            float bestScore = 0f;

            foreach (Thing item in Pawn.inventory.innerContainer)
            {
                if (!item.def.IsMeleeWeapon) continue;
                float score = GearScorer.ScoreSidearm(Pawn, item, role);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestMelee = item;
                }
            }

            if (bestMelee != null)
            {
                // 绝不替换生物编码/人格绑定武器
                var bio = (currentWeapon as ThingWithComps)?.TryGetComp<CompBiocodable>();
                if (bio != null && bio.Biocoded)
                {
                    Log.Message($"[AutoEquipment] {Pawn.LabelShort} CheckMeleeSidearm: 不替换生物编码武器 ({currentWeapon.LabelShort})");
                    return;
                }

                Log.Message($"[AutoEquipment] {Pawn.LabelShort} CheckMeleeSidearm 决策: 抽出近战副武器 '{bestMelee.LabelShort}' (score={bestScore:F1}), 收起远程 '{currentWeapon.LabelShort}'");

                // 保存当前武器为主武器（稍后重新装备）
                primaryWeapon = currentWeapon;

                // 切换：卸下远程，装备库存近战
                Pawn.equipment.TryDropEquipment(currentWeapon as ThingWithComps, out ThingWithComps droppedWep, Pawn.Position);
                if (droppedWep != null)
                {
                    if (droppedWep.Spawned)
                        droppedWep.DeSpawn();
                    if (!Pawn.inventory.innerContainer.TryAdd(droppedWep))
                        GenPlace.TryPlaceThing(droppedWep, Pawn.Position, Pawn.Map, ThingPlaceMode.Near);
                }

                Pawn.inventory.innerContainer.Remove(bestMelee);
                Pawn.equipment.AddEquipment(bestMelee as ThingWithComps);

                sidearm = bestMelee;
            }
            else
            {
                Log.Message($"[AutoEquipment] {Pawn.LabelShort} CheckMeleeSidearm: 受近战攻击但库存无近战副武器");
            }
        }

        /// <summary>
        /// 取消征召时调用。若曾抽出副武器则恢复主武器。
        /// </summary>
        public void OnUndraft()
        {
            if (sidearm == null || primaryWeapon == null)
            {
                Log.Message($"[AutoEquipment] {Pawn.LabelShort} OnUndraft: 无副武器/主武器可恢复 (sidearm={sidearm?.LabelShort ?? "null"}, primary={primaryWeapon?.LabelShort ?? "null"})");
                return;
            }
            if (Pawn.Map == null)
            {
                Log.Warning($"[AutoEquipment] {Pawn.LabelShort} OnUndraft: Pawn 无地图, 清空副武器状态");
                sidearm = null; primaryWeapon = null; return;
            }

            Log.Message($"[AutoEquipment] {Pawn.LabelShort} OnUndraft: 恢复主武器 '{primaryWeapon.LabelShort}', 收起副武器 '{sidearm.LabelShort}'");

            Thing currentWeapon = Pawn.equipment?.Primary;
            if (currentWeapon == sidearm)
            {
                // 切换回来：卸下副武器，重新装备主武器
                Pawn.equipment.TryDropEquipment(currentWeapon as ThingWithComps, out ThingWithComps droppedSidearm, Pawn.Position);
                if (droppedSidearm != null)
                {
                    if (droppedSidearm.Spawned)
                        droppedSidearm.DeSpawn();
                    if (!Pawn.inventory.innerContainer.TryAdd(droppedSidearm))
                        GenPlace.TryPlaceThing(droppedSidearm, Pawn.Position, Pawn.Map, ThingPlaceMode.Near);
                }
            }
            else
            {
                Log.Warning($"[AutoEquipment] {Pawn.LabelShort} OnUndraft: 当前武器 '{currentWeapon?.LabelShort ?? "none"}' 非副武器 '{sidearm.LabelShort}' —— 武器可能已丢失");
            }

            // 从库存重新装备主武器（处理副武器被销毁/丢失情况）
            if (primaryWeapon as ThingWithComps != null
                && Pawn.equipment?.Primary != primaryWeapon
                && Pawn.inventory.innerContainer.Contains(primaryWeapon))
            {
                Log.Message($"[AutoEquipment] {Pawn.LabelShort} OnUndraft: 从库存重新装备主武器 '{primaryWeapon.LabelShort}'");
                Pawn.inventory.innerContainer.Remove(primaryWeapon);
                Pawn.equipment.AddEquipment(primaryWeapon as ThingWithComps);
            }
            else if (primaryWeapon as ThingWithComps != null && !Pawn.inventory.innerContainer.Contains(primaryWeapon))
            {
                Log.Warning($"[AutoEquipment] {Pawn.LabelShort} OnUndraft: 主武器 '{primaryWeapon.LabelShort}' 不在库存中 —— 可能已丢失/销毁");
            }

            sidearm = null;
            primaryWeapon = null;
        }

        // ===================== 存档 =====================

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref locked, "ae_locked", false);
            Scribe_Values.Look(ref overrideRole, "ae_overrideRole", false);
            Scribe_Values.Look(ref manualRole, "ae_manualRole", Role.Default);
            Scribe_References.Look(ref sidearm, "ae_sidearm");
            Scribe_References.Look(ref primaryWeapon, "ae_primaryWeapon");
        }

        public override string CompInspectStringExtra()
        {
            if (!(parent is Pawn)) return null;
            if (!AESettings.enabled || Pawn.Dead) return null;
            return "AE_Role".Translate(CurrentRole.ToString());
        }
    }
}
