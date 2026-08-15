using RimWorld;
using Verse;

namespace AutoEverything.Core
{
    /// <summary>
    /// AutoEverything 的 GameComponent：仅用于旧存档兼容（2026-08-15 起不再注册新实例）。
    ///
    /// 演进历史：
    /// - 早期方案通过 CompGearManager（Pawn 上的 ThingComp）驱动 AutoExecutor，
    ///   但 Pawn.SpawnSetup 注入 CompGearManager 与其他装备管理类 MOD 冲突，
    ///   且 CompGearManager 注入到所有人类like Pawn ThingDef 改变原生 ThingDef.comps。
    /// - 中期方案改为 GameComponent（Game.FinalizeInit Postfix 注册），零 ThingDef 修改，
    ///   但组件实例会随存档深序列化持久化——玩家卸载 MOD 后加载旧存档
    ///   需依赖残留组件类型，存档纯净性差。
    /// - 当前方案（2026-08-15）：全局 Tick 入口改由 Harmony TickManager.DoSingleTick
    ///   Postfix 直接驱动 AutoExecutor（见 HarmonyPatches），新存档不再写入本组件。
    ///
    /// 本类保留的原因：
    /// - 旧版本保存的存档中含本组件节点，RimWorld 加载时需按类名反序列化重建实例；
    /// 删除类会导致旧存档加载报错。
    /// - 旧存档加载后 GameComponentTick 与 DoSingleTick 入口双路调用
    ///   AutoExecutor.TryTick——TryTick 入口的 60 tick 门控保证幂等，无双倍执行。
    /// </summary>
    public class AutoEverythingGameComponent : GameComponent
    {
        // 注：GameComponent 要求有无参构造或带 Game 参数构造，供 Scribe_Deep 重建使用
        // 加载存档时 RimWorld 通过反射创建实例并调用 ExposeData 恢复状态
        public AutoEverythingGameComponent() { }
        public AutoEverythingGameComponent(Game game) { }

        public override void GameComponentTick()
        {
            // 仅旧存档走到这里（新存档无本组件实例）；与 DoSingleTick 入口幂等并存
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
