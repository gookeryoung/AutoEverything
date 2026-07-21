using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using AutoEverything.AutoMarkPawn;
using AutoEverything.AutoEquipment;

namespace AutoEverything.Core
{
    /// <summary>
    /// Auto Everything MOD 的全部 Harmony 补丁集合。
    /// 补丁职责：
    /// 1) Game.FinalizeInit Postfix：注册 AutoEverythingGameComponent（作为 AutoExecutor 的 Tick 入口）
    /// 2) ColonistBarColonistDrawer.DrawColonist Postfix：在殖民者栏固定位置为人类 Pawn 绘制角色定位图标
    /// 3) Thing.SpawnSetup Postfix：装备生成/殖民者生成时标记装备分配脏标（事件驱动）
    /// 4) Thing.Destroy Postfix：装备销毁/殖民者死亡时标记装备分配脏标
    /// 5) Pawn.SetFaction Postfix：殖民者阵营变化（含奴隶转化）时标记装备分配脏标
    /// 6) Pawn.Kill Postfix：殖民者死亡时标记装备分配脏标
    /// 全部采用 Postfix 零侵入方式，不拦截原方法。
    ///
    /// 注：原 Pawn.SpawnSetup Postfix 注入 CompGearManager 的逻辑已移除——
    /// 该机制修改所有人类like Pawn ThingDef.comps，与其他装备管理类 MOD 冲突。
    /// 现改用 GameComponent 全局 Tick 驱动 AutoExecutor，零 ThingDef 修改。
    ///
    /// 殖民者栏图标显示方案演进（参考 UsefulMarks 设计）：
    /// - v1：PawnUIOverlay.DrawPawnGUIOverlay Postfix 在世界图层 Pawn 头顶绘制 ★，
    ///   依赖世界坐标到屏幕坐标换算，相机缩放时星标与 Pawn 头顶相对位置飘移
    /// - v2：ColonistBarColonistDrawer.DrawColonist Postfix 在殖民者栏 Rect 右上角绘制 ★，
    ///   殖民者栏是固定 UI 元素，与相机缩放完全解耦
    /// - v3（当前）：单一 ★ 星标改为角色定位图标（前排/远程/手工/贸易），
    ///   玩家一眼可辨殖民者定位，颜色按战斗橙/工作绿/交易粉分组，纹理由代码程序化生成
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

            // ColonistBarColonistDrawer.DrawColonist 补丁：在殖民者栏固定位置为人类 Pawn 绘制角色定位图标
            // RimWorld 1.6 中类型为 RimWorld.ColonistBarColonistDrawer，公开实例方法 DrawColonist(Rect, Pawn, Map, bool, bool)
            // 用 try-catch 降级：类型/方法缺失仅 Log.Warning，图标不显示但不崩溃
            // Priority.Last 避免与其他 MOD 的同方法 patch 顺序冲突
            try
            {
                var drawMethod = AccessTools.Method(typeof(ColonistBarColonistDrawer), nameof(ColonistBarColonistDrawer.DrawColonist));
                if (drawMethod != null)
                {
                    harmony.Patch(drawMethod,
                        postfix: new HarmonyMethod(typeof(ColonistBarDrawer_DrawColonist_Patch),
                            nameof(ColonistBarDrawer_DrawColonist_Patch.Postfix))
                        { priority = Priority.Last });
                }
                else
                {
                    Log.Warning("[AutoEverything] ColonistBarColonistDrawer.DrawColonist 未找到，殖民者栏角色图标降级为无显示");
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[AutoEverything] ColonistBarColonistDrawer 补丁失败: " + ex.Message);
            }

            // 装备分配事件补丁（仅 Apparel 与玩家阵营人类like Pawn 触发 MarkDirty）
            // 设计：事件触发只设脏标，AutoExecutor 周期去抖执行，避免 Tick 检查策略
            try
            {
                harmony.Patch(
                    AccessTools.Method(typeof(Thing), nameof(Thing.SpawnSetup)),
                    postfix: new HarmonyMethod(typeof(Thing_SpawnSetup_Patch), nameof(Thing_SpawnSetup_Patch.Postfix)));
                harmony.Patch(
                    AccessTools.Method(typeof(Thing), nameof(Thing.Destroy)),
                    postfix: new HarmonyMethod(typeof(Thing_Destroy_Patch), nameof(Thing_Destroy_Patch.Postfix)));
                harmony.Patch(
                    AccessTools.Method(typeof(Pawn), nameof(Pawn.SetFaction)),
                    postfix: new HarmonyMethod(typeof(Pawn_SetFaction_Patch), nameof(Pawn_SetFaction_Patch.Postfix)));
                harmony.Patch(
                    AccessTools.Method(typeof(Pawn), nameof(Pawn.Kill)),
                    postfix: new HarmonyMethod(typeof(Pawn_Kill_Patch), nameof(Pawn_Kill_Patch.Postfix)));
            }
            catch (Exception ex)
            {
                Log.Warning("[AutoEverything] 装备分配事件补丁失败: " + ex.Message);
            }

            Log.Message("[AutoEverything] Harmony 补丁已应用 (GameComponent 注册 + ColonistBar 角色图标 + 装备分配事件触发)");
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

        /// <summary>
        /// ColonistBarColonistDrawer.DrawColonist 的 Postfix：在殖民者栏固定位置为人类 Pawn 绘制角色定位图标。
        ///
        /// 设计动机（参考 UsefulMarks MOD）：
        /// - 早期方案在 PawnUIOverlay.DrawPawnGUIOverlay 中于世界图层 Pawn 头顶绘制 ★，
        ///   依赖世界坐标到屏幕坐标换算，相机缩放时星标与 Pawn 头顶的相对位置飘移
        /// - 改为 hook 殖民者栏绘制：殖民者栏是固定 UI 元素，与相机缩放完全解耦
        /// - 进一步演进：从单一星标（S+ 高价值 ★）改为角色定位图标（前排/远程/手工/贸易），
        ///   玩家一眼可辨殖民者定位，便于装备分配与工作安排
        ///
        /// 实现要点：
        /// - Harmony 自动注入参数 rect 与 colonist（与原方法同名同型，无需反射）
        /// - 调用 <see cref="RoleIconDef.GetRoleIcons"/> 收集 Pawn 符合的所有角色定位
        /// - 在 rect 右上角从右往左横向排列图标（最多 4 个）
        /// - 图标纹理由 <see cref="RoleIconTextures"/> 程序化生成，颜色由 <see cref="RoleIconDef.GetColor"/> 染色
        /// - 不修改任何 Pawn 数据，纯前端绘制，安全可逆
        ///
        /// 覆盖范围：
        /// - 殖民者栏中所有可见 Pawn（殖民者/奴隶/食尸鬼/动物宠物/机械族等）
        /// - 通过 PawnSuitabilityChecker.CanManageGear 过滤非人类like（动物/机械族/昆虫/异常实体）
        /// - 不强制 Spawned：殖民者栏包含卧床/运输中的殖民者，仍应标记其角色定位
        /// - 不依赖 S+ 评级判定：角色定位基于特质组合，与 CombatTier 解耦
        ///
        /// 代价：
        /// - 非殖民者栏中的高价值单位（囚犯/敌对/中立/野生）不再有可视星标，
        ///   但 PawnMarker.ScanAndMark 通知消息逻辑仍覆盖所有人类单位，玩家仍能通过消息知晓
        /// </summary>
        public static class ColonistBarDrawer_DrawColonist_Patch
        {
            /// <summary>
            /// 单个图标尺寸（像素）：殖民者栏头像约 48x48，图标 16x16 占右上角约 1/3，醒目不喧宾夺主。
            /// 多个图标横向排列时总宽 = N × IconSize + (N-1) × IconSpacing，最多 4 个 = 70px。
            /// </summary>
            private const float IconSize = 16f;

            /// <summary>图标间距（像素）：横向排列时图标之间的留白</summary>
            private const float IconSpacing = 2f;

            /// <summary>右上角内缩留白（像素）：避免图标紧贴殖民者栏边框</summary>
            private const float Margin = 2f;

            public static void Postfix(Rect rect, Pawn colonist)
            {
                if (!AESettings.enabled || !AESettings.autoMarkPawn) return;
                if (colonist == null) return;
                if (colonist.Dead) return;
                if (!PawnSuitabilityChecker.CanManageGear(colonist)) return;

                try
                {
                    DrawRoleIcons(rect, colonist);
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce("[AutoEverything] 殖民者栏角色图标绘制失败: " + ex.Message,
                        colonist.thingIDNumber ^ 0xA600);
                }
            }

            /// <summary>
            /// 在殖民者栏 Rect 右上角从右往左横向排列角色定位图标。
            ///
            /// 坐标系：
            /// - rect 由 RimWorld 内部计算（已含 UI Scale 缩放），直接用 rect.xMax/yMin 定位右上角
            /// - 第一个图标右上角对齐（内缩 Margin 留白），后续图标向左排列
            /// </summary>
            private static void DrawRoleIcons(Rect rect, Pawn pawn)
            {
                List<RoleIconDef.RoleIconType> icons = RoleIconDef.GetRoleIcons(pawn);
                if (icons.Count == 0) return;

                // 从右往左排列：第一个图标在最右
                float x = rect.xMax - IconSize - Margin;
                float y = rect.yMin + Margin;

                Color prevColor = GUI.color;
                for (int i = 0; i < icons.Count; i++)
                {
                    RoleIconDef.RoleIconType type = icons[i];
                    GUI.color = RoleIconDef.GetColor(type);
                    Texture2D tex = RoleIconTextures.Get(type);
                    Rect iconRect = new Rect(x, y, IconSize, IconSize);
                    GUI.DrawTexture(iconRect, tex);
                    x -= IconSize + IconSpacing;
                }
                GUI.color = prevColor;
            }
        }

        // ===================== 装备分配事件触发补丁 =====================
        // 设计：事件触发仅设置 GearAllocator.IsDirty 脏标，AutoExecutor 周期去抖执行
        // 避免在事件路径执行实际分配（SpawnSetup/Destroy 在游戏内部循环中频繁触发）
        // 仅对 Apparel 与玩家阵营人类like Pawn 触发，避免无关 Thing（动物、植物等）干扰

        /// <summary>
        /// Thing.SpawnSetup Postfix：装备/殖民者生成时标记装备分配脏标。
        /// 触发条件：Thing 是 Apparel，或 Thing 是玩家阵营人类like Pawn。
        /// </summary>
        public static class Thing_SpawnSetup_Patch
        {
            public static void Postfix(Thing __instance)
            {
                if (__instance == null) return;
                if (!AESettings.enabled || !AESettings.autoEquipmentEnabled) return;

                if (__instance is Apparel) { GearAllocator.MarkDirty(); return; }
                if (__instance is Pawn pawn && PawnSuitabilityChecker.CanManageGear(pawn)
                    && pawn.Faction == Faction.OfPlayer)
                {
                    GearAllocator.MarkDirty();
                }
            }
        }

        /// <summary>
        /// Thing.Destroy Postfix：装备/殖民者销毁时标记装备分配脏标。
        /// </summary>
        public static class Thing_Destroy_Patch
        {
            public static void Postfix(Thing __instance)
            {
                if (__instance == null) return;
                if (!AESettings.enabled || !AESettings.autoEquipmentEnabled) return;

                if (__instance is Apparel) { GearAllocator.MarkDirty(); return; }
                if (__instance is Pawn pawn && PawnSuitabilityChecker.CanManageGear(pawn)
                    && pawn.Faction == Faction.OfPlayer)
                {
                    GearAllocator.MarkDirty();
                }
            }
        }

        /// <summary>
        /// Pawn.SetFaction Postfix：阵营变化（含奴隶转化、殖民者招募）时标记装备分配脏标。
        /// </summary>
        public static class Pawn_SetFaction_Patch
        {
            public static void Postfix(Pawn __instance)
            {
                if (__instance == null) return;
                if (!AESettings.enabled || !AESettings.autoEquipmentEnabled) return;
                if (!PawnSuitabilityChecker.CanManageGear(__instance)) return;
                // 任意阵营变化都可能影响分配（加入/离开/奴隶转化）
                GearAllocator.MarkDirty();
            }
        }

        /// <summary>
        /// Pawn.Kill Postfix：殖民者死亡时标记装备分配脏标。
        /// </summary>
        public static class Pawn_Kill_Patch
        {
            public static void Postfix(Pawn __instance)
            {
                if (__instance == null) return;
                if (!AESettings.enabled || !AESettings.autoEquipmentEnabled) return;
                if (!PawnSuitabilityChecker.CanManageGear(__instance)) return;
                GearAllocator.MarkDirty();
            }
        }
    }
}
