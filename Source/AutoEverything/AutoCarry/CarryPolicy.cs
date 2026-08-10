using System.Collections.Generic;
using RimWorld;
using Verse;
using AutoEverything.Core;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.AutoCarry
{
    /// <summary>
    /// 携带物品策略：定义殖民者应自动携带的物品清单与数量。
    ///
    /// 设计意图：
    /// - 食物 x3：殖民者短期远征或战斗中的口粮
    /// - 活力水/清醒丸/思滞血清：数量来源于药品政策"携带"列（takeToInventory），
    ///   由 AutoDrugPolicyManager 按评级自动配置 AE-S/AE-AB/AE-CDX 三套政策
    /// - 强力血清/钢血血清：Anomaly DLC 血清类，不在 DrugPolicy 系统（无 Comp_Drug），
    ///   由 AutoCarry 按评级直接携带（仅 S 档携带）
    ///
    /// 重要：药品携带量不再硬编码为 1，必须尊重药品政策的 takeToInventory 字段！
    /// 因为 RimWorld 药品政策系统每 tick 检查背包，超过 takeToInventory 的部分会被丢地上。
    /// AutoDrugPolicyManager 已按评级配置好政策，AutoCarry 读政策 takeToInventory 补库存即可。
    ///
    /// 血清类（JuggernautSerum/MetalbloodSerum）走 AutoCarry 直接携带：
    /// - 没有 Comp_Drug 组件，药品政策系统不会自动管理其库存
    /// - S 档殖民者（评级 S/SS/SSS）携带 1 个强力血清 + 1 个钢血血清
    /// - 缺 DLC 时血清 ThingDef 为 null，自动跳过
    ///
    /// 食物优先级（用户决策 2026-08-09）：
    /// 包装食物 → 干粮 → 奢侈餐 → 精致餐 → 一般餐
    /// </summary>
    public static class CarryPolicy
    {
        // 食物目标数量：3 个
        public const int FoodCount = 3;

        // 血清类目标数量：各 1 个（仅 S 档携带）
        public const int SerumCount = 1;

        // 药品"携带"列默认值（无政策条目或反射失败时的兜底数量——0 = 不携带，避免丢地上）
        private const int DefaultDrugCarryCount = 0;

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

        // 血清类 DefName（Anomaly DLC，缺失时 GetNamedSilentFail 返回 null 自动跳过）
        private const string JuggernautSerumDefName = "JuggernautSerum";   // 强力血清
        private const string MetalbloodSerumDefName = "MetalbloodSerum";   // 钢血血清
        private const string MindNumbSerumDefName = "MindNumbSerum";       // 思滞血清

        // ThingDef 缓存：懒加载避免跨线程访问 DefDatabase
        private static List<ThingDef> cachedFoodDefs;
        private static ThingDef cachedLuciferium;
        private static ThingDef cachedWakeUp;
        private static ThingDef cachedPenoxycyline;
        private static ThingDef cachedJuggernautSerum;
        private static ThingDef cachedMetalbloodSerum;
        private static ThingDef cachedMindNumbSerum;
        private static bool serumDefsResolved = false;

        /// <summary>
        /// 懒加载并缓存食物 ThingDef 列表（仅执行一次）。
        /// 缺失的 DefName 静默跳过（DLC 差异或 MOD 移除时降级）。
        /// </summary>
        private static List<ThingDef> GetFoodDefs()
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

        /// <summary>
        /// 填充殖民者应携带的物品清单到外部缓冲区（避免每次分配新 List）。
        /// 调用方负责在调用前 Clear 缓冲区。
        ///
        /// 物品分层：
        /// 1. 食物：始终携带 x3
        /// 2. 药品类：读 DrugPolicy 的 takeToInventory（由 AutoDrugPolicyManager 按评级配置）
        /// 3. 血清类：按评级直接判定（S 档携带强力+钢血血清）
        /// </summary>
        public static void FillCarryItems(Pawn pawn, List<CarryEntry> result)
        {
            // 1. 食物：始终携带
            List<ThingDef> foodDefs = GetFoodDefs();
            for (int i = 0; i < foodDefs.Count; i++)
            {
                result.Add(new CarryEntry { Def = foodDefs[i], Count = FoodCount });
            }

            // 2. 药品类：读 DrugPolicy 的 takeToInventory
            AddDrugIfPolicyCarry(pawn, GetLuciferiumDef(), result);
            if (PawnCarryChecker.NeedSleep(pawn))
            {
                AddDrugIfPolicyCarry(pawn, GetWakeUpDef(), result);
            }
            AddDrugIfPolicyCarry(pawn, GetPenoxycylineDef(), result);

            // 3. 血清类：S 档（S/SS/SSS）携带强力+钢血+思滞血清（AutoCarry 直接携带，DrugPolicy 配 takeToInventory 防卸下）
            if (IsTierS(pawn))
            {
                AddSerumIfAvailable(GetJuggernautSerumDef(), result);
                AddSerumIfAvailable(GetMetalbloodSerumDef(), result);
                AddSerumIfAvailable(GetMindNumbSerumDef(), result);
            }
        }

        /// <summary>
        /// 判定 Pawn 是否为 S 档（S/SS/SSS）。
        /// 用 GetCombatTier（含自定义评级），与评级标签一致。
        /// </summary>
        private static bool IsTierS(Pawn pawn)
        {
            if (pawn == null) return false;
            CombatTier tier = CombatEvaluator.GetCombatTier(pawn);
            return tier == CombatTier.S || tier == CombatTier.SS || tier == CombatTier.SSS;
        }

        /// <summary>
        /// 添加药品到携带清单（如果药品政策 takeToInventory > 0）。
        /// </summary>
        private static void AddDrugIfPolicyCarry(Pawn pawn, ThingDef drugDef, List<CarryEntry> result)
        {
            if (drugDef == null) return;
            int policyCount = PawnCarryChecker.GetDrugCarryCount(pawn, drugDef);
            if (policyCount > 0)
            {
                result.Add(new CarryEntry { Def = drugDef, Count = policyCount });
            }
        }

        /// <summary>
        /// 添加血清到携带清单（数量固定为 SerumCount=1，仅 S 档调用）。
        /// </summary>
        private static void AddSerumIfAvailable(ThingDef serumDef, List<CarryEntry> result)
        {
            if (serumDef == null) return;
            result.Add(new CarryEntry { Def = serumDef, Count = SerumCount });
        }

        /// <summary>活力水 ThingDef（懒加载，缺失返回 null）</summary>
        internal static ThingDef GetLuciferiumDef()
        {
            if (cachedLuciferium == null)
                cachedLuciferium = ThingDefOf.Luciferium;
            return cachedLuciferium;
        }

        /// <summary>清醒丸 ThingDef（懒加载，缺失返回 null）</summary>
        internal static ThingDef GetWakeUpDef()
        {
            if (cachedWakeUp == null)
                cachedWakeUp = ThingDefOf.WakeUp;
            return cachedWakeUp;
        }

        /// <summary>佩诺西林（抗疟药）ThingDef（懒加载，缺失返回 null）</summary>
        internal static ThingDef GetPenoxycylineDef()
        {
            if (cachedPenoxycyline == null)
                cachedPenoxycyline = ThingDefOf.Penoxycyline;
            return cachedPenoxycyline;
        }

        /// <summary>
        /// 强力血清 ThingDef（Anomaly DLC，懒加载，缺失返回 null）。
        /// 缺 DLC 时 GetNamedSilentFail 返回 null，AutoCarry 自动跳过该血清。
        /// </summary>
        internal static ThingDef GetJuggernautSerumDef()
        {
            ResolveSerumDefsOnce();
            return cachedJuggernautSerum;
        }

        /// <summary>
        /// 钢血血清 ThingDef（Anomaly DLC，懒加载，缺失返回 null）。
        /// 缺 DLC 时 GetNamedSilentFail 返回 null，AutoCarry 自动跳过该血清。
        /// </summary>
        internal static ThingDef GetMetalbloodSerumDef()
        {
            ResolveSerumDefsOnce();
            return cachedMetalbloodSerum;
        }

        /// <summary>
        /// 思滞血清 ThingDef（Anomaly DLC，懒加载，缺失返回 null）。
        /// 缺 DLC 时 GetNamedSilentFail 返回 null，AutoCarry 自动跳过该血清。
        /// </summary>
        internal static ThingDef GetMindNumbSerumDef()
        {
            ResolveSerumDefsOnce();
            return cachedMindNumbSerum;
        }

        /// <summary>
        /// 一次性解析血清 DefName（避免重复 GetNamedSilentFail）。
        /// null 也作为已解析结果缓存（缺 DLC 时不会再查）。
        /// </summary>
        private static void ResolveSerumDefsOnce()
        {
            if (serumDefsResolved) return;
            serumDefsResolved = true;
            cachedJuggernautSerum = DefDatabase<ThingDef>.GetNamedSilentFail(JuggernautSerumDefName);
            cachedMetalbloodSerum = DefDatabase<ThingDef>.GetNamedSilentFail(MetalbloodSerumDefName);
            cachedMindNumbSerum = DefDatabase<ThingDef>.GetNamedSilentFail(MindNumbSerumDefName);
        }
    }

    /// <summary>
    /// 携带物品条目：ThingDef + 目标数量。
    /// 结构体避免堆分配，列表中的元素直接存值。
    /// </summary>
    public struct CarryEntry
    {
        public ThingDef Def;
        public int Count;
    }
}
