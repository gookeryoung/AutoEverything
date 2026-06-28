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
            Log.Message("[AutoEquipment] MOD 已初始化（Harmony + ThingComp 注入完成）");
        }
    }
}
