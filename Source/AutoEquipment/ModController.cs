using System;
using Verse;
using RimWorld;

namespace AutoEquipment
{
    /// <summary>
    /// MOD 启动引导类：游戏加载时由 RimWorld 自动调用静态构造函数。
    /// 在此应用 Harmony 补丁并注入 ThingComp，完成 MOD 初始化。
    /// 注意：[StaticConstructorOnStartup] 时机下 DefDatabase 已加载完毕，
    /// 但游戏（地图、Pawn）尚未初始化——此时给 ThingDef 添加 comp，
    /// 后续生成的 Pawn 会自动带上该 comp，无需等待游戏加载。
    /// </summary>
    [StaticConstructorOnStartup]
    public static class ModController
    {
        static ModController()
        {
            HarmonyPatches.Init();
            HarmonyPatches.AddCompToPawnDefs();
            ValidateTranslationKeys();
            Log.Message("[AutoEquipment] MOD 已初始化（Harmony + ThingComp 注入完成）");
        }

        /// <summary>
        /// 校验枚举对应的翻译键是否完整。
        /// 若新增枚举值忘记加翻译键，会显示 "AE_Role_NewRole" 原文，玩家不可读。
        /// 启动时一次性校验，缺失则 Log.Warning 提示开发者补全。
        /// </summary>
        private static void ValidateTranslationKeys()
        {
            // Role 枚举翻译键校验
            foreach (Role role in Enum.GetValues(typeof(Role)))
            {
                string key = "AE_Role_" + role;
                if (!key.CanTranslate())
                {
                    Log.Warning($"[AutoEquipment] 缺少翻译键: {key}（Role 枚举 {role}）");
                }
            }

            // GearContext 枚举翻译键校验
            foreach (GearContext ctx in Enum.GetValues(typeof(GearContext)))
            {
                string key = "AE_Context_" + ctx;
                if (!key.CanTranslate())
                {
                    Log.Warning($"[AutoEquipment] 缺少翻译键: {key}（GearContext 枚举 {ctx}）");
                }
            }
        }
    }
}
