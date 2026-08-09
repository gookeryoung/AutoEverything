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

            // PawnNameColorUtility.PawnNameColorOf 补丁：按评级覆盖殖民者名字颜色
            // SSS=金黄 / SS=橙 / S=黄 / A/B=白 / C/D=灰；X 档保持原生颜色
            // 仅对玩家阵营人类 like 殖民者生效（非囚犯/非奴隶/非精神状态），保留原生身份颜色
            try
            {
                var colorMethod = AccessTools.Method(typeof(PawnNameColorUtility), nameof(PawnNameColorUtility.PawnNameColorOf));
                if (colorMethod != null)
                {
                    harmony.Patch(colorMethod,
                        postfix: new HarmonyMethod(typeof(PawnNameColorUtility_PawnNameColorOf_Patch),
                            nameof(PawnNameColorUtility_PawnNameColorOf_Patch.Postfix))
                        { priority = Priority.Last });
                }
                else
                {
                    Log.Warning("[AutoEverything] PawnNameColorUtility.PawnNameColorOf 未找到，名字评级着色降级为无显示");
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[AutoEverything] PawnNameColorUtility 补丁失败: " + ex.Message);
            }

            // GenMapUI.DrawPawnLabel 补丁（重载二，bgRect 参数版本）：S+ 评级殖民者名字加粗
            // Prefix 保存原 fontStyle 并设 Bold；Postfix 恢复，避免影响后续绘制
            // 重载一（pos 参数版本）内部转调重载二，只需 Patch 重载二即可覆盖所有调用路径
            try
            {
                var drawLabelMethod = AccessTools.Method(typeof(GenMapUI), nameof(GenMapUI.DrawPawnLabel),
                    new[] { typeof(Pawn), typeof(Rect), typeof(float), typeof(float),
                            typeof(Dictionary<string, string>), typeof(GameFont), typeof(bool), typeof(bool) });
                if (drawLabelMethod != null)
                {
                    harmony.Patch(drawLabelMethod,
                        prefix: new HarmonyMethod(typeof(GenMapUI_DrawPawnLabel_Patch),
                            nameof(GenMapUI_DrawPawnLabel_Patch.Prefix)),
                        postfix: new HarmonyMethod(typeof(GenMapUI_DrawPawnLabel_Patch),
                            nameof(GenMapUI_DrawPawnLabel_Patch.Postfix)));
                }
                else
                {
                    Log.Warning("[AutoEverything] GenMapUI.DrawPawnLabel 未找到，名字加粗降级为无显示");
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[AutoEverything] GenMapUI.DrawPawnLabel 补丁失败: " + ex.Message);
            }

            Log.Message("[AutoEverything] Harmony 补丁已应用 (GameComponent 注册 + ColonistBar 角色图标 + 地图高价值标记 + 名字评级着色 + S+ 名字加粗)");
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
        /// 坐标系（与 RimWorld 1.6 原生 GenMapUI.LabelDrawPosFor 完全一致）：
        /// - 偏移在 Z 轴（地面平面），不是 Y 轴（世界高度）——
        ///   Y 轴偏移会因相机俯视角与缩放导致屏幕投影位置随缩放飘移；
        ///   Z 轴偏移在地面平面上，与相机缩放完全解耦，标记精准跟随 Pawn
        /// - Find.Camera.WorldToScreenPoint(drawPos) / Prefs.UIScale → GUI 局部坐标
        /// - result.y = UI.screenHeight - result.y → GUI 坐标系（Y 翻转）
        /// - 头顶锚点与原生问号图标（OverlayDrawer.RenderQuestionMarkOverlay）一致：
        ///   drawPos.z + (def.size.z - 0.45f)，人类 size.z=1 即 z+0.55 ≈ 头顶；
        ///   注意不能用名字标签锚点（z-0.6）——名字标签在 Pawn 脚下方向，
        ///   以其为基准会让标记落在 Pawn 下半身，视觉上"偏离"Pawn
        ///
        /// 颜色（与 PawnMarker 类别语义一致）：
        /// - Enemy=红色, Neutral=青色, WildHuman=白色
        /// </summary>
        public static class PawnUIOverlay_DrawPawnGUIOverlay_Patch
        {
            // 标记尺寸（像素）：地图上的标记圆形直径，比殖民者栏图标稍大便于远距识别
            private const float MarkerSize = 20f;

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
            /// 坐标变换：复用 RimWorld 1.6 原生 GenMapUI.LabelDrawPosFor 投影。
            /// 锚点取头顶而非名字标签：原生问号图标（OverlayDrawer.RenderQuestionMarkOverlay）
            /// 对 Pawn 的偏移为 drawPos.z + (def.size.z - 0.45f)，人类 size.z=1 即 z+0.55 ≈ 头顶；
            /// 名字标签锚点（z-0.6）在 Pawn 脚下方向，以其为基准标记会落在下半身。
            /// 关键：偏移在 Z 轴（地面平面），不是 Y 轴（世界高度），避免相机缩放时屏幕投影飘移。
            /// </summary>
            private static void DrawMapMarker(Pawn pawn)
            {
                // 头顶锚点与原生问号图标一致：z + (def.size.z - 0.45f)。
                // Z 轴地面平面偏移随相机缩放与 Pawn 精灵同步缩放，缩放过程不飘移。
                Vector2 pos = GenMapUI.LabelDrawPosFor(pawn, pawn.def.size.z - 0.45f);

                // 标记底部在头顶上方 2px（屏幕 y 减小方向），不遮挡头部
                pos.y -= MarkerSize * 0.5f + 2f;

                // 防御性：相机视角外跳过（用 Verse.UI.screenWidth/Height 与原生坐标系一致）
                // 用完全限定名避开本命名空间 AutoEverything.UI 的歧义
                if (pos.x < -MarkerSize || pos.x > Verse.UI.screenWidth + MarkerSize) return;
                if (pos.y < -MarkerSize || pos.y > Verse.UI.screenHeight + MarkerSize) return;

                // 取类别颜色：Enemy=红, Neutral=青, WildHuman=白（PawnMarker.GetMarkerCategory 判定）
                PawnMarker.MarkerCategory category = PawnMarker.GetMarkerCategory(pawn);
                Color markerColor = GetMarkerColor(category);

                // 圆形背景：以 pos 为中心，MarkerSize×MarkerSize 的 Rect
                Rect markerRect = new Rect(
                    pos.x - MarkerSize * 0.5f,
                    pos.y - MarkerSize * 0.5f,
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

        /// <summary>
        /// PawnNameColorUtility.PawnNameColorOf 的 Postfix：按评级覆盖殖民者名字颜色。
        ///
        /// 设计动机：
        /// - 用户需求：自动评级应当对名字进行颜色区分
        ///   SSS=金黄 / SS=橙 / S=黄 / A/B=白 / C/D=灰
        /// - 自定义评级也按相同规则着色（颜色规则同自动）
        /// - PawnNameColorOf 是 RimWorld 原生名字颜色查询入口，被 GenMapUI.DrawPawnLabel 调用
        ///   Postfix 修改 __result 会同时影响殖民者栏与地图名字标签，视觉一致
        ///
        /// 覆盖范围（保守策略，保留原生身份颜色）：
        /// - 仅对玩家阵营人类 like 殖民者生效（PawnSuitabilityChecker.CanManageGear）
        /// - 排除囚犯/奴隶/精神状态：这些状态有原生身份颜色，不应被评级颜色覆盖
        /// - X 档（无法从事暴力活动）保持原生颜色，不参与评级着色
        ///
        /// 颜色与评级对应（与 ITab_GearManager 评级徽章颜色系一致）：
        /// - SSS：金黄 (1.0, 0.84, 0.0)
        /// - SS：橙色 (1.0, 0.55, 0.0)
        /// - S：黄色 (1.0, 0.92, 0.23)
        /// - A、B：白色 (1.0, 1.0, 1.0)
        /// - C、D：灰色 (0.55, 0.55, 0.55)
        /// - X：不覆盖（保持原生 ColorColony 浅灰）
        ///
        /// 注：__result 的 alpha 会被 GenMapUI.DrawPawnLabel 内部 color.a = alpha 覆盖，
        ///   所以此处只设 RGB，alpha 留给原生逻辑处理。
        /// </summary>
        public static class PawnNameColorUtility_PawnNameColorOf_Patch
        {
            // 评级颜色常量（RGB，alpha 由原生逻辑处理）
            // 颜色选择依据：与 ITab_GearManager 评级徽章颜色系协调，确保玩家在殖民者栏/地图/检视面板看到一致的颜色语义
            private static readonly Color TierColorSSS = new Color(1.00f, 0.84f, 0.00f);  // 金黄
            private static readonly Color TierColorSS = new Color(1.00f, 0.55f, 0.00f);   // 橙色
            private static readonly Color TierColorS = new Color(1.00f, 0.92f, 0.23f);    // 黄色
            private static readonly Color TierColorAB = new Color(1.00f, 1.00f, 1.00f);   // 白色
            private static readonly Color TierColorCD = new Color(0.55f, 0.55f, 0.55f);   // 灰色

            public static void Postfix(Pawn pawn, ref Color __result)
            {
                if (!AESettings.enabled || !AESettings.autoTierTag) return;
                if (pawn == null) return;
                // 仅对玩家阵营人类 like 殖民者生效
                if (pawn.Faction != Faction.OfPlayer) return;
                if (!PawnSuitabilityChecker.CanManageGear(pawn)) return;
                // 保留原生身份颜色：囚犯/奴隶/精神状态由原生逻辑处理
                if (pawn.IsPrisonerOfColony) return;
                if (pawn.IsSlaveOfColony) return;
                if (pawn.MentalStateDef != null) return;

                try
                {
                    // 取最终评级（含自定义评级覆盖，与高价值标记/排序逻辑一致）
                    CombatTier tier = TierCacheService.GetTier(pawn);
                    Color color = GetTierColor(tier);
                    // X 档返回零值表示不覆盖（保持 __result 原值）
                    if (color.a > 0f)
                    {
                        // 保留原生 alpha（PawnNameColorOf 返回的 alpha 可能非 1，由 DrawPawnLabel 内部再覆盖）
                        color.a = __result.a;
                        __result = color;
                    }
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce("[AutoEverything] 名字评级着色失败: " + ex.Message,
                        pawn.thingIDNumber ^ 0xA800);
                }
            }

            /// <summary>
            /// 按评级返回对应颜色。X 档返回 alpha=0 的零值表示不覆盖（调用方判断 a>0 才覆盖）。
            /// </summary>
            private static Color GetTierColor(CombatTier tier)
            {
                switch (tier)
                {
                    case CombatTier.SSS: return TierColorSSS;
                    case CombatTier.SS: return TierColorSS;
                    case CombatTier.S: return TierColorS;
                    case CombatTier.A:
                    case CombatTier.B: return TierColorAB;
                    case CombatTier.C:
                    case CombatTier.D: return TierColorCD;
                    default: return new Color(0f, 0f, 0f, 0f);  // X 档不覆盖
                }
            }
        }

        /// <summary>
        /// GenMapUI.DrawPawnLabel 的 Prefix/Postfix：S+ 评级殖民者名字加粗。
        ///
        /// 设计动机：
        /// - 用户需求：S 级以上的名字加粗
        /// - RimWorld 原生 Widgets.Label 用 Text.CurFontStyle 绘制文字，fontStyle 默认 Normal
        /// - 在 DrawPawnLabel 执行期间临时修改 fontStyle 为 Bold，绘制完成后恢复
        ///
        /// 实现要点：
        /// - Patch 重载二（bgRect 参数版本，实际绘制名字的方法）
        /// - 重载一（pos 参数版本）内部转调重载二，无需重复 Patch
        /// - Prefix：保存当前 fontStyle，S+ 评级 Pawn 设 Bold
        /// - Postfix：恢复原 fontStyle（try-finally 语义，异常也恢复）
        ///
        /// 加粗范围（与颜色 patch 一致）：
        /// - 仅对玩家阵营人类 like 殖民者生效
        /// - 排除囚犯/奴隶/精神状态
        /// - S/SS/SSS 加粗，A 及以下保持 Normal
        ///
        /// 注：Text.CurFontStyle 返回 GUIStyle 引用（class），修改 .fontStyle 会修改原始 GUIStyle
        ///   因此必须在 Postfix 恢复，否则会影响后续所有文字绘制
        /// </summary>
        public static class GenMapUI_DrawPawnLabel_Patch
        {
            // 保存当前调用栈的原 fontStyle，Postfix 恢复
            // 用实例字段而非 static，避免嵌套调用（重载一→重载二）时状态被覆盖
            // 但 Harmony patch 方法通常是 static，这里用 static 字段配合调用计数也能实现
            // 实际上 GenMapUI.DrawPawnLabel 不会嵌套调用自身，简单的 static 字段即可
            private static FontStyle prevFontStyle;
            private static bool styleSaved;

            public static void Prefix(Pawn pawn)
            {
                styleSaved = false;
                if (!AESettings.enabled || !AESettings.autoTierTag) return;
                if (pawn == null) return;
                if (pawn.Faction != Faction.OfPlayer) return;
                if (!PawnSuitabilityChecker.CanManageGear(pawn)) return;
                if (pawn.IsPrisonerOfColony) return;
                if (pawn.IsSlaveOfColony) return;
                if (pawn.MentalStateDef != null) return;

                try
                {
                    CombatTier tier = TierCacheService.GetTier(pawn);
                    if (tier >= CombatTier.S)
                    {
                        // S+ 评级：保存原 fontStyle 并设 Bold
                        prevFontStyle = Text.CurFontStyle.fontStyle;
                        Text.CurFontStyle.fontStyle = FontStyle.Bold;
                        styleSaved = true;
                    }
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce("[AutoEverything] 名字加粗 Prefix 失败: " + ex.Message,
                        pawn.thingIDNumber ^ 0xA900);
                }
            }

            public static void Postfix(Pawn pawn)
            {
                if (!styleSaved) return;
                try
                {
                    Text.CurFontStyle.fontStyle = prevFontStyle;
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce("[AutoEverything] 名字加粗 Postfix 恢复失败: " + ex.Message,
                        pawn.thingIDNumber ^ 0xA910);
                }
                finally
                {
                    styleSaved = false;
                }
            }
        }
    }
}
