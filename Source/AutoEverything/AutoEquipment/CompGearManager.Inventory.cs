using System;
using RimWorld;
using Verse;
using Verse.AI;
using AutoEverything.RoleEvaluation;
using AutoEverything.Core;

namespace AutoEverything.AutoEquipment
{
    public partial class CompGearManager
    {
        // ===================== 库存 =====================

        private void EvaluateInventory(Role role)
        {
            if (!AESettings.carryMedicine) return;

            // 防止反复拾取药品——每次尝试后冷却
            if (Find.TickManager.TicksGame - lastMedPickupTick < 2500) return;

            // 不打断医疗工作与伤员休养：手术 DoBill 执行期间取药会 TryTakeOrderedJob 取消手术 Job，导致手术死循环；
            // 伤员休养被打断会取消 LayDown Job，打断免疫力/治疗进度导致重伤者死亡
            if (PawnJobGuard.ShouldSkipForMedical(Pawn)) return;

            // 医生与有医疗技能的战斗人员应携带药品
            int medSkill = Pawn.skills?.GetSkill(SkillDefOf.Medicine)?.Level ?? 0;
            bool shouldCarryMeds = role == Role.Doctor
                || (medSkill >= 4 && !Pawn.WorkTagIsDisabled(WorkTags.Caring));

            if (!shouldCarryMeds)
            {
                if (AEDebug.IsActive) AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(Pawn)} EvaluateInventory: 跳过 (role={role}, medSkill={medSkill}, shouldCarry=false)");
                return;
            }

            // 带药数量按 AESettings.medicineCount 配置
            int targetCount = AESettings.medicineCount;

            // 仅统计库存中的药品（不含手持——手持药品是临时的，
            // 用于治疗或搬运工作，统计会导致反复拾取死循环）
            int medsInInventory = 0;
            foreach (Thing item in Pawn.inventory.innerContainer)
            {
                if (item.def.IsMedicine)
                    medsInInventory += item.stackCount;
            }

            if (medsInInventory >= targetCount)
            {
                if (AEDebug.IsActive) AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(Pawn)} EvaluateInventory: 已满 ({medsInInventory}/{targetCount})");
                return;
            }

            int needed = targetCount - medsInInventory;
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
                Log.Message($"[AutoEverything] {AEDebug.Label(Pawn)} EvaluateInventory: 拾取 {pickupCount}x {bestMed.def.label} (持有 {medsInInventory}, 需 {needed})");

                var job = JobMaker.MakeJob(JobDefOf.TakeCountToInventory, bestMed);
                job.count = pickupCount;
                Pawn.jobs.TryTakeOrderedJob(job, Verse.AI.JobTag.Misc);

                lastMedPickupTick = Find.TickManager.TicksGame;
            }
            else
            {
                if (AEDebug.IsActive) AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(Pawn)} EvaluateInventory: 附近无药品");
            }
        }

        /// <summary>
        /// 取消征召时调用。若曾抽出副武器则恢复主武器。
        /// </summary>
        public void OnUndraft()
        {
            if (sidearm == null || primaryWeapon == null)
            {
                if (AEDebug.IsActive) AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(Pawn)} OnUndraft: 无副武器/主武器可恢复 (sidearm={sidearm?.LabelShort ?? "null"}, primary={primaryWeapon?.LabelShort ?? "null"})");
                return;
            }
            if (Pawn.Map == null)
            {
                Log.Warning($"[AutoEverything] {AEDebug.Label(Pawn)} OnUndraft: Pawn 无地图, 清空副武器状态");
                sidearm = null; primaryWeapon = null; return;
            }

            Log.Message($"[AutoEverything] {AEDebug.Label(Pawn)} OnUndraft: 恢复主武器 '{primaryWeapon.LabelShort}', 收起副武器 '{sidearm.LabelShort}'");

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
                // 当前武器非副武器：Pawn 在征召中通过其他方式换了武器
                // 仍尝试从库存恢复主武器（若主武器仍在库存中），避免主武器永久滞留库存
                Log.Warning($"[AutoEverything] {AEDebug.Label(Pawn)} OnUndraft: 当前武器 '{currentWeapon?.LabelShort ?? "none"}' 非副武器 '{sidearm.LabelShort}'，尝试恢复主武器");
            }

            // 从库存重新装备主武器（处理副武器被销毁/丢失情况）
            // 当前武器非副武器时也走此路径：若主武器仍在库存中，强制恢复
            if (primaryWeapon as ThingWithComps != null
                && Pawn.equipment?.Primary != primaryWeapon
                && Pawn.inventory.innerContainer.Contains(primaryWeapon))
            {
                // 若装备槽已有其他武器（玩家手动换的），先卸下再放入库存
                ThingWithComps existing = Pawn.equipment?.Primary;
                if (existing != null && existing != primaryWeapon)
                {
                    Pawn.equipment.TryDropEquipment(existing, out ThingWithComps droppedExisting, Pawn.Position);
                    if (droppedExisting != null)
                    {
                        if (droppedExisting.Spawned) droppedExisting.DeSpawn();
                        if (!Pawn.inventory.innerContainer.TryAdd(droppedExisting))
                            GenPlace.TryPlaceThing(droppedExisting, Pawn.Position, Pawn.Map, ThingPlaceMode.Near);
                    }
                }
                Log.Message($"[AutoEverything] {AEDebug.Label(Pawn)} OnUndraft: 从库存重新装备主武器 '{primaryWeapon.LabelShort}'");
                Pawn.inventory.innerContainer.Remove(primaryWeapon);
                Pawn.equipment.AddEquipment(primaryWeapon as ThingWithComps);
            }
            else if (primaryWeapon as ThingWithComps != null && !Pawn.inventory.innerContainer.Contains(primaryWeapon))
            {
                Log.Warning($"[AutoEverything] {AEDebug.Label(Pawn)} OnUndraft: 主武器 '{primaryWeapon.LabelShort}' 不在库存中 —— 可能已丢失/销毁");
            }

            sidearm = null;
            primaryWeapon = null;
        }
    }
}
