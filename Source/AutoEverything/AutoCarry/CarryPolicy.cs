using System.Collections.Generic;
using RimWorld;
using Verse;
using AutoEverything.Core;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.AutoCarry
{
    /// <summary>
    /// 携带物品策略：根据玩家在【自动】→ 自动携带面板中的逐项配置生成携带清单。
    ///
    /// 设计意图：
    /// - 食物：按"总量 + 优先级"选取具体食物类型，避免每种食物都带 x3 撑爆背包
    /// - 药品/血清：逐项配置数量（0 表示不携带），目录见 CarryItemCatalog
    /// - 血清默认仅高价值档位携带，可在面板关闭该限制
    ///
    /// 重要：药品实际携带量仍受药品政策"携带"列（takeToInventory）限制。
    /// RimWorld 药品政策系统每 tick 检查背包，超过 takeToInventory 的部分会被丢地上。
    /// AutoDrugPolicy 会把本面板配置的数量同步进 AE-* 三套政策；
    /// 未开启时按 min(配置数量, 政策数量) 派发，避免"拿了又被丢掉"。
    /// 评级门槛（CarryItemDef.MinTier）是唯一来源：开启用药方案时低于门槛的物品
    /// 政策"携带"列为 0，本策略同样跳过，两侧口径一致。
    ///
    /// 特殊兜底（安全优先）：
    /// - 魔鬼素成瘾者即使配置为 0 也携带 1 个（断药会死亡）
    /// - 清醒丸仅对需要睡眠或已成瘾的殖民者携带（不需要睡眠者带了也没用）
    /// </summary>
    public static class CarryPolicy
    {
        // 食物目标数量默认值（实际值由【自动】面板配置，见 AESettings.carryFoodCount）
        public const int FoodCount = 3;

        // 食物 DefName 列表（按优先级从高到低排序）
        // DefName 已校验：e:\SteamLibrary\steamapps\common\RimWorld\Data\Core\Defs\ThingDefs_Items\Items_Food.xml
        // internal 暴露供测试验证优先级顺序
        internal static readonly string[] FoodDefNames =
        {
            "MealSurvivalPack",     // 包装食物（生存口粮）：不腐坏、便于远征
            "Pemmican",             // 干粮：长久保存
            "MealLavish",           // 奢侈餐
            "MealFine",             // 精致餐
            "MealSimple",           // 一般餐
            "MealNutrientPaste"     // 一般餐：营养糊（最低优先级兜底）
        };

        private const string LuciferiumDefName = "Luciferium";
        private const string WakeUpDefName = "WakeUp";

        // ThingDef 缓存：懒加载避免跨线程访问 DefDatabase
        private static List<ThingDef> cachedFoodDefs;
        private static Dictionary<string, ThingDef> cachedCarryDefs;
        private static HediffDef cachedLuciferiumAddiction;
        private static HediffDef cachedWakeUpAddiction;

        /// <summary>
        /// 食物 DefName 已解析为 ThingDef 的列表（按优先级）。
        /// 缺失的 DefName 静默跳过（DLC 差异或 MOD 移除时降级）。
        /// </summary>
        internal static List<ThingDef> FoodDefs
        {
            get
            {
                if (cachedFoodDefs != null) return cachedFoodDefs;
                cachedFoodDefs = new List<ThingDef>();
                for (int i = 0; i < FoodDefNames.Length; i++)
                {
                    ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(FoodDefNames[i]);
                    if (def != null) cachedFoodDefs.Add(def);
                }
                return cachedFoodDefs;
            }
        }

        /// <summary>
        /// 填充殖民者应携带的物品清单到外部缓冲区（避免每次分配新 List）。
        /// 调用方负责在调用前 Clear 缓冲区。
        /// 食物以 IsFoodGroup 条目表示（Def 为 null），由 CarryAllocator 按优先级选具体食物。
        /// </summary>
        public static void FillCarryItems(Pawn pawn, List<CarryEntry> result)
        {
            // ── 食物：总量控制，具体类型由分配器按优先级选取 ──
            if (AESettings.carryFoodCount > 0)
            {
                result.Add(new CarryEntry
                {
                    IsFoodGroup = true,
                    Count = AESettings.carryFoodCount
                });
            }

            bool needSleep = PawnCarryChecker.NeedSleep(pawn);
            bool wakeUpAddicted = HasWakeUpAddiction(pawn);
            bool luciferiumAddicted = HasLuciferiumAddiction(pawn);
            bool highValue = IsHighValue(pawn);

            // 评级门槛：开启用药方案时，AE-* 政策会把低于 CarryItemDef.MinTier 的物品"携带"列
            // 强制为 0（见 DrugPolicyPresets.SyncConfiguredCarryCounts），此处提前跳过，
            // 避免"派发了又被 FirstUnloadableThing 卸下"。未开启时保留面板原有行为。
            CombatTier pawnTier = AESettings.autoDrugPolicyEnabled
                ? CombatEvaluator.GetCombatTier(pawn)
                : CombatTier.X;

            CarryItemDef[] items = CarryItemCatalog.Items;
            for (int i = 0; i < items.Length; i++)
            {
                CarryItemDef item = items[i];
                if (!MeetsTierGate(pawnTier, item.MinTier)) continue;

                int configured = AESettings.GetCarryItemCount(item.DefName);

                if (item.Kind == CarryItemKind.Serum)
                {
                    if (configured <= 0) continue;
                    if (!ShouldCarrySerum(highValue, AESettings.carrySerumHighValueOnly)) continue;
                    ThingDef serumDef = GetCarryDef(item.DefName);
                    if (serumDef != null)
                    {
                        result.Add(new CarryEntry { Def = serumDef, Count = configured });
                    }
                    continue;
                }

                // ── 药品 ──
                if (item.DefName == LuciferiumDefName && luciferiumAddicted)
                {
                    // 成瘾者断药会死亡：无论配置如何都至少带 1 个
                    ThingDef luciferium = GetCarryDef(item.DefName);
                    if (luciferium != null)
                    {
                        result.Add(new CarryEntry { Def = luciferium, Count = 1 });
                    }
                    continue;
                }

                if (configured <= 0) continue;
                if (item.DefName == WakeUpDefName && !ShouldCarryWakeUp(needSleep, wakeUpAddicted)) continue;

                ThingDef def = GetCarryDef(item.DefName);
                if (def == null) continue;

                int policyCount = PawnCarryChecker.GetDrugCarryCount(pawn, def);
                int effective = EffectiveDrugCount(configured, policyCount);
                if (effective > 0)
                {
                    result.Add(new CarryEntry { Def = def, Count = effective });
                }
            }
        }

        /// <summary>
        /// 纯逻辑：配置数量与药品政策数量的实际携带量。
        /// - 配置 0 → 0（玩家明确不携带）
        /// - 政策无条目（-1）→ 配置值（不受卸下限制）
        /// - 政策有条目 → 两者取较小值（超过政策会被游戏丢地上）
        /// </summary>
        internal static int EffectiveDrugCount(int configuredCount, int policyCount)
        {
            if (configuredCount <= 0) return 0;
            if (policyCount < 0) return configuredCount;
            return policyCount < configuredCount ? policyCount : configuredCount;
        }

        /// <summary>纯逻辑：是否满足物品的最低评级门槛（MinTier）。</summary>
        internal static bool MeetsTierGate(CombatTier pawnTier, CombatTier minTier)
        {
            return pawnTier >= minTier;
        }

        /// <summary>纯逻辑：是否需要携带清醒丸（需睡眠或已成瘾）。</summary>
        internal static bool ShouldCarryWakeUp(bool needSleep, bool wakeUpAddicted)
        {
            return needSleep || wakeUpAddicted;
        }

        /// <summary>纯逻辑：是否携带血清（高价值限制关闭时全员携带）。</summary>
        internal static bool ShouldCarrySerum(bool isHighValue, bool highValueOnly)
        {
            return !highValueOnly || isHighValue;
        }

        /// <summary>
        /// 判定 Pawn 是否达到【自动】面板配置的高价值档位（默认 S/SS/SSS）。
        /// 用 GetCombatTier（含自定义评级），与评级标签、高价值标记阈值一致。
        /// </summary>
        private static bool IsHighValue(Pawn pawn)
        {
            if (pawn == null) return false;
            CombatTier tier = CombatEvaluator.GetCombatTier(pawn);
            return tier >= AESettings.markHighValueTier;
        }

        /// <summary>
        /// 检查 Pawn 是否有 Luciferium 成瘾（永久成瘾，每天必须服用）。
        /// HediffDefOf 无此字段，用 DefDatabase 缓存 HediffDef。
        /// </summary>
        private static bool HasLuciferiumAddiction(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null) return false;
            if (cachedLuciferiumAddiction == null)
            {
                cachedLuciferiumAddiction = DefDatabase<HediffDef>.GetNamedSilentFail("LuciferiumAddiction");
                if (cachedLuciferiumAddiction == null) return false;
            }
            return pawn.health.hediffSet.GetFirstHediffOfDef(cachedLuciferiumAddiction) != null;
        }

        /// <summary>
        /// 检查 Pawn 是否有清醒丸成瘾。
        /// 有成瘾的 Pawn 即使不需要睡眠也应携带清醒丸（满足渴求避免戒断）。
        /// </summary>
        private static bool HasWakeUpAddiction(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null) return false;
            if (cachedWakeUpAddiction == null)
            {
                cachedWakeUpAddiction = DefDatabase<HediffDef>.GetNamedSilentFail("WakeUpAddiction");
                if (cachedWakeUpAddiction == null) return false;
            }
            return pawn.health.hediffSet.GetFirstHediffOfDef(cachedWakeUpAddiction) != null;
        }

        /// <summary>
        /// 按 DefName 解析并缓存携带物品 ThingDef。
        /// 缺失（缺 DLC/被移除）也缓存 null，避免每周期重复查询。
        /// </summary>
        private static ThingDef GetCarryDef(string defName)
        {
            if (cachedCarryDefs == null) cachedCarryDefs = new Dictionary<string, ThingDef>();
            ThingDef def;
            if (cachedCarryDefs.TryGetValue(defName, out def)) return def;
            def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            cachedCarryDefs[defName] = def;
            return def;
        }
    }

    /// <summary>
    /// 携带物品条目：ThingDef + 目标数量。
    /// IsFoodGroup=true 时 Def 为 null，表示"食物总量"，由分配器按优先级选取具体食物。
    /// 结构体避免堆分配，列表中的元素直接存值。
    /// </summary>
    public struct CarryEntry
    {
        public ThingDef Def;
        public int Count;
        public bool IsFoodGroup;
    }
}
