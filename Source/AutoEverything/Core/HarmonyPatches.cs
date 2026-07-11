using System.Collections.Generic;
using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using AutoEverything.AutoEquipment;
using AutoEverything.AutoMarkPawn;

namespace AutoEverything.Core
{
    /// <summary>
    /// Auto Everything MOD 的全部 Harmony 补丁集合。
    /// 补丁职责：
    /// 1) 游戏加载时为所有 Pawn 注入 CompGearManager 组件
    /// 2) 在非殖民者高价值 Pawn 头顶绘制红色星标
    /// 全部采用 Postfix 零侵入方式，不拦截原方法。
    /// </summary>
    public static class HarmonyPatches
    {
        // Harmony ID：整个 MOD 单一实例，发布后不可更改
        public const string HarmonyID = "gookeryoung.autoeverything";

        public static void Init()
        {
            var harmony = new Harmony(HarmonyID);
            // 显式 Patch：避免 PatchAll 扫描整个程序集的开销
            harmony.Patch(
                AccessTools.Method(typeof(Pawn), "SpawnSetup"),
                postfix: new HarmonyMethod(typeof(Pawn_SpawnSetup_Patch), nameof(Pawn_SpawnSetup_Patch.Postfix)));
            // PawnUIOverlay.DrawPawnGUIOverlay 补丁：在非殖民者高价值 Pawn 头顶绘制红色星标
            // 类型/方法名可能因 RimWorld 版本差异变化，用 try-catch + null 检查降级
            try
            {
                var overlayType = AccessTools.TypeByName("RimWorld.PawnUIOverlay");
                if (overlayType != null)
                {
                    pawnUIOverlayPawnField = AccessTools.Field(overlayType, "pawn");
                    var drawMethod = AccessTools.Method(overlayType, "DrawPawnGUIOverlay");
                    if (drawMethod != null)
                    {
                        harmony.Patch(drawMethod,
                            postfix: new HarmonyMethod(typeof(PawnUIOverlay_DrawPawnGUIOverlay_Patch),
                                nameof(PawnUIOverlay_DrawPawnGUIOverlay_Patch.Postfix)));
                    }
                    else
                    {
                        Log.Warning("[AutoEverything] PawnUIOverlay.DrawPawnGUIOverlay 未找到，头顶星标降级为无显示");
                    }
                }
                else
                {
                    Log.Warning("[AutoEverything] PawnUIOverlay 类型未找到，头顶星标降级为无显示");
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[AutoEverything] PawnUIOverlay 补丁失败: " + ex.Message);
            }
            Log.Message("[AutoEverything] Harmony 补丁已应用 (显式注册 Postfix)");
        }

        /// <summary>
        /// Pawn 生成到地图时的 Postfix：检查并补注入 CompGearManager 实例。
        /// 关键：RimWorld 加载存档时不会根据 ThingDef.comps 重新创建已存 Pawn 的 comps，
        /// 必须在 Pawn.SpawnSetup 时运行时检查并注入。
        /// 此 Postfix 覆盖所有 Pawn 生成场景：新游戏、加载存档、运行时生成。
        /// </summary>
        public static class Pawn_SpawnSetup_Patch
        {
            public static void Postfix(Pawn __instance)
            {
                // 异常隔离：单个 Pawn 注入失败不应影响其他 Pawn 的 SpawnSetup
                // 历史教训：未隔离时一个 Pawn 异常会导致后续所有 Pawn 都无 Comp
                try
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
                catch (Exception ex)
                {
                    Log.WarningOnce("[AutoEverything] Pawn SpawnSetup 注入失败 " + (__instance?.LabelShort ?? "?") + ": " + ex.Message,
                        (__instance?.thingIDNumber ?? 0) ^ 0x4153);
                }
            }
        }

        // 防止重复注入标志：注入操作只需执行一次
        private static bool _compAdded;

        // PawnUIOverlay.pawn 私有字段缓存：运行时反射查找，类型不存在则为 null
        // Postfix 通过此字段获取 PawnUIOverlay 关联的 Pawn 实例
        private static FieldInfo pawnUIOverlayPawnField;

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
            Log.Message($"[AutoEverything] ThingComp 注入完成: 新增={injected}, 已存在跳过={skipped}, 不适用类别跳过={skippedUnsuitable}");
        }

        /// <summary>
        /// PawnUIOverlay.DrawPawnGUIOverlay 的 Postfix：在非殖民者高价值 Pawn 头顶绘制鲜艳红色星标。
        /// 仅在 autoMarkPawn 开启且 Pawn 为可标记的非殖民者高价值对象（S+）时绘制。
        ///
        /// 实现要点：
        /// - 通过反射获取 PawnUIOverlay.pawn 私有字段（兼容 RimWorld 版本差异，类型不存在则降级）
        /// - 世界坐标转屏幕坐标：pawn.DrawPos 上方约 1.8 格（头顶位置）
        /// - GUI 坐标 Y 轴翻转：Screen.height - screenPos.y
        /// - 不修改任何 Pawn 数据，纯前端绘制，安全可逆
        ///
        /// 标记范围：敌对派系敌人 / 友好派系访客 / 交易者 / 野生人类难民（非殖民者人类）
        /// </summary>
        public static class PawnUIOverlay_DrawPawnGUIOverlay_Patch
        {
            private static readonly Color StarColor = new Color(1.0f, 0.15f, 0.15f);

            public static void Postfix(object __instance)
            {
                if (!AESettings.enabled || !AESettings.autoMarkPawn) return;
                if (pawnUIOverlayPawnField == null) return;

                Pawn pawn;
                try
                {
                    pawn = pawnUIOverlayPawnField.GetValue(__instance) as Pawn;
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce("[AutoEverything] PawnUIOverlay pawn 字段读取失败: " + ex.Message, 0xA610);
                    return;
                }
                if (pawn == null) return;
                if (!PawnMarker.IsMarkableTarget(pawn)) return;
                if (!PawnMarker.IsHighValue(pawn)) return;

                try
                {
                    DrawStarAbovePawn(pawn);
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce("[AutoEverything] 头顶星标绘制失败: " + ex.Message,
                        pawn.thingIDNumber ^ 0xA600);
                }
            }

            /// <summary>
            /// 在 Pawn 头顶绘制鲜艳红色 ★ 图标。
            /// 世界坐标 pawn.DrawPos 上方约 1.8 格 → 屏幕坐标 → GUI 坐标（Y 轴翻转）。
            /// </summary>
            private static void DrawStarAbovePawn(Pawn pawn)
            {
                // 头顶世界坐标：DrawPos 上方（y 轴为高度方向）
                Vector3 worldPos = pawn.DrawPos + new Vector3(0f, 1.8f, 0f);
                Vector3 screenPos = Find.Camera.WorldToScreenPoint(worldPos);
                // screenPos.z <= 0 表示在相机后面或同一平面，不绘制
                if (screenPos.z <= 0) return;

                // GUI 坐标（Y 轴翻转：Unity Screen 原点在左下，GUI 原点在左上）
                float guiX = screenPos.x;
                float guiY = Screen.height - screenPos.y;

                float starSize = 20f;
                Rect starRect = new Rect(guiX - starSize / 2f, guiY - starSize / 2f, starSize, starSize);

                Color prevColor = GUI.color;
                GUI.color = StarColor;
                GameFont prevFont = Text.Font;
                Text.Font = GameFont.Medium;
                TextAnchor prevAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleCenter;
                bool prevWrap = Text.WordWrap;
                Text.WordWrap = false;

                Widgets.Label(starRect, TierTagHelper.StarMarker);

                Text.WordWrap = prevWrap;
                Text.Anchor = prevAnchor;
                Text.Font = prevFont;
                GUI.color = prevColor;
            }
        }
    }
}