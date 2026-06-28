using Verse;
using RimWorld;

namespace AutoEquipment
{
    /// <summary>
    /// MOD 启动引导类：游戏加载时由 RimWorld 自动调用静态构造函数。
    /// 在此应用 Harmony 补丁完成 MOD 初始化。
    /// </summary>
    [StaticConstructorOnStartup]
    public static class ModController
    {
        static ModController()
        {
            HarmonyPatches.Init();
            Log.Message("[AutoEquipment] MOD 已初始化");
        }
    }
}
