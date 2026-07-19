using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using AutoEverything.AutoMarkPawn;

namespace AutoEverything.Core
{
    /// <summary>
    /// Auto Everything MOD 的全部 Harmony 补丁集合。
    /// 补丁职责：
    /// 1) Game.FinalizeInit Postfix：注册 AutoEverythingGameComponent（作为 AutoExecutor 的 Tick 入口）
    /// 2) PawnUIOverlay.DrawPawnGUIOverlay Postfix：在 S+ 档次人类 Pawn 头顶绘制彩色星标（按类别区分颜色）
    /// 全部采用 Postfix 零侵入方式，不拦截原方法。
    ///
    /// 注：原 Pawn.SpawnSetup Postfix 注入 CompGearManager 的逻辑已移除——
    /// 该机制修改所有人类like Pawn ThingDef.comps，与其他装备管理类 MOD 冲突。
    /// 现改用 GameComponent 全局 Tick 驱动 AutoExecutor，零 ThingDef 修改。
    /// </summary>
    public static class HarmonyPatches
    {
        // Harmony ID：整个 MOD 单一实例，发布后不可更改
        public const string HarmonyID = "gookeryoung.autoeverything";

        public static void Init()
        {
            var harmony = new Harmony(HarmonyID);
            // 显式 Patch：避免 PatchAll 扫描整个程序集的开销

            // Game.FinalizeInit Postfix：新游戏/加载存档后注册 GameComponent
            harmony.Patch(
                AccessTools.Method(typeof(Game), nameof(Game.FinalizeInit)),
                postfix: new HarmonyMethod(typeof(Game_FinalizeInit_Patch), nameof(Game_FinalizeInit_Patch.Postfix)));

            // PawnUIOverlay.DrawPawnGUIOverlay 补丁：在 S+ 档次人类 Pawn 头顶绘制彩色星标（按类别区分颜色）
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
            Log.Message("[AutoEverything] Harmony 补丁已应用 (GameComponent 注册 + PawnUIOverlay 彩色星标)");
        }

        /// <summary>
        /// Game.FinalizeInit Postfix：在新游戏/加载存档后注册 AutoEverythingGameComponent。
        /// 已注册则跳过，避免重复添加。
        /// FinalizeInit 在新游戏和加载存档两种场景都会被调用，是注册 GameComponent 的最佳时机。
        /// </summary>
        public static class Game_FinalizeInit_Patch
        {
            public static void Postfix(Game __instance)
            {
                try
                {
                    // 检查是否已注册（避免重复添加）
                    List<GameComponent> comps = __instance.components;
                    for (int i = 0; i < comps.Count; i++)
                    {
                        if (comps[i] is AutoEverythingGameComponent) return;
                    }
                    comps.Add(new AutoEverythingGameComponent(__instance));
                    AEDebug.Log(() => "[AutoEverything] AutoEverythingGameComponent 已注册");
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce("[AutoEverything] GameComponent 注册失败: " + ex.Message, 0xA710);
                }
            }
        }

        // PawnUIOverlay.pawn 私有字段缓存：运行时反射查找，类型不存在则为 null
        // Postfix 通过此字段获取 PawnUIOverlay 关联的 Pawn 实例
        private static FieldInfo pawnUIOverlayPawnField;

        /// <summary>
        /// PawnUIOverlay.DrawPawnGUIOverlay 的 Postfix：在 S+ 档次人类 Pawn 头顶绘制彩色星标。
        /// 仅在 autoMarkPawn 开启且 Pawn 为可标记的高价值对象（S+）时绘制。
        ///
        /// 实现要点：
        /// - 通过反射获取 PawnUIOverlay.pawn 私有字段（兼容 RimWorld 版本差异，类型不存在则降级）
        /// - 世界坐标转屏幕坐标：pawn.DrawPos 上方约 1.8 格（头顶位置）
        /// - GUI 坐标 Y 轴翻转：Screen.height - screenPos.y
        /// - 颜色按 Pawn 类别动态取色：殖民者=金、奴隶=橙、囚犯=黄、敌对=红、中立/盟友=青、野生=白
        /// - 不修改任何 Pawn 数据，纯前端绘制，安全可逆
        ///
        /// 标记范围：所有人类like 单位（殖民者、奴隶、囚犯、敌对、中立/盟友、野生人类）
        /// </summary>
        public static class PawnUIOverlay_DrawPawnGUIOverlay_Patch
        {
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
            /// 在 Pawn 头顶绘制彩色 ★ 图标，颜色按 Pawn 类别取自 <see cref="PawnMarker.GetMarkerColor"/>。
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

                // 按类别取色：殖民者=金、奴隶=橙、囚犯=黄、敌对=红、中立/盟友=青、野生=白
                Color starColor = PawnMarker.GetMarkerColor(PawnMarker.GetMarkerCategory(pawn));

                Color prevColor = GUI.color;
                GUI.color = starColor;
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
