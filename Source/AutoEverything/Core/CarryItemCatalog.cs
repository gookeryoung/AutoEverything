using System.Collections.Generic;

namespace AutoEverything.Core
{
    /// <summary>携带物品类别：药品（成瘾品）与强化血清。</summary>
    public enum CarryItemKind : byte
    {
        Drug = 0,
        Serum = 1
    }

    /// <summary>
    /// 单品携带配置项：DefName + 类别 + 默认数量。
    /// 目录固定，避免玩家在 UI 里逐项添加 DefName；默认数量保持旧行为。
    /// </summary>
    public sealed class CarryItemDef
    {
        public readonly string DefName;
        public readonly CarryItemKind Kind;
        public readonly int DefaultCount;

        public CarryItemDef(string defName, CarryItemKind kind, int defaultCount)
        {
            DefName = defName;
            Kind = kind;
            DefaultCount = defaultCount;
        }
    }

    /// <summary>
    /// 自动携带的单品目录（药品 + 血清）。
    ///
    /// 设计意图：
    /// - 玩家在【自动】→ 自动携带面板逐项配置数量，数量为 0 表示不携带
    /// - 食物不在此目录：食物按"总量 + 优先级"选择具体食物，见 CarryPolicy.FoodDefNames
    /// - DefName 均为原版（含 Anomaly）DefName，缺失时（缺 DLC/被移除）运行时自动跳过
    /// - 默认数量保持旧行为：清醒丸/活力水/佩诺西林/精神茶/三种血清各 1，其余 0
    /// </summary>
    public static class CarryItemCatalog
    {
        // UI 显示顺序即此数组顺序（药品与血清各自分组）
        public static readonly CarryItemDef[] Items =
        {
            // ── 药品（成瘾品）──
            new CarryItemDef("WakeUp", CarryItemKind.Drug, 1),           // 清醒丸：抵抗睡眠
            new CarryItemDef("GoJuice", CarryItemKind.Drug, 1),          // 活力水：战斗移速/瞄准
            new CarryItemDef("Penoxycyline", CarryItemKind.Drug, 1),     // 佩诺西林：预防疟疾
            new CarryItemDef("PsychiteTea", CarryItemKind.Drug, 1),      // 精神茶：心情
            new CarryItemDef("Luciferium", CarryItemKind.Drug, 0),       // 魔鬼素：永久成瘾才必须携带
            new CarryItemDef("Beer", CarryItemKind.Drug, 0),             // 啤酒
            new CarryItemDef("SmokeleafJoint", CarryItemKind.Drug, 0),   // 烟叶
            new CarryItemDef("Ambrosia", CarryItemKind.Drug, 0),         // 仙馔
            new CarryItemDef("Flake", CarryItemKind.Drug, 0),            // 薄片（政策默认禁止服用）
            new CarryItemDef("Yayo", CarryItemKind.Drug, 0),             // 亚咯（政策默认禁止服用）

            // ── 强化血清（Anomaly DLC）──
            new CarryItemDef("JuggernautSerum", CarryItemKind.Serum, 1), // 强力血清
            new CarryItemDef("MetalbloodSerum", CarryItemKind.Serum, 1), // 钢血血清
            new CarryItemDef("MindNumbSerum", CarryItemKind.Serum, 1),   // 思滞血清
            new CarryItemDef("VoidsightSerum", CarryItemKind.Serum, 0)   // 虚空视界血清
        };

        /// <summary>按目录生成默认存档条目（"defName#count"）。</summary>
        public static List<string> CreateDefaultEntries()
        {
            List<string> entries = new List<string>(Items.Length);
            for (int i = 0; i < Items.Length; i++)
            {
                entries.Add(Items[i].DefName + "#" + Items[i].DefaultCount);
            }
            return entries;
        }

        /// <summary>查询默认数量；目录外返回 0（不携带）。</summary>
        public static int GetDefaultCount(string defName)
        {
            for (int i = 0; i < Items.Length; i++)
            {
                if (Items[i].DefName == defName) return Items[i].DefaultCount;
            }
            return 0;
        }
    }
}
