using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace AutoEverything.Core
{
    /// <summary>
    /// 兴趣度（Passion）量化分层：兼容 Vanilla Skills Expanded（VSE）扩展类型。
    ///
    /// 设计意图：RimWorld 原生 Passion 枚举只有 None/Minor/Major 三种，
    /// VSE 通过 PassionDef 扩展了 3 种新类型（Apathy/Natural/Critical）。
    /// 本类把不同 passion 统一映射到 PassionTier 量化值，供评分、排序、角色判定复用。
    ///
    /// VSE 扩展机制：PassionDef.defName 决定类型，PassionManager 分配 index，
    /// SkillRecord.passion 字段存储 index（强转为 Passion 枚举）。
    /// 原版 index 固定：None=0, Minor=1, Major=2；VSE 新增类型 index>=3。
    ///
    /// PassionTier 量化（与项目评级规则对齐）：
    ///   Apathy   = -1（VSE 负面，低于无火）
    ///   None     =  0
    ///   Minor    =  1
    ///   Major    =  2（含 VSE_Natural：学习速率高于 Major 但遗忘快，按双火处理）
    ///   Critical =  3（VSE 最高，不遗忘）
    /// </summary>
    public static class PassionHelper
    {
        /// <summary>
        /// 兴趣度量化分层。用 sbyte 减少内存占用。
        /// 注意：VSE_Natural 不单独设枚举值，在映射时归入 Major（语义等同双火）。
        /// 名称显示通过 nameMap 单独查表保留"自然"区分。
        /// </summary>
        public enum PassionTier : sbyte
        {
            Apathy = -1,
            None = 0,
            Minor = 1,
            Major = 2,
            Critical = 3,
        }

        // 启动时构建的 passion → tier 映射。
        // key 是 SkillRecord.passion 强转 ushort（VSE index 可能超过 byte，但 ushort 足够）。
        private static readonly Dictionary<ushort, PassionTier> tierMap = new Dictionary<ushort, PassionTier>();

        // 启动时构建的 passion → 中文名映射（用于调试明细，保留 Natural/Critical 区分）
        private static readonly Dictionary<ushort, string> nameMap = new Dictionary<ushort, string>();

        // VSE 是否加载（启动时一次性检测，失败时降级为原版 3 档）
        private static readonly bool vseLoaded;

        // 错误去重 salt
        private const int ErrorSalt = 0xA500;

        static PassionHelper()
        {
            try
            {
                vseLoaded = DetectVSE();
                if (vseLoaded)
                {
                    BuildMapsFromVSE();
                }
            }
            catch (Exception ex)
            {
                // 兼容层失败不能阻断主功能：降级为原版 3 档
                Log.ErrorOnce("[AutoEverything] PassionHelper VSE 检测异常: " + ex.Message, ErrorSalt);
                vseLoaded = false;
            }

            // 兜底：无 VSE 或反射失败时，至少填充原版 3 档保证 GetPassionTier 可用
            if (tierMap.Count == 0)
            {
                tierMap[0] = PassionTier.None;
                tierMap[1] = PassionTier.Minor;
                tierMap[2] = PassionTier.Major;
                nameMap[0] = "无火";
                nameMap[1] = "单火";
                nameMap[2] = "双火";
            }
        }

        /// <summary>
        /// 检测 Vanilla Skills Expanded 是否加载。
        /// 遍历 AppDomain 查找 VSE.Passions.PassionManager 类型。
        /// </summary>
        private static bool DetectVSE()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                if (assemblies[i].GetType("VSE.Passions.PassionManager", false) != null)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 从 VSE 反射构建 passion → tier 与 passion → name 映射。
        /// VSE 的 PassionManager.Passions 数组按 index 顺序存储所有 PassionDef。
        /// 通过反射读取 defName 字段，按 defName 决定 tier 与 name。
        /// </summary>
        private static void BuildMapsFromVSE()
        {
            Type passionManagerType = null;
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                passionManagerType = assemblies[i].GetType("VSE.Passions.PassionManager", false);
                if (passionManagerType != null) break;
            }
            if (passionManagerType == null) return;

            // Passions 是 PassionDef[] 静态字段
            FieldInfo passionsField = passionManagerType.GetField("Passions", BindingFlags.Public | BindingFlags.Static);
            if (passionsField == null) return;

            Array passionsArray = passionsField.GetValue(null) as Array;
            if (passionsArray == null) return;

            for (int i = 0; i < passionsArray.Length; i++)
            {
                object passionDef = passionsArray.GetValue(i);
                if (passionDef == null) continue;

                // 反射读 defName 字段（Def.defName 是 public 字段）
                Type passionDefType = passionDef.GetType();
                FieldInfo defNameField = passionDefType.GetField("defName");
                if (defNameField == null) continue;
                string defName = defNameField.GetValue(passionDef) as string;
                if (string.IsNullOrEmpty(defName)) continue;

                tierMap[(ushort)i] = DefNameToTier(defName);
                nameMap[(ushort)i] = DefNameToName(defName);
            }
        }

        /// <summary>
        /// VSE defName → PassionTier 映射。
        /// 原版 defName：None/Minor/Major（VSE 也定义这三个）。
        /// VSE 新增：VSE_Apathy/VSE_Natural/VSE_Critical。
        /// VSE_Natural 归入 Major（语义等同双火，避免枚举值冲突）。
        /// 未知 defName 兜底为 None。
        /// </summary>
        private static PassionTier DefNameToTier(string defName)
        {
            switch (defName)
            {
                case "None": return PassionTier.None;
                case "Minor": return PassionTier.Minor;
                case "Major": return PassionTier.Major;
                case "VSE_Apathy": return PassionTier.Apathy;
                case "VSE_Natural": return PassionTier.Major; // 归入 Major
                case "VSE_Critical": return PassionTier.Critical;
                default: return PassionTier.None;
            }
        }

        /// <summary>
        /// VSE defName → 中文名映射（用于调试明细，保留 Natural 区分）。
        /// </summary>
        private static string DefNameToName(string defName)
        {
            switch (defName)
            {
                case "None": return "无火";
                case "Minor": return "单火";
                case "Major": return "双火";
                case "VSE_Apathy": return "冷漠";
                case "VSE_Natural": return "自然";
                case "VSE_Critical": return "临界";
                default: return "无火";
            }
        }

        /// <summary>
        /// 获取 passion 对应的量化 tier。
        /// 无 VSE 时仅返回原版 3 档（None/Minor/Major），保证向后兼容。
        /// 未知 index（理论不会出现）兜底为 None。
        /// </summary>
        public static PassionTier GetPassionTier(Passion passion)
        {
            ushort idx = (ushort)passion;
            if (tierMap.TryGetValue(idx, out PassionTier tier))
                return tier;
            return PassionTier.None;
        }

        /// <summary>
        /// 获取 passion 的中文名称（用于调试明细输出）。
        /// 通过 nameMap 查表，保留 VSE_Natural/Critical 的区分。
        /// </summary>
        public static string GetPassionName(Passion passion)
        {
            ushort idx = (ushort)passion;
            if (nameMap.TryGetValue(idx, out string name))
                return name;
            return "无火";
        }
    }
}
