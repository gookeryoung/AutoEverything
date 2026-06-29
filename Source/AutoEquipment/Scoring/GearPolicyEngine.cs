using RimWorld;
using Verse;

namespace AutoEquipment.Scoring
{
    /// <summary>
    /// 装备策略调度层：根据全局预设方案返回对应权重。
    /// 设计模式：策略模式——不同预设方案是不同的策略实现。
    /// 角色级覆盖由 CompGearManager.overrideRole/manualRole 处理，不在此处。
    /// </summary>
    public static class GearPolicyEngine
    {
        /// <summary>
        /// 当前全局预设方案。
        /// 默认 Standard，玩家可在设置界面切换。
        /// </summary>
        public static GearPreset ActivePreset { get; private set; } = GearPreset.Standard;

        /// <summary>
        /// 切换全局预设方案。
        /// </summary>
        public static void SwitchPreset(GearPreset preset)
        {
            if (ActivePreset == preset) return;
            ActivePreset = preset;
            Log.Message($"[AutoEquipment] 全局方案切换: {preset}");
        }

        /// <summary>
        /// 获取预设方案对应的评分权重。
        /// 注意：权重仅按预设方案返回，不随角色/情境变化。
        /// 情境加成（如战斗 DPS×1.5）由各 Scorer 内部根据 context 参数处理。
        /// </summary>
        public static GearWeights GetWeights()
        {
            return ActivePreset.GetDefaultWeights();
        }

        /// <summary>
        /// 存档持久化：保存预设方案。
        /// </summary>
        public static void ExposeData()
        {
            GearPreset preset = ActivePreset;
            Scribe_Values.Look(ref preset, "ae_activePreset", GearPreset.Standard);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                ActivePreset = preset;
            }
        }
    }
}
