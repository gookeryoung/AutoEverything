using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using AutoEverything.AutoEquipment.Scoring;
using Verse.AI;
using AutoEverything.RoleEvaluation;
using AutoEverything.Allocation;
using AutoEverything.Core;

namespace AutoEverything.AutoEquipment
{
    public class CompProperties_GearManager : CompProperties
    {
        public CompProperties_GearManager() { compClass = typeof(CompGearManager); }
    }

    /// <summary>
    /// 挂载在 Pawn 上的装备管理组件：周期性评估角色、情境与可用装备。
    /// </summary>
    public partial class CompGearManager : ThingComp
    {
        // TraitDef 查询统一由 TraitDefCache 提供（集中管理，避免重复定义）

        // 缓存角色（周期性重算）
        public Role cachedRole = Role.Default;
        private int roleCacheTick = -9999;
        private const int RoleCacheInterval = 2500;

        // 上一次情境（用于检测情境变化）
        private GearContext lastContext = GearContext.Normal;

        // 副武器追踪
        public Thing sidearm;
        public Thing primaryWeapon;

        // 锁定：玩家可锁定 Pawn 以禁用自动万物
        public bool locked;

        // 冷却：防止反复拾取药品
        private int lastMedPickupTick = -9999;

        // 玩家可手动覆盖角色
        public bool overrideRole;
        public Role manualRole = Role.Default;

        private int tickOffset = -1;

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
            if (!AESettings.enabled) return;

            // 全局自动执行（工作重配 + 人员评级）：静态门控，每 60 tick 检查一次
            // 放在 locked 检查之前：即使本 Pawn 被锁定，全局自动执行仍应为其他殖民者运行
            AutoExecutor.TryTick();

            if (locked) return;
            if (Pawn.Dead || Pawn.Downed || Pawn.Map == null) return;

            // 兜底防御：旧存档可能已注入动物/机械族等不适用类别的 Comp，
            // 在此一次性移除并返回，避免 Tick 路径持续空转
            // 场景：旧版本 CanManageGearDef 逻辑差异、其他 mod 冲突、玩家控制机械族（Mechinator DLC）
            // 机械族/动物/昆虫被注入 comp 后，Tick 时访问 pawn.skills 等会抛 NRE
            if (!PawnSuitabilityChecker.CanManageGear(Pawn))
            {
                // 静默移除：动物等不适用 Pawn 不应在装备管理中
                if (parent?.AllComps != null && parent.AllComps.Contains(this))
                    parent.AllComps.Remove(this);
                return;
            }

            // 仅管理玩家阵营的装备，访客不处理
            if (Pawn.Faction != Faction.OfPlayer) return;
            if (Pawn.IsPrisoner) return;
            if (QuestUtility.IsQuestLodger(Pawn)) return; // 临时任务成员

            // 食尸鬼（Anomaly DLC 变异体）无法使用武器装备，禁止参与装备管理
            // 设计原则：逻辑上杜绝食尸鬼进入装备管理流程，而非事后清理
            // 玩家手动给食尸鬼装备的物品由玩家自行负责，MOD 不干预
            // 此处兜底处理：旧存档可能已注入 Comp，静默自移除避免 Tick 持续空转
            if (DLCCompat.IsGhoul(Pawn))
            {
                if (parent?.AllComps != null && parent.AllComps.Contains(this))
                    parent.AllComps.Remove(this);
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
                        if (AEDebug.IsActive) AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(Pawn)} 征召副武器检查 (role={CurrentRole}, weapon={Pawn.equipment?.Primary?.LabelShort ?? "none"})");
                        CheckMeleeSidearm(CurrentRole);
                    }
                    catch (Exception ex)
                    {
                        Log.ErrorOnce("[AutoEverything] 副武器检查失败 " + Pawn.LabelShort + ": " + ex.Message,
                            Pawn.thingIDNumber ^ 0x5348);
                    }
                }
                return;
            }

            // 奴隶未征召时不主动找武器装备：跳过所有自动评估
            // 玩家手动给奴隶装备由玩家负责，MOD 不干预
            // 征召时的副武器切换已在上方 Drafted 分支处理（奴隶征召时允许切换）
            if (isSlave) return;

            if (tickOffset < 0)
                tickOffset = parent.thingIDNumber % AESettings.evaluateInterval;
            if ((Find.TickManager.TicksGame + tickOffset) % AESettings.evaluateInterval != 0) return;

            // 定期清理已死亡/消失 Pawn 在 RoleDetector/ContextDetector 字典中的残留条目
            // 内部有 60000 tick 门控，仅周期性执行一次实际清理
            RoleDetector.CleanupDeadPawns();
            ContextDetector.CleanupDeadPawns();

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

                if (AEDebug.IsActive) AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(Pawn)} 评估 tick: role={role}, context={context}, contextChanged={contextChanged}, weapon={Pawn.equipment?.Primary?.LabelShort ?? "none"}, isSlave={isSlave}, isChild={isChild}");

                // 不打断医疗工作与伤员休养：治疗、手术、救援、伤员休养
                // TryTakeOrderedJob 会取消当前工作，导致医生把药品装进口袋并陷入死循环；
                // 伤员休养被打断会取消 LayDown Job，打断免疫力/治疗进度导致重伤者死亡
                // 守卫逻辑统一由 PawnJobGuard 提供，供 GlobalAllocator/WorkAllocator 复用
                if (PawnJobGuard.ShouldSkipForMedical(Pawn))
                {
                    if (AEDebug.IsActive) AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(Pawn)} 跳过评估：正在执行医疗工作或休养 ({Pawn.CurJob?.def?.defName})");
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
                        if (AEDebug.IsActive) AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(Pawn)} 执行 EvaluateWeapon (情境变化 {prevContext}->{context})");
                        else
                        if (AEDebug.IsActive) AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(Pawn)} 执行 EvaluateWeapon (无武器，强制评估)");
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
                    if (AEDebug.IsActive) AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(Pawn)} 执行 EvaluateApparel (apparelContextChanged={apparelContextChanged})");
                    EvaluateApparel(role, context, apparelContextChanged);
                }

                if (AESettings.autoInventory && !isChild)
                {
                    if (AEDebug.IsActive) AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(Pawn)} 执行 EvaluateInventory");
                    EvaluateInventory(role);
                }

                // 副武器仅对殖民者（非奴隶、非未成年）
                if (AESettings.sidearms && !isSlave && !isChild)
                {
                    if (AEDebug.IsActive) AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(Pawn)} 执行 EvaluateSidearm");
                    EvaluateSidearm(role);
                }

                // 副武器近战切换在上方征召块中处理
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] 装备评估失败 " + Pawn.LabelShort + ": " + ex.Message,
                    Pawn.thingIDNumber ^ 0x5347);
            }
        }

        // 医疗/休养守卫统一由 PawnJobGuard 提供（见 Core/PawnJobGuard.cs）

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
            // 不打断医疗工作与伤员休养：手术/治疗执行期间跳过强制评估
            // EvaluateInventory 会 TryTakeOrderedJob 取药，取消当前手术 DoBill Job，导致手术死循环；
            // 伤员休养被打断会取消 LayDown Job，打断免疫力/治疗进度导致重伤者死亡
            if (PawnJobGuard.ShouldSkipForMedical(Pawn)) return;

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
                Log.ErrorOnce("[AutoEverything] 手动换装失败 " + Pawn.LabelShort + ": " + ex.Message,
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

            Log.Message($"[AutoEverything] 手动换装统计: 扫描={scanned}, 触发={triggered}, " +
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
                if (AEDebug.IsActive) AEDebug.Log(() => "[AutoEverything] WARN: 异常装备 " + Pawn.LabelShort
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
            // 翻译角色名：使用 AE_Role_* 键获取本地化文本（如"射手"），避免输出枚举名"Shooter"
            return "AE_Role".Translate() + ": " + ("AE_Role_" + CurrentRole).Translate();
        }
    }
}