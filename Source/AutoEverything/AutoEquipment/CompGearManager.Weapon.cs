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
    public partial class CompGearManager
    {
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

            if (AEDebug.IsActive) AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(Pawn)} EvaluateWeapon: current={currentWeapon?.LabelShort ?? "none"} score={currentScore:F1}, role={role}, context={context}, contextChanged={contextChanged}");

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
                // 禁用暴力的 Pawn 无法使用任何武器，合并检查（L399-401 已保证 IsRangedWeapon || IsMeleeWeapon）
                if (Pawn.WorkTagIsDisabled(WorkTags.Violent)) { candidatesSkipped++; continue; }

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
                // 决策详细信息走 AEDebug.Log（受 debugLogging 开关控制），避免大量 Pawn 同时换装时刷屏
                if (AEDebug.IsActive) AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(Pawn)} EvaluateWeapon 决策详情: 切换到 '{bestWeapon.LabelShort}' (score={bestScore:F1}) 从 '{currentWeapon?.LabelShort ?? "none"}' (score={currentScore:F1}). 检查 {candidatesChecked} 件武器, 跳过 {candidatesSkipped}");
                var job = JobMaker.MakeJob(JobDefOf.Equip, bestWeapon);
                Pawn.jobs.TryTakeOrderedJob(job, Verse.AI.JobTag.Misc);

                // 监测：上报换装事件
                DebugMonitor.ReportSwap(Pawn, "AE_Slot_Weapon".Translate(), currentWeapon, bestWeapon, currentScore, bestScore);
            }
            else
            {
                if (AEDebug.IsActive) AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(Pawn)} EvaluateWeapon: 保留当前武器. 检查 {candidatesChecked} 候选, 跳过 {candidatesSkipped}, 无超越阈值");

                // 过渡武器：空手且无匹配武器时，拾取任意可用武器过渡（格斗者特质+远程例外）
                if (currentWeapon == null)
                {
                    TryFallbackWeapon(role, context);
                }
            }
        }

        /// <summary>
        /// 过渡武器兜底：空手且无匹配武器时，拾取最不差的可用武器作为过渡。
        /// 例外：格斗者特质（Brawler trait）+ 远程武器 → 跳过（拿远程会不开心）。
        /// 设计意图：空手比拿一把不理想的武器更糟；过渡武器在下次评估时会被更好的匹配替换。
        /// 用 ScoreBreakdown.Total 比较（含 Veto 前的技能分），选技能最契合的过渡武器。
        /// 性能：复用 EvaluateFast 的 fastBreakdown，避免每件候选创建 ScoreBreakdown+List。
        /// </summary>
        private void TryFallbackWeapon(Role role, GearContext context)
        {
            Thing fallbackWeapon = null;
            float fallbackScore = -99999f;

            var pipeline = ScoringPipelineFactory.GetWeaponPipeline();
            GearWeights weights = GearPolicyEngine.GetWeights();

            foreach (Thing thing in Pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon))
            {
                if (!thing.def.IsWeapon) continue;
                if (!thing.def.IsRangedWeapon && !thing.def.IsMeleeWeapon) continue;
                if (thing.def.IsStuff) continue;
                if (thing.IsForbidden(Pawn)) continue;
                if (!Pawn.CanReserve(thing) || !Pawn.CanReach(thing, PathEndMode.ClosestTouch, Danger.Some)) continue;
                if (Pawn.WorkTagIsDisabled(WorkTags.Violent)) continue;

                // 格斗者特质+远程=不开心，跳过（用户例外）
                if (Pawn.story?.traits?.HasTrait(TraitDefOf.Brawler) == true && thing.def.IsRangedWeapon) continue;

                // 性能路径：复用 fastBreakdown，读 Total（含 Veto 前的技能分）选最契合的过渡武器
                ScoreBreakdown bd = pipeline.EvaluateFast(Pawn, thing, role, context, weights);
                if (bd.Total > fallbackScore)
                {
                    fallbackScore = bd.Total;
                    fallbackWeapon = thing;
                }
            }

            if (fallbackWeapon != null)
            {
                // 决策日志走 AEDebug.Log，与 EvaluateWeapon 一致，避免 50 Pawn 同时换装刷屏
                if (AEDebug.IsActive) AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(Pawn)} 过渡武器: 空手无匹配, 使用 '{fallbackWeapon.LabelShort}' (rawScore={fallbackScore:F1}, role={role})");
                var job = JobMaker.MakeJob(JobDefOf.Equip, fallbackWeapon);
                Pawn.jobs.TryTakeOrderedJob(job, Verse.AI.JobTag.Misc);
            }
        }

        // ===================== 副武器 =====================

        /// <summary>
        /// 副武器分配入口：委托给全局分配器。
        /// 全局分配器按战斗价值优先级排序，高价值角色（双火高技能）优先，
        /// 地图副武器不足时低价值角色不抢占。
        /// </summary>
        private void EvaluateSidearm(Role role)
        {
            if (!AESettings.sidearms) return;
            if (Pawn.WorkTagIsDisabled(WorkTags.Violent)) return;
            if (Pawn.equipment?.Primary == null) return;

            // 委托给全局分配器：受 2000 tick 周期控制
            SidearmAllocator.AllocateForPawn(Pawn);
        }

        private void CheckMeleeSidearm(Role role)
        {
            if (!ContextDetector.IsUnderMeleeAttack(Pawn))
            {
                if (AEDebug.IsActive) AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(Pawn)} CheckMeleeSidearm: 未受近战攻击");
                return;
            }

            Thing currentWeapon = Pawn.equipment?.Primary;
            if (currentWeapon == null)
            {
                if (AEDebug.IsActive) AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(Pawn)} CheckMeleeSidearm: 受近战攻击但未装备武器");
                return;
            }

            // 已使用近战武器则无需切换
            if (currentWeapon.def.IsMeleeWeapon)
            {
                if (AEDebug.IsActive) AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(Pawn)} CheckMeleeSidearm: 已使用近战 ({currentWeapon.LabelShort})");
                return;
            }

            if (AEDebug.IsActive) AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(Pawn)} CheckMeleeSidearm: 持远程武器受近战攻击 ({currentWeapon.LabelShort}), 搜索库存近战副武器");

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
                    if (AEDebug.IsActive) AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(Pawn)} CheckMeleeSidearm: 不替换生物编码武器 ({currentWeapon.LabelShort})");
                    return;
                }

                // 决策详细信息走 AEDebug.Log，避免战斗中频繁切武器刷屏
                if (AEDebug.IsActive) AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(Pawn)} CheckMeleeSidearm 决策: 抽出近战副武器 '{bestMelee.LabelShort}' (score={bestScore:F1}), 收起远程 '{currentWeapon.LabelShort}'");

                // 保存当前武器为主武器（稍后重新装备）
                primaryWeapon = currentWeapon;

                // 切换：卸下远程，装备库存近战
                // 原子性修复：TryDropEquipment 失败时不应继续 Remove/AddEquipment，
                // 否则 AddEquipment 会强制替换当前仍在装备槽的主武器，导致主武器被丢弃到地上无主
                Pawn.equipment.TryDropEquipment(currentWeapon as ThingWithComps, out ThingWithComps droppedWep, Pawn.Position);
                if (droppedWep == null)
                {
                    // 卸下失败：回滚 primaryWeapon 引用，避免 OnUndraft 时引用失效
                    Log.Warning($"[AutoEverything] {AEDebug.Label(Pawn)} CheckMeleeSidearm: 卸下主武器失败，放弃切换");
                    primaryWeapon = null;
                    return;
                }

                if (droppedWep.Spawned)
                    droppedWep.DeSpawn();
                if (!Pawn.inventory.innerContainer.TryAdd(droppedWep))
                    GenPlace.TryPlaceThing(droppedWep, Pawn.Position, Pawn.Map, ThingPlaceMode.Near);

                Pawn.inventory.innerContainer.Remove(bestMelee);
                Pawn.equipment.AddEquipment(bestMelee as ThingWithComps);

                sidearm = bestMelee;
            }
            else
            {
                if (AEDebug.IsActive) AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(Pawn)} CheckMeleeSidearm: 受近战攻击但库存无近战副武器");
            }
        }
    }
}
