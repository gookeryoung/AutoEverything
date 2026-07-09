using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using AutoEverything.Core;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.AutoFood
{
    /// <summary>
    /// 子功能3：按角色自动设置 Pawn 的 FoodPolicy（食物限制）。
    ///
    /// 设计目的：
    /// - Doctor 角色：切到高营养食物限制（允许 lavish meal，禁 raw）——医生需保持体力治疗伤员
    /// - 卧床饥饿伤员：同样切到高营养限制——伤员需营养恢复
    /// - 普通角色：保留玩家手动设置（不强制修改，返回 null）
    ///
    /// RimWorld 1.6 API 说明：
    /// - 1.6 已将 FoodRestriction 类重命名为 FoodPolicy（数据库类名 FoodRestrictionDatabase 保留）
    /// - pawn.foodRestriction.CurrentFoodPolicy（get/set，类型 FoodPolicy）
    /// - Current.Game.foodRestrictionDatabase.AllFoodRestrictions 返回 List&lt;FoodPolicy&gt;
    ///
    /// 实现要点（简化策略，避免创建复杂 FoodPolicy）：
    /// - 不主动创建新 FoodPolicy（避免每 tick 重复创建与 filter 配置复杂）
    /// - 优先按 label 查找已存在的"AE_Patient"或"Lavish"政策
    /// - "Lavish"是 RimWorld 默认政策，允许所有食物类型，适合伤员恢复
    /// - 跳过已设置相同 label 的 Pawn，避免每 tick 重复设置
    /// - 医疗守卫：医生正在执行医疗 Job 时不修改政策，避免触发 Job 重评估打断治疗
    /// </summary>
    public static class FoodRestrictionManager
    {
        // 缓存政策实例：第一次解析后复用，避免每 tick 重复查找
        // 注：FoodPolicy 实例不能跨存档保留，存档加载后需重新解析
        private static FoodPolicy patientPolicy;

        // 错误去重 salt：与 MealCarrier/FeedingAllocator/FoodAllocator 区分
        private const int PolicyErrorSalt = 0xA730;

        // 自定义伤员政策 label 常量（玩家可在 Assign 界面手动创建此 label 的政策，MOD 优先采用）
        // 与原生 "Lavish" 区分，便于玩家为伤员配置特殊 filter（如禁 raw human meat）
        private const string PatientPolicyLabel = "AE_Patient";
        // 原生默认政策 label，作为兜底（允许所有食物，适合伤员恢复）
        private const string LavishPolicyLabel = "Lavish";

        /// <summary>
        /// 单 Pawn 入口：按角色检测并设置 FoodPolicy。
        /// 受 AESettings.autoFoodRestriction 子开关控制。
        /// </summary>
        public static void UpdateForPawn(Pawn pawn)
        {
            if (!AESettings.autoFoodRestriction) return;
            if (pawn?.foodRestriction == null) return;

            // 医疗守卫：医生正在执行医疗 Job 时跳过——设置 CurrentFoodPolicy 可能触发 Job 重评估
            if (PawnJobGuard.ShouldSkipForMedical(pawn)) return;

            try
            {
                FoodPolicy targetPolicy = ResolvePolicyForPawn(pawn);
                // 普通角色（非伤员/非医生）不强制政策：保留玩家手动设置
                if (targetPolicy == null) return;

                FoodPolicy current = pawn.foodRestriction.CurrentFoodPolicy;
                if (current != null && current.label == targetPolicy.label)
                {
                    // 已是目标政策：跳过，避免每 tick 重复设置
                    if (AEDebug.IsActive)
                        AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(pawn)} FoodPolicy 已是 {targetPolicy.label}");
                    return;
                }

                // 直接赋值 CurrentFoodPolicy：Pawn_FoodRestrictionTracker 的 setter 内部处理数据库同步
                pawn.foodRestriction.CurrentFoodPolicy = targetPolicy;
                if (AEDebug.IsActive)
                    AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(pawn)} FoodPolicy 设置为 {targetPolicy.label} (prev={current?.label ?? "null"})");
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] FoodPolicy 设置失败 " + (pawn?.LabelShort ?? "null") + ": " + ex.Message,
                    (pawn?.thingIDNumber ?? 0) ^ PolicyErrorSalt);
            }
        }

        /// <summary>
        /// 按角色解析目标 FoodPolicy：
        /// - Doctor 角色：高营养政策（医生需保持体力）
        /// - 卧床饥饿伤员（InBed && CurLevelPercentage &lt; 0.3）：高营养政策（恢复营养）
        /// - 其他（普通角色）：返回 null（保留玩家手动设置）
        /// </summary>
        private static FoodPolicy ResolvePolicyForPawn(Pawn pawn)
        {
            Role role = RoleDetector.DetectRole(pawn);
            if (role == Role.Doctor)
            {
                return EnsurePatientPolicy();
            }

            // 卧床饥饿伤员：与 FeedingAllocator 候选条件一致（CurLevelPercentage < 0.3）
            if (pawn.InBed() && pawn.needs?.food != null
                && pawn.needs.food.CurLevelPercentage < 0.3f)
            {
                return EnsurePatientPolicy();
            }

            return null;
        }

        /// <summary>
        /// 确保伤员政策可用：先查静态缓存，再按 label 查 FoodRestrictionDatabase。
        /// 解析顺序：
        /// 1. 玩家自定义 "AE_Patient" 政策（玩家可在 Assign 界面创建此 label）
        /// 2. 兜底原生 "Lavish" 政策（允许所有食物，适合伤员恢复）
        /// 3. 都不存在则返回 null（保留玩家当前设置，不强制修改）
        /// </summary>
        private static FoodPolicy EnsurePatientPolicy()
        {
            // 静态缓存有效则直接复用
            if (patientPolicy != null) return patientPolicy;

            // 1. 优先查找玩家自定义的 AE_Patient 政策
            FoodPolicy custom = FindPolicyByLabel(PatientPolicyLabel);
            if (custom != null)
            {
                patientPolicy = custom;
                return custom;
            }

            // 2. 兜底查找原生 Lavish 政策（默认允许所有食物类型）
            FoodPolicy lavish = FindPolicyByLabel(LavishPolicyLabel);
            if (lavish != null)
            {
                patientPolicy = lavish;
                return lavish;
            }

            // 3. 都不存在：不强制修改（保留玩家当前设置）
            // 理论上 Lavish 是 RimWorld 默认政策，必定存在；此处兜底防御
            return null;
        }

        /// <summary>
        /// 在 FoodRestrictionDatabase 中按 label 查找政策。
        /// 用 AllFoodRestrictions 列表遍历，避免 LINQ（Tick 路径禁止 LINQ）。
        /// </summary>
        private static FoodPolicy FindPolicyByLabel(string label)
        {
            FoodRestrictionDatabase db = Current.Game?.foodRestrictionDatabase;
            if (db == null) return null;

            List<FoodPolicy> all = db.AllFoodRestrictions;
            if (all == null) return null;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].label == label) return all[i];
            }
            return null;
        }
    }
}
