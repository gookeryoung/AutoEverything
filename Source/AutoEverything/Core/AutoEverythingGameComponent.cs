using RimWorld;
using Verse;

namespace AutoEverything.Core
{
    /// <summary>
    /// AutoEverything 的 GameComponent：作为 AutoExecutor 的全局 Tick 入口。
    ///
    /// 设计动机：
    /// - 原方案通过 CompGearManager（Pawn 上的 ThingComp）驱动 AutoExecutor，
    ///   但 Pawn.SpawnSetup 注入 CompGearManager 与其他装备管理类 MOD 冲突，
    ///   且 CompGearManager 注入到所有人类like Pawn ThingDef 改变原生 ThingDef.comps。
    /// - 改用 GameComponent 后，不再修改任何 ThingDef，不再注入 ThingComp，
    ///   AutoExecutor 由 GameComponent.Tick 全局驱动，零侵入。
    ///
    /// 注册机制：
    /// - 通过 Harmony Postfix patch Game.FinalizeInit，在新游戏/加载存档后自动注册本组件。
    /// - 已注册则跳过，避免重复添加。
    /// - GameComponent 构造必须接受 Game 参数（ RimWorld 约定，用于 ExposeData 重建）。
    /// </summary>
    public class AutoEverythingGameComponent : GameComponent
    {
        // 注：GameComponent 要求有无参构造或带 Game 参数构造，供 Scribe_Deep 重建使用
        // 加载存档时 RimWorld 通过反射创建实例并调用 ExposeData 恢复状态
        public AutoEverythingGameComponent() { }
        public AutoEverythingGameComponent(Game game) { }

        public override void GameComponentTick()
        {
            // AutoExecutor.TryTick 内部有 60 tick 静态门控，每 tick 调用仅做一次 int 比较
            // GameComponentTick 与 TickManager.DoSingleTick 等效，开销可忽略
            try
            {
                AutoExecutor.TryTick();
            }
            catch (System.Exception ex)
            {
                // 异常隔离：Tick 失败不应导致 GameComponent 被移除或游戏崩溃
                Log.ErrorOnce("[AutoEverything] GameComponentTick 失败: " + ex.Message, 0xA700);
            }
        }
    }
}
