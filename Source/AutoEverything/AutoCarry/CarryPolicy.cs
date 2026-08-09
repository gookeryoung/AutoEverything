using System.Collections.Generic;
using RimWorld;
using Verse;

namespace AutoEverything.AutoCarry
{
    /// <summary>
    /// 携带物品策略：定义殖民者应自动携带的物品清单与数量。
    ///
    /// 设计意图：
    /// - 食物 x3：殖民者短期远征或战斗中的口粮
    /// - 活力水（Luciferium）x1：上瘾者维持补给（不会让无瘾者强制上瘾，玩家政策禁用时不带）
    /// - 清醒丸（WakeUp）x1：抵抗突发疲劳/睡眠（不需要睡眠的殖民者不带）
    /// - 思滞血清（Penoxycyline）x1：预防机械孢子/瘟疫/疟疾
    ///
    /// 食物优先级（用户决策 2026-08-09）：
    /// 包装食物 → 干粮 → 奢侈餐 → 精致餐 → 一般餐
    /// 取舍逻辑：地图仓库中第一个可用的食物类型即为该殖民者的目标食物
    /// （包装食物优先因为不腐坏、便于远征；干粮次之；新鲜餐按品质降级兜底）
    ///
    /// 注：硬编码数量与列表遵循 KISS 原则。如未来需可配置，可改为 Mod 选项滑块。
    /// </summary>
    public static class CarryPolicy
    {
        // 食物目标数量：3 个
        public const int FoodCount = 3;

        // 药品目标数量：各 1 个
        public const int LuciferiumCount = 1;
        public const int WakeUpCount = 1;
        public const int PenoxycylineCount = 1;

        // 食物 DefName 列表（按优先级从高到低排序）
        // 顺序依据用户需求：包装食物 → 干粮 → 奢侈 → 精致 → 一般
        // 注：使用 defName 字符串而非 ThingDef 直接缓存，避免静态字段初始化器跨线程访问 DefDatabase
        // internal 暴露供测试验证优先级顺序
        internal static readonly string[] FoodDefNames =
        {
            "PackagedSurvivalMeal", // 包装食物：不腐坏、便于远征
            "Pemmican",             // 干粮：长久保存
            "JerkedDriedMeat",      // 干粮：风干肉（CORE 1.4+）
            "MealLavish",           // 奢侈餐
            "MealFine",             // 精致餐
            "MealSimple",           // 一般餐
            "MealNutrientPaste"     // 一般餐：营养糊（最低优先级兜底）
        };

        // 食物 ThingDef 缓存：懒加载避免跨线程访问 DefDatabase
        private static List<ThingDef> cachedFoodDefs;
        private static ThingDef cachedLuciferium;
        private static ThingDef cachedWakeUp;
        private static ThingDef cachedPenoxycyline;

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
        /// 根据殖民者状态过滤：
        /// - 不需要睡眠者：不带清醒丸（WakeUp 主要用于抵抗睡眠，无睡眠需求则无意义）
        /// - 禁止活力水者：不带活力水（玩家在药品政策中禁用 Luciferium 时不带）
        /// </summary>
        public static void FillCarryItems(Pawn pawn, List<CarryEntry> result)
        {
            bool needSleep = PawnCarryChecker.NeedSleep(pawn);
            bool luciferiumAllowed = PawnCarryChecker.LuciferiumAllowed(pawn);
            FillCarryItemsCore(needSleep, luciferiumAllowed, result);
        }

        /// <summary>
        /// 纯逻辑版本：根据需求判定填充应携带的物品清单。
        /// 仅供测试调用，生产路径请用 FillCarryItems(Pawn, List)。
        /// </summary>
        /// <param name="needSleep">true 表示殖民者需要睡眠（带清醒丸）；false 不需要睡眠（不带）</param>
        /// <param name="luciferiumAllowed">true 表示药品政策允许活力水；false 禁止</param>
        /// <param name="result">输出缓冲区，调用方负责 Clear</param>
        internal static void FillCarryItemsCore(bool needSleep, bool luciferiumAllowed, List<CarryEntry> result)
        {
            // 食物：始终携带（无个性化过滤）
            List<ThingDef> foodDefs = GetFoodDefs();
            for (int i = 0; i < foodDefs.Count; i++)
            {
                result.Add(new CarryEntry { Def = foodDefs[i], Count = FoodCount });
            }

            // 活力水：仅当药品政策允许时携带
            if (luciferiumAllowed)
            {
                ThingDef luciferium = GetLuciferiumDef();
                if (luciferium != null)
                    result.Add(new CarryEntry { Def = luciferium, Count = LuciferiumCount });
            }

            // 清醒丸：仅当殖民者需要睡眠时携带
            if (needSleep)
            {
                ThingDef wakeUp = GetWakeUpDef();
                if (wakeUp != null)
                    result.Add(new CarryEntry { Def = wakeUp, Count = WakeUpCount });
            }

            // 思滞血清：始终携带（预防疾病，无个性化过滤）
            ThingDef penoxycyline = GetPenoxycylineDef();
            if (penoxycyline != null)
                result.Add(new CarryEntry { Def = penoxycyline, Count = PenoxycylineCount });
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

        /// <summary>思滞血清 ThingDef（懒加载，缺失返回 null）</summary>
        internal static ThingDef GetPenoxycylineDef()
        {
            if (cachedPenoxycyline == null)
                cachedPenoxycyline = ThingDefOf.Penoxycyline;
            return cachedPenoxycyline;
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
