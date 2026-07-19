using System.Collections.Generic;
using RimWorld;
using Verse;

namespace AutoEverything.AutoEquipment
{
    /// <summary>
    /// 装备层过滤器：按需求"仅护甲类（含衣物层），不含附件"过滤候选装备。
    ///
    /// 设计意图：
    /// - 包含所有提供保护/保暖/装饰性穿戴的层（OnSkin/Shell/Middle/Overhead/EyeCover）
    /// - 排除附件层（Belt=腰带附件、Backpack/Bag/Pack=容器类，若由 DLC/mod 注册）
    /// - 武器不在 Apparel 系统内，本过滤器天然不涉及；调用方应只对 Apparel 实例调用
    ///
    /// 实现说明：
    /// - ApparelLayerDefOf 原版只有 Belt（其他 Backpack/Bag/Pack 由 DLC/mod 通过 defName 注册）
    /// - 用 DefDatabase&lt;T&gt;.GetNamed 动态查询，找不到则忽略（null 检查）
    ///   RimWorld 中 DefDatabase 是泛型类（DefDatabase&lt;T&gt;），GetNamed 是其静态方法
    /// - [StaticConstructorOnStartup] 确保 DefDatabase 就绪后再访问
    /// - 用 HashSet&lt;Def&gt; 查询 O(1)，避免每次分配 LINQ
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class ApparelLayerFilter
    {
        // 排除层集合：这些层属于附件/容器，不是护甲或衣物
        private static readonly HashSet<Def> excludedLayers = new HashSet<Def>();

        static ApparelLayerFilter()
        {
            // ApparelLayerDefOf.Belt 是原版唯一定义的附件层
            TryAddExcluded(ApparelLayerDefOf.Belt);
            // Backpack/Bag/Pack 由 DLC/mod 通过 defName 注册（原版 ApparelLayerDefOf 不含）
            // 用 DefDatabase<T>.GetNamed 动态查询，找不到则跳过（不影响无 DLC 环境）
            TryAddExcluded(DefDatabase<ApparelLayerDef>.GetNamed("Backpack", false));
            TryAddExcluded(DefDatabase<ApparelLayerDef>.GetNamed("Bag", false));
            TryAddExcluded(DefDatabase<ApparelLayerDef>.GetNamed("Pack", false));
        }

        private static void TryAddExcluded(Def layer)
        {
            if (layer != null) excludedLayers.Add(layer);
        }

        /// <summary>
        /// 判定 apparel 是否在自动分配范围内。
        /// - 必须有 ApparelProperties
        /// - 至少有一层不在排除集合内（即提供护甲或衣物功能）
        /// </summary>
        public static bool IsRelevant(Apparel apparel)
        {
            if (apparel == null) return false;
            ApparelProperties props = apparel.def?.apparel;
            if (props == null) return false;
            List<ApparelLayerDef> layers = props.layers;
            if (layers == null || layers.Count == 0) return false;

            for (int i = 0; i < layers.Count; i++)
            {
                if (!excludedLayers.Contains(layers[i])) return true;
            }
            return false;
        }
    }
}
