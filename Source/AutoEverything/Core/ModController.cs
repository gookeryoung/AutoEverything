using System;
using Verse;
using RimWorld;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.Core
{
    /// <summary>
    /// MOD 启动引导类：游戏加载时由 RimWorld 自动调用静态构造函数。
    /// 在此应用 Harmony 补丁（Game.FinalizeInit Postfix 注册 GameComponent +
    /// PawnUIOverlay Postfix 绘制头顶星标），并校验翻译键完整性。
    /// </summary>
    [StaticConstructorOnStartup]
    public static class ModController
    {
        static ModController()
        {
            HarmonyPatches.Init();
            ValidateTranslationKeys();
            Log.Message("[AutoEverything] MOD 已初始化（Harmony GameComponent 注册 + PawnUIOverlay 星标补丁）");
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
                    Log.Warning($"[AutoEverything] 缺少翻译键: {key}（Role 枚举 {role}）");
                }
            }

            // GearContext 枚举翻译键校验
            foreach (GearContext ctx in Enum.GetValues(typeof(GearContext)))
            {
                string key = "AE_Context_" + ctx;
                if (!key.CanTranslate())
                {
                    Log.Warning($"[AutoEverything] 缺少翻译键: {key}（GearContext 枚举 {ctx}）");
                }
            }

            // ColonistBarSortMode 枚举翻译键校验
            foreach (ColonistBarSortMode mode in Enum.GetValues(typeof(ColonistBarSortMode)))
            {
                string key = "AE_SortMode_" + mode;
                if (!key.CanTranslate())
                {
                    Log.Warning($"[AutoEverything] 缺少翻译键: {key}（ColonistBarSortMode 枚举 {mode}）");
                }
            }
        }
    }
}