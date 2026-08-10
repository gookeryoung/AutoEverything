using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace AutoEverything.AutoDrugPolicy
{
    /// <summary>
    /// 用药方案预设：按评级档位定义 3 套 DrugPolicy 的内容。
    ///
    /// 用户需求（2026-08-10）：
    /// - 基本配置：复用 RimWorld 默认 SocialDrugs 政策（啤酒+烟叶 allowedForJoy=true，其他 allowed=false）
    /// - CDX 档（C/D/X）：基本 + 活力水 takeToInventory=1 + 清醒丸 takeToInventory=1
    /// - AB 档（A/B）：CDX + 思滞血清 takeToInventory=1
    /// - S 档（S/SS/SSS）：AB 配置（强力血清/钢血血清不在 DrugPolicy 系统中，由 AutoCarry 直接携带）
    ///
    /// 注意：血清类（JuggernautSerum/MetalbloodSerum）没有 Comp_Drug 组件，
    /// 不属于 RimWorld 药品政策系统的"药品"，无法在药品政策 UI 中显示条目。
    /// 用户决策：血清类由 AutoCarry 按评级直接派发 TakeInventory Job 携带，不走 DrugPolicy。
    /// </summary>
    public static class DrugPolicyPresets
    {
        // 3 个药品政策的 label 前缀（避免与玩家自定义政策冲突）
        public const string PolicyLabelS = "AE-S";
        public const string PolicyLabelAB = "AE-AB";
        public const string PolicyLabelCDX = "AE-CDX";

        /// <summary>
        /// 用药评级档位（与 CombatTier 的映射关系）：
        /// - TierS：S/SS/SSS
        /// - TierAB：A/B
        /// - TierCDX：C/D/X
        /// </summary>
        public enum DrugTier : byte
        {
            CDX = 0,
            AB = 1,
            S = 2
        }

        // 反射缓存：DrugPolicyEntry 的非公开字段（RimWorld 1.6 中可见性受限）
        private static readonly FieldInfo entriesField = typeof(DrugPolicy).GetField("entries",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private static readonly FieldInfo allowedField = typeof(DrugPolicyEntry).GetField("allowed",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private static readonly FieldInfo takeToInventoryField = typeof(DrugPolicyEntry).GetField("takeToInventory",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        /// <summary>
        /// 把 CombatTier 映射到 DrugTier。
        /// S/SS/SSS → S；A/B → AB；C/D/X → CDX。
        /// </summary>
        public static DrugTier CombatTierToDrugTier(Core.CombatTier tier)
        {
            switch (tier)
            {
                case Core.CombatTier.S:
                case Core.CombatTier.SS:
                case Core.CombatTier.SSS:
                    return DrugTier.S;
                case Core.CombatTier.A:
                case Core.CombatTier.B:
                    return DrugTier.AB;
                default:
                    return DrugTier.CDX;
            }
        }

        /// <summary>
        /// 按档位填充 DrugPolicy 的 entries 列表。
        /// 调用方负责提供 DrugPolicy 实例，本方法通过反射赋值 entries 字段。
        /// </summary>
        public static void FillPolicyEntries(DrugPolicy policy, DrugTier tier)
        {
            var entries = new List<DrugPolicyEntry>();

            // ── 基本配置：SocialDrugs 内容（啤酒 + 烟叶 allowedForJoy=true）──
            AddSocialDrugEntry(entries, ThingDefOf.Beer);
            AddSocialDrugEntry(entries, ThingDefOf.SmokeleafJoint);

            // ── CDX 档叠加：活力水 + 清醒丸（takeToInventory=1）──
            if (tier >= DrugTier.CDX)
            {
                AddCarryDrugEntry(entries, ThingDefOf.Luciferium, 1);
                AddCarryDrugEntry(entries, ThingDefOf.WakeUp, 1);
            }

            // ── AB 档叠加：思滞血清（takeToInventory=1）──
            if (tier >= DrugTier.AB)
            {
                AddCarryDrugEntry(entries, ThingDefOf.Penoxycyline, 1);
            }

            // S 档的强力血清/钢血血清由 AutoCarry 直接携带，不放入 DrugPolicy

            // 反射赋值 entries 到 DrugPolicy 实例
            SetEntries(policy, entries);
        }

        /// <summary>
        /// 添加社交用药条目（allowedForJoy=true，其他字段默认）。
        /// 对应 RimWorld 默认 SocialDrugs 政策中的条目格式。
        /// </summary>
        private static void AddSocialDrugEntry(List<DrugPolicyEntry> entries, ThingDef drug)
        {
            if (drug == null) return;
            var entry = new DrugPolicyEntry
            {
                drug = drug,
                allowedForJoy = true
            };
            entries.Add(entry);
        }

        /// <summary>
        /// 添加携带列条目（allowed=true + takeToInventory=count）。
        /// allowed=true 让殖民者可以在合适时机服用，
        /// takeToInventory=count 让游戏自动管理背包库存（AutoCarry 读此字段补充库存）。
        ///
        /// 注：allowed 与 takeToInventory 字段在 RimWorld 1.6 中可见性受限，需反射设置。
        /// </summary>
        private static void AddCarryDrugEntry(List<DrugPolicyEntry> entries, ThingDef drug, int carryCount)
        {
            if (drug == null) return;
            var entry = new DrugPolicyEntry
            {
                drug = drug
            };
            // 反射设置 allowed=true
            if (allowedField != null)
            {
                allowedField.SetValue(entry, true);
            }
            // 反射设置 takeToInventory=carryCount
            if (takeToInventoryField != null)
            {
                takeToInventoryField.SetValue(entry, carryCount);
            }
            entries.Add(entry);
        }

        /// <summary>
        /// 反射设置 DrugPolicy.entries 字段。
        /// 失败时 Log.ErrorOnce 警告，不影响其他逻辑。
        /// </summary>
        private static void SetEntries(DrugPolicy policy, List<DrugPolicyEntry> entries)
        {
            if (entriesField == null)
            {
                Log.ErrorOnce("[AutoEverything] DrugPolicy.entries 字段反射失败，无法配置用药方案",
                    0xA500);
                return;
            }
            entriesField.SetValue(policy, entries);
        }

        /// <summary>
        /// 反射读取 DrugPolicy.entries 字段。供 AutoCarry 查询 takeToInventory 复用。
        /// </summary>
        public static List<DrugPolicyEntry> GetEntries(DrugPolicy policy)
        {
            if (policy == null || entriesField == null) return null;
            return entriesField.GetValue(policy) as List<DrugPolicyEntry>;
        }
    }
}
