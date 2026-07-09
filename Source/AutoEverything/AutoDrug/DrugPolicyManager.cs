using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using AutoEverything.Core;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.AutoDrug
{
    /// <summary>
    /// 子功能2：按角色自动设置 Pawn 的 DrugPolicy。
    ///
    /// 设计目的：
    /// - Doctor 角色：自动配置带 penoxycyline 的政策（防疟疾/瘟疫/嗜睡病，takeToInventory=1）
    /// - 战斗员（Brawler/Shooter/Hunter/Leader）：禁成瘾品（空 entries 政策）
    /// - 普通角色：保留玩家手动设置（不强制修改）
    ///
    /// 实现要点：
    /// - DrugPolicyDef 在 XML 中预定义（Defs/DrugPolicyDefs/AE_DrugPolicyDefs.xml）
    ///   RimWorld 在游戏启动时自动从 DrugPolicyDef 实例化 DrugPolicy 加入 DrugPolicyDatabase
    /// - 此处仅查找已存在的政策（按 label 匹配），不主动创建——避免每 tick 重复创建
    /// - 调用 pawn.drugs.CurrentPolicy = policy 设置政策
    /// - 跳过已设置相同 label 的 Pawn，避免每 tick 重复设置
    /// - 医疗守卫：医生正在执行 TendPatient/DoBill 时不修改政策，避免触发 Job 重评估打断治疗
    /// </summary>
    public static class DrugPolicyManager
    {
        // 缓存政策实例：第一次解析后复用，避免每 tick 重复查找
        // 注：DrugPolicy 实例不能跨存档保留，存档加载后需重新解析
        private static DrugPolicy doctorPolicy;
        private static DrugPolicy combatPolicy;

        // 错误去重 salt：与 TreatmentAllocator/MedicationAllocator 区分
        private const int PolicyErrorSalt = 0xA630;

        // 政策 label 常量：与 XML DrugPolicyDef.label 一致，使用 AE_ 前缀避免与玩家自定义政策冲突
        private const string DoctorPolicyLabel = "AE_Doctor";
        private const string CombatPolicyLabel = "AE_Combat";

        /// <summary>
        /// 单 Pawn 入口：按角色检测并设置 DrugPolicy。
        /// 受 AESettings.autoDrugPolicy 子开关控制。
        /// </summary>
        public static void UpdateForPawn(Pawn pawn)
        {
            if (!AESettings.autoDrugPolicy) return;
            if (pawn?.drugs == null) return;

            // 医疗守卫：医生正在执行医疗 Job 时跳过——设置 CurrentPolicy 可能触发 Job 重评估
            if (PawnJobGuard.ShouldSkipForMedical(pawn)) return;

            try
            {
                Role role = RoleDetector.DetectRole(pawn);
                DrugPolicy targetPolicy = ResolvePolicyForRole(role);
                // 普通角色（Worker/Pacifist/Default）不强制政策：保留玩家手动设置
                if (targetPolicy == null) return;

                DrugPolicy current = pawn.drugs.CurrentPolicy;
                if (current != null && current.label == targetPolicy.label)
                {
                    // 已是目标政策：跳过，避免每 tick 重复设置
                    if (AEDebug.IsActive)
                        AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(pawn)} DrugPolicy 已是 {targetPolicy.label} (role={role})");
                    return;
                }

                // 直接赋值 CurrentPolicy：Pawn_DrugPolicyTracker 的 setter 内部处理数据库同步
                pawn.drugs.CurrentPolicy = targetPolicy;
                if (AEDebug.IsActive)
                    AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(pawn)} DrugPolicy 设置为 {targetPolicy.label} (role={role}, prev={current?.label ?? "null"})");
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] DrugPolicy 设置失败 " + (pawn?.LabelShort ?? "null") + ": " + ex.Message,
                    (pawn?.thingIDNumber ?? 0) ^ PolicyErrorSalt);
            }
        }

        /// <summary>
        /// 按角色解析目标 DrugPolicy：
        /// - Doctor：含 penoxycyline 的政策
        /// - Brawler/Shooter/Hunter/Leader：禁成瘾品政策（空 entries）
        /// - 其他（Worker/Pacifist/Default）：返回 null（保留玩家手动设置）
        /// </summary>
        private static DrugPolicy ResolvePolicyForRole(Role role)
        {
            switch (role)
            {
                case Role.Doctor:
                    return EnsureDoctorPolicy();
                case Role.Brawler:
                case Role.Shooter:
                case Role.Hunter:
                case Role.Leader:
                    return EnsureCombatPolicy();
                default:
                    return null;
            }
        }

        /// <summary>
        /// 确保 Doctor 政策可用：先查静态缓存，再查 DrugPolicyDatabase（按 label 匹配）。
        /// 政策由 RimWorld 在游戏启动时从 DrugPolicyDef 自动创建（见 AE_DrugPolicyDefs.xml）。
        /// 若未自动创建（理论上不会发生），用 MakeNewDrugPolicy + sourceDef + InitializeIfNeeded 兜底创建。
        /// </summary>
        private static DrugPolicy EnsureDoctorPolicy()
        {
            if (doctorPolicy != null && doctorPolicy.label == DoctorPolicyLabel) return doctorPolicy;

            DrugPolicy existing = FindPolicyByLabel(DoctorPolicyLabel);
            if (existing != null)
            {
                doctorPolicy = existing;
                return existing;
            }

            // 兜底创建：从 DrugPolicyDef 实例化一个新政策
            DrugPolicy created = CreatePolicyFromDef(DoctorPolicyLabel, "AE_Doctor");
            if (created != null) doctorPolicy = created;
            return created;
        }

        /// <summary>
        /// 确保战斗员政策可用：空 entries，禁所有成瘾品。
        /// 战斗员需要保持清醒，成瘾品戒断会严重影响战斗力。
        /// </summary>
        private static DrugPolicy EnsureCombatPolicy()
        {
            if (combatPolicy != null && combatPolicy.label == CombatPolicyLabel) return combatPolicy;

            DrugPolicy existing = FindPolicyByLabel(CombatPolicyLabel);
            if (existing != null)
            {
                combatPolicy = existing;
                return existing;
            }

            DrugPolicy created = CreatePolicyFromDef(CombatPolicyLabel, "AE_Combat");
            if (created != null) combatPolicy = created;
            return created;
        }

        /// <summary>
        /// 从 DrugPolicyDef 实例化新 DrugPolicy 并加入数据库。
        /// 用 db.NewDrugPolicyFromDef(def) 一步完成：创建空政策 + 从 def 复制 entries。
        /// 注：通常 RimWorld 会在游戏启动时自动调用 GenerateStartingDrugPolicies 创建所有 def 对应的政策，
        /// 此方法仅作为兜底——理论上不会执行，但保留以防 mod 加载顺序导致政策未自动创建。
        /// </summary>
        private static DrugPolicy CreatePolicyFromDef(string label, string defName)
        {
            DrugPolicyDatabase db = Current.Game?.drugPolicyDatabase;
            if (db == null) return null;

            DrugPolicyDef def = DefDatabase<DrugPolicyDef>.GetNamed(defName, false);
            if (def == null)
            {
                Log.WarningOnce("[AutoEverything] 未找到 DrugPolicyDef " + defName, PolicyErrorSalt ^ 0x100);
                return null;
            }

            // NewDrugPolicyFromDef 内部创建空政策 + 设置 sourceDef + 从 def 复制 entries
            DrugPolicy policy = db.NewDrugPolicyFromDef(def);
            // 覆盖默认 label（NewDrugPolicyFromDef 用 def.label，可能与我们的 label 不一致）
            // 注：def.label 与 label 一致（XML 中 <label>AE_Doctor</label>），此处仍设置确保一致
            policy.RenamableLabel = label;
            return policy;
        }

        /// <summary>
        /// 在 DrugPolicyDatabase 中按 label 查找政策。
        /// 用 AllPolicies 列表遍历，避免 LINQ（Tick 路径禁止 LINQ）。
        /// </summary>
        private static DrugPolicy FindPolicyByLabel(string label)
        {
            DrugPolicyDatabase db = Current.Game?.drugPolicyDatabase;
            if (db == null) return null;

            List<DrugPolicy> all = db.AllPolicies;
            if (all == null) return null;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].label == label) return all[i];
            }
            return null;
        }
    }
}
