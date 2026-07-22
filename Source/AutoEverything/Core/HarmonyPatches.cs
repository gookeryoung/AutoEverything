using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using AutoEverything.AutoMarkPawn;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.Core
{
    /// <summary>
    /// Auto Everything MOD 的全部 Harmony 补丁集合。
    /// 补丁职责：
    /// 1) Game.FinalizeInit Postfix：注册 AutoEverythingGameComponent（作为 AutoExecutor 的 Tick 入口）
    /// 2) ColonistBarColonistDrawer.DrawColonist Postfix：在殖民者栏固定位置为人类 Pawn 绘制角色定位图标
    /// 3) PawnUIOverlay.DrawPawnGUIOverlay Postfix：在地图上为非殖民者栏的高价值单位（敌方/中立/野生）绘制标记
    /// 全部采用 Postfix 零侵入方式，不拦截原方法。
    ///
    /// 注：原 Pawn.SpawnSetup Postfix 注入 CompGearManager 的逻辑已移除——
    /// 该机制修改所有人类like Pawn ThingDef.comps，与其他装备管理类 MOD 冲突。
    /// 现改用 GameComponent 全局 Tick 驱动 AutoExecutor，零 ThingDef 修改。
    ///
    /// 注：原 AutoEquipment 模块（自动装备分配）已整体移除——玩家反馈换装效果不理想，
    /// 改用 RimWorld 原生换装（玩家手动管理装备）。相关 Harmony 事件补丁
    /// （Thing.SpawnSetup/Destroy/Pawn.SetFaction/Kill）同步移除。
    ///
    /// 殖民者栏图标显示方案演进（参考 UsefulMarks 设计）：
    /// - v1：PawnUIOverlay.DrawPawnGUIOverlay Postfix 在世界图层 Pawn 头顶绘制 ★，
    ///   依赖世界坐标到屏幕坐标换算，相机缩放时星标与 Pawn 头顶相对位置飘移
    /// - v2：ColonistBarColonistDrawer.DrawColonist Postfix 在殖民者栏 Rect 右上角绘制 ★，
    ///   殖民者栏是固定 UI 元素，与相机缩放完全解耦
    /// - v3：单一 ★ 星标改为角色定位图标（前排/远程/手工/贸易），
    ///   玩家一眼可辨殖民者定位，颜色按战斗橙/工作绿/交易粉分组，纹理由代码程序化生成
    /// - v4（当前）：殖民者栏图标继续由 ColonistBar patch 负责；
    ///   新增 PawnUIOverlay patch 在地图上为非殖民者栏的高价值单位（敌方/中立/野生）绘制
    ///   圆形标记 + 档位字母（S/SS/SSS），让玩家在地图上也能一眼识别高价值目标
    /// </summary>
    [StaticConstructorOnStartup]
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

            // PawnUIOverlay.DrawPawnGUIOverlay 补丁：在地图上为非殖民者栏的高价值单位（敌方/中立/野生）绘制标记
            // PawnUIOverlay 类位于 Verse 命名空间（非 RimWorld），公开实例方法 DrawPawnGUIOverlay()
            // 用 typeof 编译期解析类型，避免字符串拼写错误（之前误写 "RimWorld.PawnUIOverlay" 导致 patch 静默失败）
            // 用 try-catch 降级：方法缺失仅 Log.Warning，地图标记不显示但不崩溃
            // 注：通过 ___pawn 参数注入访问 PawnUIOverlay.pawn 实例字段
            try
            {
                var overlayMethod = AccessTools.Method(typeof(PawnUIOverlay), nameof(PawnUIOverlay.DrawPawnGUIOverlay));
                if (overlayMethod != null)
                {
                    harmony.Patch(overlayMethod,
                        postfix: new HarmonyMethod(typeof(PawnUIOverlay_DrawPawnGUIOverlay_Patch),
                            nameof(PawnUIOverlay_DrawPawnGUIOverlay_Patch.Postfix))
                        { priority = Priority.Last });
                }
                else
                {
                    Log.Warning("[AutoEverything] PawnUIOverlay.DrawPawnGUIOverlay 未找到，地图高价值标记降级为无显示");
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[AutoEverything] PawnUIOverlay 补丁失败: " + ex.Message);
            }

            Log.Message("[AutoEverything] Harmony 补丁已应用 (GameComponent 注册 + ColonistBar 角色图标 + 地图高价值标记)");
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

        /// <summary>
        /// PawnUIOverlay.DrawPawnGUIOverlay 的 Postfix：在地图上为非殖民者栏的高价值单位（敌方/中立/野生）绘制标记。
        ///
        /// 设计动机：
        /// - 殖民者栏 patch 只覆盖殖民者/奴隶/囚犯等玩家阵营单位，敌方/中立/野生高价值单位在地图上没有任何可视标记
        /// - 玩家反馈"标记高价值殖民者，没有标记到敌对方，只看到日志提示"——本 patch 解决此问题
        /// - PawnUIOverlay.DrawPawnGUIOverlay 是 RimWorld 原生绘制血条/状态 icon 的入口，
        ///   Postfix 此时 GUI.matrix 与坐标变换已完成，可直接用屏幕坐标绘制
        ///
        /// 实现要点：
        /// - 通过 ___pawn 参数注入访问 PawnUIOverlay.pawn 实例字段
        /// - 跳过殖民者栏中的单位（pawn.Faction == Faction.OfPlayer 或 IsPrisonerOfColony），
        ///   避免与殖民者栏角色图标重复
        /// - 仅对 PawnMarker.IsHighValue(pawn) 为 true 的单位绘制
        /// - 标记样式：圆形背景（按 MarkerCategory 染色）+ 档位字母（S/SS/SSS）
        ///
        /// 坐标系：
        /// - pawn.DrawPos + 头顶偏移（Humanlike 1.6f）→ 世界坐标
        /// - Find.Camera.WorldToScreenPoint(worldPos) / Prefs.UIScale → 屏幕像素坐标
        /// - screenPos.y = Screen.height - screenPos.y → GUI 坐标系（Y 翻转）
        ///
        /// 颜色（与 PawnMarker 类别语义一致）：
        /// - Enemy=红色, Neutral=青色, WildHuman=白色
        /// </summary>
        public static class PawnUIOverlay_DrawPawnGUIOverlay_Patch
        {
            // 标记尺寸（像素）：地图上的标记圆形直径，比殖民者栏图标稍大便于远距识别
            private const float MarkerSize = 20f;

            // 头顶偏移：人类 Pawn 头顶以上一点（与 RimWorld 原生 icon 绘制位置一致）
            private const float HeadOffsetY = 1.6f;

            // 圆形纹理：白色圆 + 透明背景，运行时由 GUI.color 染色
            // 静态字段初始化器中只调用 Texture2D 构造，不调用 ContentFinder/DefDatabase，符合规则
            private static readonly Texture2D CircleTexture = CreateCircleTexture(32);

            public static void Postfix(Pawn ___pawn)
            {
                if (!AESettings.enabled || !AESettings.autoMarkPawn) return;
                Pawn pawn = ___pawn;
                if (pawn == null || pawn.Dead || !pawn.Spawned) return;
                if (!PawnSuitabilityChecker.CanManageGear(pawn)) return;

                // 跳过殖民者栏中的单位（殖民者/奴隶/囚犯），避免与殖民者栏角色图标重复
                // 殖民者与奴隶的 Faction == Faction.OfPlayer；囚犯通过 IsPrisonerOfColony 判定
                if (pawn.Faction == Faction.OfPlayer) return;
                if (pawn.IsPrisonerOfColony) return;

                // 仅对 S+ 高价值单位绘制（评级缓存 2500 tick TTL）
                if (!PawnMarker.IsHighValue(pawn)) return;

                try
                {
                    DrawMapMarker(pawn);
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce("[AutoEverything] 地图高价值标记绘制失败: " + ex.Message,
                        pawn.thingIDNumber ^ 0xA700);
                }
            }

            /// <summary>
            /// 在 Pawn 头顶绘制圆形标记 + 档位字母。
            /// 坐标变换：WorldToScreenPoint + UIScale 除法 + Y 翻转，与 RimWorld 原生 PawnUIOverlay 一致。
            /// </summary>
            private static void DrawMapMarker(Pawn pawn)
            {
                // 世界坐标：pawn.DrawPos + 头顶偏移
                Vector3 worldPos = pawn.DrawPos;
                worldPos.y += HeadOffsetY;

                // 屏幕坐标变换：与 RimWorld 原生 PawnUIOverlay 内部一致
                Vector2 screenPos = Find.Camera.WorldToScreenPoint(worldPos) / Prefs.UIScale;
                screenPos.y = Screen.height - screenPos.y;

                // 防御性：相机视角外（screenPos 异常）跳过
                if (screenPos.x < -MarkerSize || screenPos.x > Screen.width + MarkerSize) return;
                if (screenPos.y < -MarkerSize || screenPos.y > Screen.height + MarkerSize) return;

                // 取类别颜色：Enemy=红, Neutral=青, WildHuman=白（PawnMarker.GetMarkerCategory 判定）
                PawnMarker.MarkerCategory category = PawnMarker.GetMarkerCategory(pawn);
                Color markerColor = GetMarkerColor(category);

                // 圆形背景：以 screenPos 为中心，MarkerSize×MarkerSize 的 Rect
                Rect markerRect = new Rect(
                    screenPos.x - MarkerSize * 0.5f,
                    screenPos.y - MarkerSize * 0.5f,
                    MarkerSize,
                    MarkerSize);

                Color prevColor = GUI.color;
                GUI.color = markerColor;
                GUI.DrawTexture(markerRect, CircleTexture);

                // 档位字母：S/SS/SSS，居中绘制在圆形上
                CombatTier tier = TierCacheService.GetTier(pawn);
                string tierText = GetTierShortText(tier);
                if (!string.IsNullOrEmpty(tierText))
                {
                    // 黑色字母在彩色圆背景上对比清晰
                    GUI.color = Color.black;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(markerRect, tierText);
                    Text.Anchor = TextAnchor.UpperLeft;
                }
                GUI.color = prevColor;
            }

            /// <summary>
            /// 按标记类别返回颜色：Enemy=红, Neutral=青, WildHuman=白。
            /// 颜色选择依据：红色警示敌对、青色表示中立、白色表示无派系野生。
            /// </summary>
            private static Color GetMarkerColor(PawnMarker.MarkerCategory category)
            {
                switch (category)
                {
                    case PawnMarker.MarkerCategory.Enemy: return new Color(0.85f, 0.2f, 0.2f);
                    case PawnMarker.MarkerCategory.Neutral: return new Color(0.2f, 0.7f, 0.85f);
                    default: return Color.white;
                }
            }

            /// <summary>
            /// 返回档位短文本：S/SS/SSS（其他档位为空，因为非高价值不会进入此路径）。
            /// </summary>
            private static string GetTierShortText(CombatTier tier)
            {
                switch (tier)
                {
                    case CombatTier.S: return "S";
                    case CombatTier.SS: return "SS";
                    case CombatTier.SSS: return "SSS";
                    default: return string.Empty;
                }
            }

            /// <summary>
            /// 程序化生成圆形纹理（白圆 + 透明背景）。
            /// 半径 = size/2 - 1（留 1px 透明边缘），抗锯齿通过边缘像素 alpha 渐变近似。
            /// </summary>
            private static Texture2D CreateCircleTexture(int size)
            {
                Color[] pixels = new Color[size * size];
                float center = size * 0.5f;
                float radius = center - 1f;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = x + 0.5f - center;
                        float dy = y + 0.5f - center;
                        float dist = Mathf.Sqrt(dx * dx + dy * dy);
                        // 距离 < radius-1 完全填充；radius-1 ~ radius 边缘像素 alpha 渐变抗锯齿
                        float alpha;
                        if (dist <= radius - 1f) alpha = 1f;
                        else if (dist <= radius) alpha = radius - dist;
                        else alpha = 0f;
                        pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                    }
                }
                Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                tex.SetPixels(pixels);
                tex.Apply();
                tex.filterMode = FilterMode.Point;
                return tex;
            }
        }
    }
}
