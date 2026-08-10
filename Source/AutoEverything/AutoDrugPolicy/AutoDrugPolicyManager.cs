using System;
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
    /// 关键修复（2026-08-10）：
    /// 探针验证 DrugPolicyDatabase.AllPolicies 是公开属性，无需反射访问 policies 字段。
    /// 之前用反射读取 policies，现改为直接用 AllPoliciesForReading 风格的公开 API。
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

        // 反射缓存：DrugPolicyDatabase.policies 字段（私有，但用于添加新政策到列表）
        // 探针验证：Field: policies : List`1 (Private=True, Public=False)
        // 公开属性 AllPolicies 只读，添加新政策必须反射写 policies 列表
        private static readonly FieldInfo policiesListField = typeof(DrugPolicyDatabase)
            .GetField("policies", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        /// <summary>
        /// 启动时检查并创建 3 个 DrugPolicy（如果不存在）。
        /// 在 GameComponentTick 或 ITab 勾选时调用。
        /// 幂等：重复调用不会创建重复政策，已存在的政策不会被重置内容（避免覆盖玩家手动调整）。
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
        /// 已存在时也重新填充 entries（覆盖旧内容）——确保最新预设生效，修复旧版本创建的错误政策。
        ///
        /// 设计权衡（2026-08-10 修复）：
        /// 之前"已存在不重置"导致旧 bug 版本创建的政策（漏设 allowScheduled、没精神茶）无法修复。
        /// 用户决策"按评级自动配置"，自动覆盖是预期行为。
        /// 玩家如需手动调整，可取消勾选"用药方案"开关停止自动重分配，或重命名为非 AE- 前缀。
        /// </summary>
        private static DrugPolicy FindOrCreatePolicy(string label, DrugPolicyPresets.DrugTier tier)
        {
            DrugPolicyDatabase db = Current.Game?.drugPolicyDatabase;
            if (db == null) return null;

            // 探针验证：DrugPolicyDatabase.AllPolicies 是公开属性 (Get only)
            List<DrugPolicy> policies = db.AllPolicies;
            DrugPolicy existing = null;
            if (policies != null)
            {
                for (int i = 0; i < policies.Count; i++)
                {
                    if (policies[i].label == label)
                    {
                        existing = policies[i];
                        break;
                    }
                }
            }

            if (existing != null)
            {
                // 已存在：重新填充 entries（覆盖旧内容，确保最新预设生效）
                DrugPolicyPresets.FillPolicyEntries(existing, tier);
                if (AEDebug.IsActive)
                {
                    int entryCount = existing.Count;
                    AEDebug.Log(() => $"[AutoDrugPolicy] 重置已有政策: {label} (entries={entryCount})");
                }
                return existing;
            }

            // 创建新政策：new DrugPolicy(id, label) + 反射添加到 policies 列表
            int newId = GeneratePolicyId(policies);
            var policy = new DrugPolicy(newId, label);
            DrugPolicyPresets.FillPolicyEntries(policy, tier);

            // 反射添加到数据库的 policies 列表（公开属性 AllPolicies 只读，必须反射写）
            List<DrugPolicy> policiesList = GetPoliciesListForWrite(db);
            if (policiesList != null)
            {
                policiesList.Add(policy);
            }
            else
            {
                Log.ErrorOnce("[AutoEverything] DrugPolicyDatabase.policies 字段反射失败，无法添加用药方案: " + label,
                    PolicyErrorSalt ^ label.GetHashCode());
            }

            if (AEDebug.IsActive)
            {
                int entryCount = policy.Count;
                AEDebug.Log(() => $"[AutoDrugPolicy] 创建政策: {label} (tier={tier}, id={newId}, entries={entryCount})");
            }

            return policy;
        }

        /// <summary>
        /// 反射获取 DrugPolicyDatabase.policies 列表用于添加新政策。
        /// 公开属性 AllPolicies 只读，写操作必须反射。
        /// </summary>
        private static List<DrugPolicy> GetPoliciesListForWrite(DrugPolicyDatabase db)
        {
            if (policiesListField == null) return null;
            return policiesListField.GetValue(db) as List<DrugPolicy>;
        }

        /// <summary>
        /// 生成唯一的 DrugPolicy id（在现有列表中取最大 id + 1）。
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
        /// </summary>
        public static int ReassignAll()
        {
            EnsurePoliciesCreated();
            if (policyS == null || policyAB == null || policyCDX == null)
            {
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
            if (!PawnSuitabilityChecker.CanManageGear(pawn)) return false;

            Core.CombatTier combatTier = CombatEvaluator.GetCombatTier(pawn);
            DrugPolicyPresets.DrugTier drugTier = DrugPolicyPresets.CombatTierToDrugTier(combatTier);
            DrugPolicy targetPolicy = GetPolicyByTier(drugTier);
            if (targetPolicy == null) return false;

            // 探针验证：Pawn_DrugPolicyTracker.CurrentPolicy 是公开属性 (Get/Set)
            DrugPolicy currentPolicy = pawn.drugs?.CurrentPolicy;
            if (currentPolicy == targetPolicy) return false;

            if (pawn.drugs == null) return false;
            pawn.drugs.CurrentPolicy = targetPolicy;

            if (AEDebug.IsActive)
                AEDebug.Log(() => $"[AutoDrugPolicy] {AEDebug.Label(pawn)} 评级={combatTier} → 政策={targetPolicy.label}");

            return true;
        }

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
