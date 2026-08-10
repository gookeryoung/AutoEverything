using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;
using AutoEverything.Core;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.AutoDrugPolicy
{
    /// <summary>
    /// 用药方案自动管理器：按殖民者评级自动创建并分配 DrugPolicy。
    ///
    /// 工作流：
    /// 1. EnsurePoliciesCreated：MOD 启动时检查并创建 3 个 DrugPolicy（AE-S/AE-AB/AE-CDX）
    /// 2. ReassignAll：遍历自由殖民者，按评级自动分配对应 DrugPolicy
    /// 3. 周期触发：由 AutoExecutor 在评级触发后联动调用 ReassignAll
    ///
    /// 与 AutoCarry 协同：
    /// - 药品类（活力水/清醒丸/思滞血清）走 DrugPolicy 的 takeToInventory
    /// - 血清类（强力血清/钢血血清）由 AutoCarry 按评级直接携带（不走 DrugPolicy）
    ///
    /// 不破坏玩家自定义政策：
    /// - 只自动分配 label 以 "AE-" 开头的政策
    /// - 玩家手动给某个殖民者分配了非 AE- 政策，下次 ReassignAll 会覆盖回 AE- 政策
    ///   （这是用户决策"按评级自动分配"的预期行为）
    /// </summary>
    public static class AutoDrugPolicyManager
    {
        // 错误去重 salt
        private const int PolicyErrorSalt = 0xA510;

        // 3 个政策的运行时实例引用（EnsurePoliciesCreated 后填充）
        private static DrugPolicy policyS;
        private static DrugPolicy policyAB;
        private static DrugPolicy policyCDX;

        // 静态标志：是否已执行过 EnsurePoliciesCreated
        private static bool policiesInitialized = false;

        // 反射缓存：DrugPolicyDatabase.policies 字段（不同 RimWorld 版本可见性可能不同）
        private static readonly FieldInfo policiesListField = typeof(DrugPolicyDatabase)
            .GetField("policies", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        /// <summary>
        /// 启动时检查并创建 3 个 DrugPolicy（如果不存在）。
        /// 在 GameComponentTick 或 ITab 勾选时调用。
        /// 幂等：重复调用不会创建重复政策，已存在的政策不会被重置内容（避免覆盖玩家手动调整）。
        ///
        /// 设计权衡：
        /// - 已存在同名政策时，**不重置内容**——避免覆盖玩家手动调整后的政策
        /// - 仅在首次创建时按预设填充 entries
        /// - 如需强制重置，玩家可手动删除政策，下次启动会按预设重建
        /// </summary>
        public static void EnsurePoliciesCreated()
        {
            if (policiesInitialized) return;
            policiesInitialized = true;

            try
            {
                policyS = FindOrCreatePolicy(DrugPolicyPresets.PolicyLabelS, DrugPolicyPresets.DrugTier.S);
                policyAB = FindOrCreatePolicy(DrugPolicyPresets.PolicyLabelAB, DrugPolicyPresets.DrugTier.AB);
                policyCDX = FindOrCreatePolicy(DrugPolicyPresets.PolicyLabelCDX, DrugPolicyPresets.DrugTier.CDX);

                if (AEDebug.IsActive)
                    AEDebug.Log(() => $"[AutoDrugPolicy] 政策创建完成: S={policyS?.id}, AB={policyAB?.id}, CDX={policyCDX?.id}");
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] 用药方案创建失败: " + ex.Message, PolicyErrorSalt);
            }
        }

        /// <summary>
        /// 查找或创建指定 label 的 DrugPolicy。
        /// 已存在时返回现有政策（不重置内容），不存在时创建并按预设填充。
        /// </summary>
        private static DrugPolicy FindOrCreatePolicy(string label, DrugPolicyPresets.DrugTier tier)
        {
            DrugPolicyDatabase db = Current.Game?.drugPolicyDatabase;
            if (db == null) return null;

            // 反射读取 policies 列表（List<DrugPolicy>）
            List<DrugPolicy> policies = GetPoliciesList(db);
            if (policies != null)
            {
                for (int i = 0; i < policies.Count; i++)
                {
                    if (policies[i].label == label)
                    {
                        return policies[i];
                    }
                }
            }

            // 创建新政策：new DrugPolicy(label, id) + 加入数据库
            // RimWorld 1.6 中 DrugPolicy 构造需要唯一 id，从数据库生成
            int newId = GeneratePolicyId(policies);
            var policy = new DrugPolicy(newId, label);
            DrugPolicyPresets.FillPolicyEntries(policy, tier);

            // 加入数据库（反射添加到 policies 列表）
            if (policies != null)
            {
                policies.Add(policy);
            }

            if (AEDebug.IsActive)
                AEDebug.Log(() => $"[AutoDrugPolicy] 创建政策: {label} (tier={tier}, id={newId})");

            return policy;
        }

        /// <summary>
        /// 反射获取 DrugPolicyDatabase.policies 列表。
        /// RimWorld 1.6 中该字段可见性受限，需要反射访问。
        /// </summary>
        private static List<DrugPolicy> GetPoliciesList(DrugPolicyDatabase db)
        {
            if (policiesListField == null) return null;
            return policiesListField.GetValue(db) as List<DrugPolicy>;
        }

        /// <summary>
        /// 生成唯一的 DrugPolicy id（在现有列表中取最大 id + 1）。
        /// RimWorld 1.6 中 DrugPolicy.id 用于数据库内部索引。
        /// </summary>
        private static int GeneratePolicyId(List<DrugPolicy> policies)
        {
            if (policies == null || policies.Count == 0) return 1;
            int max = 0;
            for (int i = 0; i < policies.Count; i++)
            {
                if (policies[i].id > max) max = policies[i].id;
            }
            return max + 1;
        }

        /// <summary>
        /// 全局重新分配：遍历所有自由殖民者，按评级自动分配对应 DrugPolicy。
        /// 返回受影响的殖民者数量（实际改变政策的 Pawn 数）。
        ///
        /// 调用方职责：
        /// - autoDrugPolicyEnabled 开关由调用方检查
        /// - 战斗过滤由调用方控制（用药方案分配不像工作重配那样打断 Job，可不禁用战斗中分配）
        /// </summary>
        public static int ReassignAll()
        {
            EnsurePoliciesCreated();
            if (policyS == null || policyAB == null || policyCDX == null)
            {
                // 政策创建失败，无法分配
                return 0;
            }

            int affected = 0;
            foreach (Pawn pawn in PawnsFinder.AllMaps_FreeColonists)
            {
                if (pawn == null) continue;
                if (pawn.Dead || pawn.Downed) continue;

                try
                {
                    if (TryAssignPolicy(pawn))
                    {
                        affected++;
                    }
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce("[AutoEverything] 用药方案分配失败 pawn=" +
                        pawn.LabelShort + ": " + ex.Message,
                        pawn.thingIDNumber ^ PolicyErrorSalt);
                }
            }

            if (AEDebug.IsActive)
                AEDebug.Log(() => $"[AutoDrugPolicy] ReassignAll end (affected={affected})");

            return affected;
        }

        /// <summary>
        /// 为单个 Pawn 分配对应评级的 DrugPolicy。
        /// 返回 true 表示政策改变（从旧政策切换到新政策）。
        /// </summary>
        private static bool TryAssignPolicy(Pawn pawn)
        {
            // 仅人类 like 通过（与 AutoCarry 一致，排除机械族/食尸鬼/奴隶由调用方过滤）
            if (!PawnSuitabilityChecker.CanManageGear(pawn)) return false;

            // 评级 → DrugTier → DrugPolicy
            Core.CombatTier combatTier = CombatEvaluator.GetCombatTier(pawn);
            DrugPolicyPresets.DrugTier drugTier = DrugPolicyPresets.CombatTierToDrugTier(combatTier);
            DrugPolicy targetPolicy = GetPolicyByTier(drugTier);
            if (targetPolicy == null) return false;

            // 当前政策已正确则跳过
            DrugPolicy currentPolicy = pawn.drugs?.CurrentPolicy;
            if (currentPolicy == targetPolicy) return false;

            // 分配新政策
            if (pawn.drugs == null) return false;
            pawn.drugs.CurrentPolicy = targetPolicy;

            if (AEDebug.IsActive)
                AEDebug.Log(() => $"[AutoDrugPolicy] {AEDebug.Label(pawn)} 评级={combatTier} → 政策={targetPolicy.label}");

            return true;
        }

        /// <summary>
        /// 按 DrugTier 取对应 DrugPolicy 实例。
        /// </summary>
        private static DrugPolicy GetPolicyByTier(DrugPolicyPresets.DrugTier tier)
        {
            switch (tier)
            {
                case DrugPolicyPresets.DrugTier.S: return policyS;
                case DrugPolicyPresets.DrugTier.AB: return policyAB;
                default: return policyCDX;
            }
        }

        /// <summary>
        /// 重置初始化标志：存档加载后允许重新创建政策（应对 DrugPolicyDatabase 实例变化）。
        /// 由 GameComponentTick 首次调用 EnsurePoliciesCreated 前自动触发。
        /// </summary>
        public static void ResetInitialization()
        {
            policiesInitialized = false;
            policyS = null;
            policyAB = null;
            policyCDX = null;
        }
    }
}
