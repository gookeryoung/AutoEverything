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
    /// - 基本配置：所有档启用啤酒 + 烟叶 + 精神茶（allowedForJoy=true，娱乐目的）
    /// - 精神茶额外配置计划服用：allowScheduled=true + daysFrequency=2（2 天 1 次，所有人生效）
    /// - CDX 档（C/D/X）：基本 + 活力水 + 清醒丸（takeToInventory=1 + allowScheduled=true）
    /// - AB 档（A/B）：CDX + 思滞血清（takeToInventory=1 + allowScheduled=true）
    /// - S 档（S/SS/SSS）：AB 配置（强力血清/钢血血清不在 DrugPolicy 系统中，由 AutoCarry 直接携带）
    ///
    /// 关键修复（2026-08-10）：
    /// DrugPolicyEntry 的字段全部公开，没有 allowed 字段（之前反射 GetField("allowed") 返回 null）。
    /// 让"携带列"真正生效必须同时设置：
    /// - takeToInventory=N：携带数量
    /// - allowScheduled=true：启用计划服用（否则游戏判定"无消费计划"会把携带的药品丢地上）
    /// - daysFrequency=N：服用频率（天）
    ///
    /// 注意：血清类（JuggernautSerum/MetalbloodSerum）没有 Comp_Drug 组件，
    /// 不属于 RimWorld 药品政策系统的"药品"，无法在药品政策 UI 中显示条目。
    /// </summary>
    public static class DrugPolicyPresets
    {
        // 3 个药品政策的 label 前缀（避免与玩家自定义政策冲突）
        public const string PolicyLabelS = "AE-S";
        public const string PolicyLabelAB = "AE-AB";
        public const string PolicyLabelCDX = "AE-CDX";

        // 精神茶计划服用频率：2 天 1 次（用户需求 3）
        private const float PsychiteTeaDaysFrequency = 2f;

        // 携带药品的计划服用频率默认值：1 天 1 次（活力水/清醒丸/思滞血清等）
        private const float DefaultCarryDaysFrequency = 1f;

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

        // 反射缓存：DrugPolicy.entriesInt 字段（私有，无公开 setter，初始化时需反射赋值整个列表）
        // 探针验证：Field: entriesInt : List`1 (Private=True, Public=False)
        private static readonly FieldInfo entriesIntField = typeof(DrugPolicy).GetField("entriesInt",
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
        /// 调用方负责提供 DrugPolicy 实例，本方法通过反射赋值 entriesInt 字段（私有字段无公开 setter）。
        ///
        /// 字段设置原则（关键修复）：
        /// - 社交药品（啤酒/烟叶/精神茶）：allowedForJoy=true，无 takeToInventory（娱乐用）
        /// - 精神茶：额外 allowScheduled=true + daysFrequency=2（2 天 1 次计划服用）
        /// - 携带药品（活力水/清醒丸/思滞血清）：allowedForJoy=true + allowScheduled=true
        ///   + takeToInventory=1 + daysFrequency=1（必须 allowScheduled=true，否则携带会被丢地上）
        /// </summary>
        public static void FillPolicyEntries(DrugPolicy policy, DrugTier tier)
        {
            var entries = new List<DrugPolicyEntry>();

            // ── 基本配置：所有档启用啤酒 + 烟叶 + 精神茶（娱乐目的）──
            // 烟叶保留 RimWorld 默认 SocialDrugs 配置
            AddJoyDrugEntry(entries, ThingDefOf.Beer);
            AddJoyDrugEntry(entries, ThingDefOf.SmokeleafJoint);
            // 精神茶：娱乐 + 计划服用 2 天 1 次（用户需求 2 + 3）
            AddScheduledJoyDrugEntry(entries, DefDatabase<ThingDef>.GetNamedSilentFail("PsychiteTea"),
                PsychiteTeaDaysFrequency);

            // ── CDX 档叠加：活力水 + 清醒丸（携带 1 个 + 计划服用 1 天 1 次）──
            if (tier >= DrugTier.CDX)
            {
                AddCarryDrugEntry(entries, ThingDefOf.Luciferium, 1);
                AddCarryDrugEntry(entries, ThingDefOf.WakeUp, 1);
            }

            // ── AB 档叠加：思滞血清（携带 1 个 + 计划服用 1 天 1 次）──
            if (tier >= DrugTier.AB)
            {
                AddCarryDrugEntry(entries, ThingDefOf.Penoxycyline, 1);
            }

            // S 档的强力血清/钢血血清由 AutoCarry 直接携带，不放入 DrugPolicy

            // 反射赋值 entriesInt 到 DrugPolicy 实例
            SetEntriesInt(policy, entries);
        }

        /// <summary>
        /// 添加社交用药条目（仅 allowedForJoy=true，无计划服用，无携带）。
        /// 对应 RimWorld 默认 SocialDrugs 政策中的条目格式。
        /// </summary>
        private static void AddJoyDrugEntry(List<DrugPolicyEntry> entries, ThingDef drug)
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
        /// 添加娱乐+计划服用条目（allowedForJoy=true + allowScheduled=true + daysFrequency=N）。
        /// 用于精神茶等需要定期服用的社交药品。
        /// </summary>
        private static void AddScheduledJoyDrugEntry(List<DrugPolicyEntry> entries, ThingDef drug, float daysFrequency)
        {
            if (drug == null) return;
            var entry = new DrugPolicyEntry
            {
                drug = drug,
                allowedForJoy = true,
                allowScheduled = true,
                daysFrequency = daysFrequency
            };
            entries.Add(entry);
        }

        /// <summary>
        /// 添加携带列条目（allowedForJoy=true + allowScheduled=true + takeToInventory=N + daysFrequency=1）。
        ///
        /// 关键：必须设置 allowScheduled=true，否则游戏判定"无消费计划"会把殖民者携带的药品丢地上。
        /// 这是用户反馈"携带了会自己丢掉"的根本原因——之前漏设 allowScheduled。
        /// </summary>
        private static void AddCarryDrugEntry(List<DrugPolicyEntry> entries, ThingDef drug, int carryCount)
        {
            if (drug == null) return;
            var entry = new DrugPolicyEntry
            {
                drug = drug,
                allowedForJoy = true,
                allowScheduled = true,
                takeToInventory = carryCount,
                daysFrequency = DefaultCarryDaysFrequency
            };
            entries.Add(entry);
        }

        /// <summary>
        /// 反射设置 DrugPolicy.entriesInt 字段（私有，无公开 setter）。
        ///
        /// 关键修复（2026-08-10）：
        /// 之前用 SetValue 替换整个列表引用，但 DrugPolicy 构造函数可能已初始化 entriesInt 为空列表，
        /// 其他代码（如 Item 索引器、Count 属性）可能持有旧引用导致设置不生效。
        /// 现改为：获取现有列表 → Clear → Add 逐个添加，保留列表引用不变。
        /// 如果现有列表为 null（构造函数未初始化），才用 SetValue 设置新列表。
        /// </summary>
        private static void SetEntriesInt(DrugPolicy policy, List<DrugPolicyEntry> entries)
        {
            if (entriesIntField == null)
            {
                Log.ErrorOnce("[AutoEverything] DrugPolicy.entriesInt 字段反射失败，无法配置用药方案",
                    0xA500);
                return;
            }

            // 获取现有列表（构造函数可能已初始化为空列表）
            var existingList = entriesIntField.GetValue(policy) as List<DrugPolicyEntry>;
            if (existingList == null)
            {
                // 现有列表为 null，直接设置新列表
                entriesIntField.SetValue(policy, entries);
            }
            else
            {
                // 现有列表存在，清空后逐个添加（保留列表引用，避免其他代码持有的引用失效）
                existingList.Clear();
                for (int i = 0; i < entries.Count; i++)
                {
                    existingList.Add(entries[i]);
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
