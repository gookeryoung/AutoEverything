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
    /// 1. 计划服用（allowScheduled）只允许配置精神茶（2 天 1 次）与佩诺西林（AB/S 档，5 天 1 次）
    /// 2. 魔鬼素（Luciferium）不预支不计划服用（仅满足依赖）
    /// 3. 清醒丸（WakeUp）不计划服用（所有档预支 1 个）
    /// 4. 活力水（GoJuice）AB/S 档预支 1 个，不计划服用
    /// 5. 佩诺西林（Penoxycyline）AB/S 档计划服用 5 天 1 次 + 预支 1 个
    /// 6. 所有成瘾品勾选 allowedForAddiction=true（满足依赖，RimWorld 默认）
    ///
    /// RimWorld 机制（反编译验证 2026-08-10）：
    /// - DrugPolicy(id, label) 构造函数调用 InitializeIfNeeded()，
    ///   为所有 IsDrug 物品创建默认条目（仅 allowedForAddiction=true，其他默认）
    /// - FirstUnloadableThing 遍历 DrugPolicy，takeToInventory>0 的药品加入"保留列表"，
    ///   背包中 takeToInventory=0 的药品会被自动卸下——这是"携带了会自己丢掉"的根因
    /// - JobGiver_MoveDrugsToInventory 自动派发 TakeInventory Job 补充背包至 takeToInventory 数量
    /// - takeToInventoryTempBuffer 是 UI 文本框缓冲，需同步设置避免 UI 显示空
    ///
    /// 关键修复（2026-08-10）：
    /// 之前用 Clear+Add 清空 RimWorld 默认条目，导致 UI 中其他药品消失，
    /// 且新增条目未同步 takeToInventoryTempBuffer。现改为保留默认列表，
    /// 遍历查找对应药品修改字段，找不到时手动添加。
    /// </summary>
    public static class DrugPolicyPresets
    {
        // 3 个药品政策的 label 前缀（避免与玩家自定义政策冲突）
        public const string PolicyLabelS = "AE-S";
        public const string PolicyLabelAB = "AE-AB";
        public const string PolicyLabelCDX = "AE-CDX";

        // 精神茶计划服用频率：2 天 1 次（用户需求 1）
        private const float PsychiteTeaDaysFrequency = 2f;

        // 佩诺西林计划服用频率：5 天 1 次（用户需求 2）
        private const float PenoxycylineDaysFrequency = 5f;

        // 反射缓存：DrugPolicy.entriesInt 字段（私有，添加新条目时需反射）
        // 探针验证：Field: entriesInt : List`1 (Private=True, Public=False)
        // 公开索引器 policy[i] / policy[ThingDef] 用于读写已有条目，
        // 但添加新条目（药品不在默认列表时）需反射写入 entriesInt
        private static readonly FieldInfo entriesIntField = typeof(DrugPolicy).GetField("entriesInt",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        // 反射缓存：DrugPolicy.InitializeIfNeeded 方法（私有，补全缺失药品条目）
        // 探针验证：Method: InitializeIfNeeded(Boolean) (Private=True)
        // 调用 InitializeIfNeeded(false) 不清空已有条目，仅补全缺失的药品条目（默认 allowedForAddiction=true）
        private static readonly MethodInfo initializeIfNeededMethod = typeof(DrugPolicy).GetMethod(
            "InitializeIfNeeded", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

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
        /// 按档位填充 DrugPolicy 的条目字段。
        ///
        /// 设计原则（关键修复）：
        /// - 不清空 entriesInt，保留 RimWorld 默认初始化的所有药品条目
        ///   （默认 allowedForAddiction=true，满足用户需求 3）
        /// - 遍历查找目标药品修改字段，找不到时手动添加新条目
        /// - 同步设置 takeToInventoryTempBuffer 避免 UI 显示空
        ///
        /// 字段设置规则（用户需求 2026-08-10）：
        /// - 啤酒/烟叶：allowedForJoy=true, allowedForAddiction=true（所有档）
        /// - 精神茶：上述 + allowScheduled=true + daysFrequency=2 + takeToInventory=1（所有档）
        /// - 魔鬼素（Luciferium）：allowedForAddiction=true，不预支不计划服用（所有档）
        /// - 清醒丸（WakeUp）：allowedForAddiction=true + takeToInventory=1，不计划服用（所有档）
        /// - 活力水（GoJuice）：allowedForAddiction=true + takeToInventory=1，不计划服用（仅 AB/S 档）
        /// - 佩诺西林（Penoxycyline）：allowedForAddiction=true + allowScheduled=true
        ///   + daysFrequency=5 + takeToInventory=1（仅 AB/S 档）
        /// </summary>
        public static void FillPolicyEntries(DrugPolicy policy, DrugTier tier)
        {
            if (policy == null) return;

            // 补全缺失的 RimWorld 默认药品条目（处理旧版本 policy entriesInt 不完整的情况）
            // InitializeIfNeeded(false) 不清空已有条目，仅添加缺失药品（默认 allowedForAddiction=true）
            EnsureEntriesComplete(policy);

            // ── 基本配置：所有档启用啤酒 + 烟叶 + 精神茶 ──
            // 社交用药：娱乐 + 满足依赖
            SetEntry(policy, ThingDefOf.Beer,
                allowedForJoy: true, allowedForAddiction: true);
            SetEntry(policy, ThingDefOf.SmokeleafJoint,
                allowedForJoy: true, allowedForAddiction: true);
            // 精神茶：娱乐 + 满足依赖 + 计划服用 2 天 1 次 + 携带 1 个
            // （计划服用需 takeToInventory=1 让殖民者背包持有备用）
            SetEntry(policy, DefDatabase<ThingDef>.GetNamedSilentFail("PsychiteTea"),
                allowedForJoy: true, allowedForAddiction: true,
                allowScheduled: true, daysFrequency: PsychiteTeaDaysFrequency,
                takeToInventory: 1);

            // ── 所有档：魔鬼素不预支不计划服用（仅满足依赖）──
            // Luciferium 是永久成瘾必要药，每天自动服用；不预支不计划服用
            SetEntry(policy, ThingDefOf.Luciferium,
                allowedForAddiction: true);

            // ── 所有档：清醒丸预支 1 个，不计划服用 ──
            // WakeUp 是战斗增强药，预支 1 个备用，不计划服用
            SetEntry(policy, ThingDefOf.WakeUp,
                allowedForAddiction: true, takeToInventory: 1);

            // ── AB/S 档叠加：活力水 + 佩诺西林 ──
            if (tier >= DrugTier.AB)
            {
                // 活力水（GoJuice）：预支 1 个，不计划服用
                // GoJuice 是战斗增强药，预支 1 个备用
                SetEntry(policy, DefDatabase<ThingDef>.GetNamedSilentFail("GoJuice"),
                    allowedForAddiction: true, takeToInventory: 1);
                // 佩诺西林（Penoxycyline）：计划服用 5 天 1 次 + 预支 1 个
                // 抗疟药，预防性疾病，定期服用预防
                SetEntry(policy, ThingDefOf.Penoxycyline,
                    allowedForAddiction: true, allowScheduled: true,
                    daysFrequency: PenoxycylineDaysFrequency, takeToInventory: 1);
            }

            // S 档的强力血清/钢血血清不在 DrugPolicy 系统中（无 Comp_Drug），
            // 由 AutoCarry 模块直接携带
        }

        /// <summary>
        /// 修改（或添加）DrugPolicy 中指定药品的条目字段。
        ///
        /// 实现：
        /// - 遍历 entriesInt 查找 drug==targetDef 的条目
        /// - 找到则修改字段（仅修改非 null 参数）
        /// - 找不到则创建新条目并反射添加到 entriesInt 列表
        /// - 设置 takeToInventory 时同步更新 takeToInventoryTempBuffer（UI 文本框缓冲）
        ///
        /// 注意：DrugPolicy.this[ThingDef] 索引器找不到时抛 ArgumentException，
        /// 故不能用索引器查找，必须手动遍历。
        /// </summary>
        private static void SetEntry(DrugPolicy policy, ThingDef drug,
            bool? allowedForJoy = null,
            bool? allowedForAddiction = null,
            bool? allowScheduled = null,
            float? daysFrequency = null,
            int? takeToInventory = null)
        {
            if (policy == null || drug == null) return;

            // 遍历查找已有条目
            DrugPolicyEntry entry = null;
            int count = policy.Count;
            for (int i = 0; i < count; i++)
            {
                DrugPolicyEntry e = policy[i];
                if (e != null && e.drug == drug)
                {
                    entry = e;
                    break;
                }
            }

            // 找不到则创建新条目并反射添加到 entriesInt
            if (entry == null)
            {
                entry = new DrugPolicyEntry
                {
                    drug = drug,
                    // 新建条目保持 RimWorld 默认值：allowedForAddiction=true
                    allowedForAddiction = true
                };
                AddEntryToList(policy, entry);
            }

            // 修改字段（仅修改非 null 参数）
            if (allowedForJoy.HasValue) entry.allowedForJoy = allowedForJoy.Value;
            if (allowedForAddiction.HasValue) entry.allowedForAddiction = allowedForAddiction.Value;
            if (allowScheduled.HasValue) entry.allowScheduled = allowScheduled.Value;
            if (daysFrequency.HasValue) entry.daysFrequency = daysFrequency.Value;
            if (takeToInventory.HasValue)
            {
                entry.takeToInventory = takeToInventory.Value;
                // 同步 UI 文本框缓冲，避免政策 UI 显示空
                entry.takeToInventoryTempBuffer = takeToInventory.Value.ToString();
            }
        }

        /// <summary>
        /// 反射添加新条目到 DrugPolicy.entriesInt 列表。
        /// 公开 API 无 Add 方法，必须反射写入私有字段。
        /// </summary>
        private static void AddEntryToList(DrugPolicy policy, DrugPolicyEntry entry)
        {
            if (entriesIntField == null) return;
            var list = entriesIntField.GetValue(policy) as List<DrugPolicyEntry>;
            if (list == null)
            {
                // entriesInt 为 null（构造函数未初始化），创建新列表
                list = new List<DrugPolicyEntry>();
                entriesIntField.SetValue(policy, list);
            }
            list.Add(entry);
        }

        /// <summary>
        /// 确保 DrugPolicy.entriesInt 包含 RimWorld 所有药品的默认条目。
        /// 反射调用 InitializeIfNeeded(false) 补全缺失条目（不清空已有，仅添加缺失）。
        ///
        /// 适用场景：旧版本创建的 policy entriesInt 不完整（只有几个条目），
        /// 现版本需要补全其他药品条目让政策 UI 显示完整药品列表。
        ///
        /// 边界处理：entriesInt 为 null 时先创建空列表，避免 InitializeIfNeeded(false) 内部
        /// 的 entriesInt.Any 抛 NullReferenceException。
        /// </summary>
        private static void EnsureEntriesComplete(DrugPolicy policy)
        {
            // 先确保 entriesInt 不为 null（InitializeIfNeeded(false) 内部会 Any 遍历）
            var list = entriesIntField?.GetValue(policy) as List<DrugPolicyEntry>;
            if (list == null && entriesIntField != null)
            {
                list = new List<DrugPolicyEntry>();
                entriesIntField.SetValue(policy, list);
            }

            // 反射调用 InitializeIfNeeded(false) 补全缺失药品条目
            if (initializeIfNeededMethod != null)
            {
                try
                {
                    initializeIfNeededMethod.Invoke(policy, new object[] { false });
                }
                catch
                {
                    // 反射失败静默忽略，SetEntry 会通过 AddEntryToList 兜底添加缺失条目
                }
            }
        }

        /// <summary>
        /// 反射读取 DrugPolicy.entriesInt 字段。供 AutoCarry 查询 takeToInventory 复用。
        /// </summary>
        public static List<DrugPolicyEntry> GetEntries(DrugPolicy policy)
        {
            if (policy == null || entriesIntField == null) return null;
            return entriesIntField.GetValue(policy) as List<DrugPolicyEntry>;
        }
    }
}
