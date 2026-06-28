using HarmonyLib;
using RimWorld;
using Verse;

namespace AutoEquipment
{
    /// <summary>
    /// Auto Equipment MOD 的全部 Harmony 补丁集合。
    /// 补丁职责：
    /// 1) 游戏加载时为所有 Pawn 注入 CompGearManager 组件
    /// 2) 取消征召时恢复 Pawn 的主武器
    /// 全部采用 Postfix 零侵入方式，不拦截原方法。
    /// </summary>
    public static class HarmonyPatches
    {
        // Harmony ID：整个 MOD 单一实例，发布后不可更改
        public const string HarmonyID = "gookeryoung.autoequipment";

        public static void Init()
        {
            var harmony = new Harmony(HarmonyID);
            harmony.PatchAll();
            Log.Message("[AutoEquipment] Harmony 补丁已应用");
        }

        /// <summary>
        /// 新游戏开始时为所有 Pawn 类型 ThingDef 注入装备管理组件。
        /// 运行时机：游戏初始化，避免修改原始 XML，运行时遍历 DefDatabase 添加。
        /// </summary>
        [HarmonyPatch(typeof(Verse.Game), "InitNewGame")]
        public static class Game_InitNewGame_Patch
        {
            static void Postfix()
            {
                AddCompToPawnDefs();
            }
        }

        /// <summary>
        /// 加载存档时同样注入组件，保证旧存档兼容。
        /// </summary>
        [HarmonyPatch(typeof(ScribeLoader), "LoadGame")]
        public static class ScribeLoader_LoadGame_Patch
        {
            static void Postfix()
            {
                AddCompToPawnDefs();
            }
        }

        // 防止重复注入标志：注入操作只需执行一次
        private static bool _compAdded;

        /// <summary>
        /// 遍历 DefDatabase 中所有 Pawn 类别 ThingDef，
        /// 若未挂载 CompGearManager 则注入。已存在则跳过，避免重复。
        /// 时机：[StaticConstructorOnStartup]（DefDatabase 已加载，Pawn 未生成）。
        /// </summary>
        public static void AddCompToPawnDefs()
        {
            if (_compAdded) return;
            _compAdded = true;

            int injected = 0;
            int skipped = 0;
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs)
            {
                if (def.category != ThingCategory.Pawn) continue;
                if (def.comps == null) continue;

                // 检查是否已存在组件，避免重复注入
                bool hasComp = false;
                foreach (var comp in def.comps)
                {
                    if (comp is CompProperties_GearManager)
                    {
                        hasComp = true;
                        break;
                    }
                }

                if (!hasComp)
                {
                    def.comps.Add(new CompProperties_GearManager());
                    injected++;
                }
                else
                {
                    skipped++;
                }
            }
            Log.Message($"[AutoEquipment] ThingComp 注入完成: 新增={injected}, 已存在跳过={skipped}");
        }

        /// <summary>
        /// 取消征召时的 Postfix：若 Pawn 此前为应对近战切出了副武器，
        /// 则恢复其主武器。食尸鬼不使用装备管理，直接跳过。
        /// </summary>
        [HarmonyPatch(typeof(Pawn_DraftController), "SetDrafted")]
        public static class DraftController_SetDrafted_Patch
        {
            static void Postfix(Pawn_DraftController __instance, bool drafted)
            {
                // 仅处理「取消征召」事件，征召时无需干预
                if (drafted) return;
                Pawn pawn = __instance.pawn;
                if (pawn == null) return;

                // 食尸鬼不使用 CompGearManager，跳过取消征召时的副武器恢复
                if (DLCCompat.IsGhoul(pawn)) return;

                var comp = pawn.GetComp<CompGearManager>();
                if (comp != null)
                {
                    // 异常隔离：单个 Pawn 取消征召失败不应影响其他 Pawn
                    try { comp.OnUndraft(); }
                    catch (System.Exception ex)
                    {
                        Log.Warning("[AutoEquipment] " + pawn.LabelShort + " 取消征召恢复失败: " + ex.Message);
                    }
                }
            }
        }
    }
}
