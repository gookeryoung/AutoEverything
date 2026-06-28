using System.Collections.Generic;
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
        /// Pawn 生成到地图时的 Postfix：检查并补注入 CompGearManager 实例。
        /// 关键：RimWorld 加载存档时不会根据 ThingDef.comps 重新创建已存 Pawn 的 comps，
        /// 必须在 Pawn.SpawnSetup 时运行时检查并注入。
        /// 此 Postfix 覆盖所有 Pawn 生成场景：新游戏、加载存档、运行时生成。
        /// </summary>
        [HarmonyPatch(typeof(Pawn), "SpawnSetup")]
        public static class Pawn_SpawnSetup_Patch
        {
            static void Postfix(Pawn __instance)
            {
                // 食尸鬼不参与装备管理，跳过
                if (DLCCompat.IsGhoul(__instance)) return;

                // 仅人类like 种族适合装备管理
                // 动物、机械族、昆虫、异常实体等不使用武器装备槽
                if (!PawnSuitabilityChecker.CanManageGear(__instance)) return;

                // 已有组件则跳过，避免重复注入
                if (__instance.GetComp<CompGearManager>() != null) return;

                // 运行时创建 ThingComp 实例并注入
                // 复现 Pawn.AddComps 的标准流程：创建实例 -> 设 parent -> 加入 AllComps -> Initialize
                var comp = new CompGearManager();
                comp.parent = __instance;
                __instance.AllComps.Add(comp);
                comp.Initialize(new CompProperties_GearManager());
            }
        }

        // 防止重复注入标志：注入操作只需执行一次
        private static bool _compAdded;

        /// <summary>
        /// 遍历 DefDatabase 中所有 Pawn 类别 ThingDef，
        /// 若未挂载 CompGearManager 则注入。已存在则跳过，避免重复。
        /// 时机：[StaticConstructorOnStartup]（DefDatabase 已加载，Pawn 未生成）。
        /// 注意：ThingDef.comps 可能为 null（XML 未声明 comps 节点），
        /// 此时应初始化为空列表再注入，而不是跳过——否则 Human 等基础种族会被漏掉。
        /// </summary>
        public static void AddCompToPawnDefs()
        {
            if (_compAdded) return;
            _compAdded = true;

            int injected = 0;
            int skipped = 0;
            int skippedUnsuitable = 0;
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs)
            {
                if (def.category != ThingCategory.Pawn) continue;

                // 适配性过滤：仅给人类like 种族 ThingDef 注入 Comp
                // 动物、机械族、昆虫、异常实体等不使用武器装备槽
                if (!PawnSuitabilityChecker.CanManageGearDef(def))
                {
                    skippedUnsuitable++;
                    continue;
                }

                // 关键修复：comps 为 null 时初始化空列表，而非跳过
                // Human 等基础种族的 ThingDef.comps 可能是 null
                if (def.comps == null) def.comps = new List<CompProperties>();

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
            Log.Message($"[AutoEquipment] ThingComp 注入完成: 新增={injected}, 已存在跳过={skipped}, 不适用类别跳过={skippedUnsuitable}");
        }

        /// <summary>
        /// 取消征召时的 Postfix：若 Pawn 此前为应对近战切出了副武器，
        /// 则恢复其主武器。食尸鬼不使用装备管理，直接跳过。
        /// RimWorld 1.6 起 Pawn_DraftController.SetDrafted 已改为 Drafted 属性，
        /// 改用 MethodType.Setter patch 属性 setter。
        /// </summary>
        [HarmonyPatch(typeof(Pawn_DraftController), nameof(Pawn_DraftController.Drafted), MethodType.Setter)]
        public static class DraftController_SetDrafted_Patch
        {
            static void Postfix(Pawn_DraftController __instance, bool value)
            {
                // 仅处理「取消征召」事件（value=false 时为取消征召）
                if (value) return;
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
