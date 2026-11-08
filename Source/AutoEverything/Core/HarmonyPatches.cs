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
    /// 1) TickManager.DoSingleTick Postfix：作为 AutoExecutor 的全局 Tick 入口
    /// 2) ColonistBarColonistDrawer.DrawColonist Postfix：在殖民者栏固定位置为人类 Pawn 绘制角色定位图标（右上角）与心灵纽带爱心标识（左下角）
    /// 3) PawnUIOverlay.DrawPawnGUIOverlay Postfix：在地图上为非殖民者栏的高价值单位（敌方/中立/野生）绘制标记
    /// 全部采用 Postfix 零侵入方式，不拦截原方法。
    ///
    /// 注：原 Pawn.SpawnSetup Postfix 注入 CompGearManager 的逻辑已移除——
    /// 该机制修改所有人类like Pawn ThingDef.comps，与其他装备管理类 MOD 冲突。
    /// 现改用全局 Tick（DoSingleTick Postfix）驱动 AutoExecutor，零 ThingDef 修改。
    ///
    /// 注：原 Game.FinalizeInit Postfix 注册 GameComponent 的方案已移除（2026-08-15）——
    /// GameComponent 实例会被 RimWorld 写入存档（Game.components 深序列化），
    /// 玩家卸载 MOD 后加载旧存档需依赖残留组件类型，存档纯净性差。
    /// 改 hook DoSingleTick 后新存档零组件写入；AutoEverythingGameComponent 类保留
    /// 仅供旧存档反序列化兼容，其 GameComponentTick 与本入口双路调用
    /// AutoExecutor.TryTick——内部 60 tick 门控保证幂等，无双倍执行。
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

            // TickManager.DoSingleTick Postfix：AutoExecutor 的全局 Tick 入口
            // 仅在游戏内 tick 时调用（与 GameComponentTick 等效），Postfix 零拦截
            harmony.Patch(
                AccessTools.Method(typeof(TickManager), nameof(TickManager.DoSingleTick)),
                postfix: new HarmonyMethod(typeof(TickManager_DoSingleTick_Patch), nameof(TickManager_DoSingleTick_Patch.Postfix)));

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

            // ColonistBar.MarkColonistsDirty / CheckRecacheEntries 补丁：殖民者栏按角色分组
            // MarkColonistsDirty Postfix 置脏标记；CheckRecacheEntries Postfix 消费并重编 Entry.group，
            // 使原生分组框绘制（DrawGroupFrame）与组内拖拽按角色生效。详见 ColonistBarRoleGrouping。
            // CheckRecacheEntries 为私有方法，用 AccessTools 按名解析；缺失时降级为原生分组
            try
            {
                var recacheMethod = AccessTools.Method(typeof(ColonistBar), "CheckRecacheEntries");
                if (recacheMethod != null)
                {
                    // Prefix/Finalizer 维护 recache 调用深度：只在最外层应用分组，
                    // 避免嵌套 recache 期间重编 group 与原生的 groupsCount 失配导致数组越界
                    harmony.Patch(recacheMethod,
                        prefix: new HarmonyMethod(typeof(ColonistBarRoleGrouping),
                            nameof(ColonistBarRoleGrouping.OnRecachePrefix)),
                        postfix: new HarmonyMethod(typeof(ColonistBarRoleGrouping), nameof(ColonistBarRoleGrouping.TryApply))
                        { priority = Priority.Last },
                        finalizer: new HarmonyMethod(typeof(ColonistBarRoleGrouping),
                            nameof(ColonistBarRoleGrouping.OnRecacheFinalizer)));
                    harmony.Patch(AccessTools.Method(typeof(ColonistBar), nameof(ColonistBar.MarkColonistsDirty)),
                        postfix: new HarmonyMethod(typeof(ColonistBarRoleGrouping), nameof(ColonistBarRoleGrouping.NotifyDirty)));

                    // DrawGroupFrame Postfix：分组框叠加"图标 + 文字"标签、彩色边界与右键拖拽换序
                    // ColonistBarOnGUI Postfix：右键在分组框外释放时清理拖拽状态
                    var groupFrameMethod = AccessTools.Method(typeof(ColonistBarColonistDrawer), "DrawGroupFrame");
                    if (groupFrameMethod != null)
                    {
                        harmony.Patch(groupFrameMethod,
                            postfix: new HarmonyMethod(typeof(ColonistBarRoleGrouping),
                                nameof(ColonistBarRoleGrouping.DrawGroupDecorations))
                            { priority = Priority.Last });
                    }

                    var barGuiMethod = AccessTools.Method(typeof(ColonistBar), "ColonistBarOnGUI");
                    if (barGuiMethod != null)
                    {
                        harmony.Patch(barGuiMethod,
                            postfix: new HarmonyMethod(typeof(ColonistBarRoleGrouping),
                                nameof(ColonistBarRoleGrouping.EndGroupDragIfReleased)));
                    }
                }
                else
                {
                    Log.Warning("[AutoEverything] ColonistBar.CheckRecacheEntries 未找到，殖民者栏角色分组降级为无显示");
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[AutoEverything] ColonistBar 分组补丁失败: " + ex.Message);
            }

            // PawnNameColorUtility.PawnNameColorOf 补丁：按评级覆盖殖民者名字颜色
            // SSS/SS/S=亮紫 / A/B=白 / C/D=灰；X 档保持原生颜色
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

            Log.Message("[AutoEverything] Harmony 补丁已应用 (DoSingleTick 入口 + ColonistBar 角色图标 + 殖民者栏角色分组 + 地图高价值标记 + 名字评级着色)");
        }

        /// <summary>
        /// TickManager.DoSingleTick 的 Postfix：AutoExecutor 的全局 Tick 入口。
        ///
        /// 设计动机（2026-08-15 由 GameComponent 注册方案演进）：
        /// - 原方案 patch Game.FinalizeInit 注入 AutoEverythingGameComponent，
        ///   组件实例随存档持久化（Game.components 深序列化）——玩家卸载 MOD 后
        ///   加载旧存档需依赖残留组件类型，存档纯净性差
        /// - 改 hook DoSingleTick：与 GameComponentTick 等效（仅游戏内每 tick 调用），
        ///   但不向存档写入任何 MOD 组件，新存档零污染
        /// - 幂等保障：旧存档中的 AutoEverythingGameComponent.GameComponentTick
        ///   与本入口双路调用 AutoExecutor.TryTick，TryTick 入口的 60 tick 门控
        ///   使同 tick 第二次调用直接 return，无双倍执行
        /// </summary>
        public static class TickManager_DoSingleTick_Patch
        {
            public static void Postfix()
            {
                try
                {
                    AutoExecutor.TryTick();
                }
                catch (Exception ex)
                {
                    // 异常隔离：本 MOD Tick 失败不影响游戏原生 tick 逻辑
                    Log.ErrorOnce("[AutoEverything] DoSingleTick 入口失败: " + ex.Message, 0xA710);
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
            /// 单个图标基准尺寸（未缩放，像素）：原版殖民者栏文化职位图标上限为
            /// BaseIconMaxSize=20 且按头像可用宽度自适应缩小（ColonistBarColonistDrawer.DrawIcons）。
            /// 这里取更小的 16 作为上限，图标越多继续按可用宽度缩小，总宽不超出头像宽度。
            /// </summary>
            private const float BaseIconSize = 16f;

            /// <summary>图标间距（未缩放，像素）：横向排列时图标之间的留白</summary>
            private const float BaseIconSpacing = 2f;

            /// <summary>右上角内缩留白（未缩放，像素）：避免图标紧贴殖民者栏边框</summary>
            private const float BaseMargin = 2f;

            /// <summary>
            /// 原版底部图标带高度（未缩放，像素）：原版 DrawIcons 在头像底部绘制状态/职位图标，
            /// 尺寸上限 20 且与底边留 1px。爱心与伤痕条上移至此高度之上，避免覆盖原版图标。
            /// </summary>
            private const float VanillaIconBand = 21f;

            /// <summary>
            /// 心灵纽带爱心基准尺寸（未缩放，像素）：用户决策 2026-11-08 在上限 16 基础上增大一倍到 32，
            /// 便于在小头像上清晰辨认；仍按头像可用宽度收缩，避免超出头像。
            /// </summary>
            private const float BaseBondIconSize = 32f;

            /// <summary>
            /// 伤痕竖条尺寸（未缩放，像素）：用户反馈原 3×6 太小，增大到 4×14，间距 3；
            /// 3 条总宽 18，远小于头像，不遮挡脸部。
            /// </summary>
            private const float ScarBarWidth = 4f;
            private const float ScarBarHeight = 14f;
            private const float ScarBarSpacing = 3f;

            /// <summary>1×1 白色纹理：伤痕竖条用 GUI.color 染色，避免新增贴图资源</summary>
            private static readonly Texture2D ScarBarTexture = CreateWhiteTexture();

            public static void Postfix(Rect rect, Pawn colonist)
            {
                if (!AESettings.enabled || !AESettings.autoMarkPawn) return;
                if (colonist == null) return;
                if (colonist.Dead) return;
                if (!PawnSuitabilityChecker.CanManageGear(colonist)) return;

                try
                {
                    // 爱心先画：尺寸较大时优先让右上角角色图标保持可见（不遮挡）
                    DrawBondHeart(rect, colonist);
                    DrawRoleIcons(rect, colonist);
                    DrawScarBars(rect, colonist);
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
            ///
            /// 尺寸：与原版 DrawIcons 一致，图标总宽不超过头像宽度，图标越多单个越小；
            /// 上限 16px（略小于原版文化职位图标的 20px），避免头像被大面积遮挡。
            /// </summary>
            private static void DrawRoleIcons(Rect rect, Pawn pawn)
            {
                List<RoleIconDef.RoleIconType> icons = RoleIconDef.GetRoleIcons(pawn);
                if (icons.Count == 0) return;

                // 缩放系数取自头像实际宽度，使图标随 ColonistBar.Scale 同步缩放（与原版一致）
                float scale = rect.width / ColonistBar.BaseSize.x;
                float margin = BaseMargin * scale;
                float spacing = BaseIconSpacing * scale;
                float available = rect.width - margin * 2f - spacing * (icons.Count - 1);
                float iconSize = Mathf.Min(BaseIconSize * scale, available / icons.Count);
                if (iconSize <= 0f) return;

                // 从右往左排列：第一个图标在最右
                float x = rect.xMax - iconSize - margin;
                float y = rect.yMin + margin;

                Color prevColor = GUI.color;
                for (int i = 0; i < icons.Count; i++)
                {
                    RoleIconDef.RoleIconType type = icons[i];
                    Color iconColor = RoleIconDef.GetColor(type);
                    // 跟随殖民者栏条目透明度（换图/拖拽时与原版图标一起淡出）
                    iconColor.a = prevColor.a;
                    GUI.color = iconColor;
                    Texture2D tex = RoleIconTextures.Get(type);
                    Rect iconRect = new Rect(x, y, iconSize, iconSize);
                    GUI.DrawTexture(iconRect, tex);
                    x -= iconSize + spacing;
                }
                GUI.color = prevColor;
            }

            /// <summary>
            /// 在头像左下角绘制爱心标识：标记已建立心灵纽带的伴侣。
            /// 同一对伴侣使用相同的「颜色 + 风格」组合（4 色 × 空心/实心 2 风格），
            /// 因此同色同风格爱心即为伴侣；悬停显示伴侣名字。
            /// 位置在原版底部状态图标带之上，避免遮挡；尺寸为原上限 16 的一倍增大（32）。
            /// </summary>
            private static void DrawBondHeart(Rect rect, Pawn pawn)
            {
                Pawn partner = RoleIconDef.GetBondPartner(pawn);
                if (partner == null) return;

                float scale = rect.width / ColonistBar.BaseSize.x;
                float margin = BaseMargin * scale;
                float size = Mathf.Min(BaseBondIconSize * scale, rect.width - margin * 2f);
                if (size <= 0f) return;

                float bottom = rect.yMax - VanillaIconBand * scale;
                Rect heartRect = new Rect(rect.xMin + margin, bottom - size - margin, size, size);

                Color prevColor = GUI.color;
                Color heartColor = RoleIconDef.GetBondColor(pawn, partner);
                heartColor.a = prevColor.a;
                GUI.color = heartColor;
                Texture2D tex = RoleIconDef.UseBondOutline(pawn, partner)
                    ? RoleIconTextures.BondOutline
                    : RoleIconTextures.Bond;
                GUI.DrawTexture(heartRect, tex);
                GUI.color = prevColor;

                // 悬停提示伴侣名字，避免配对组合重复时无法确认
                if (Mouse.IsOver(heartRect))
                    TooltipHandler.TipRegion(heartRect, "AE_BondPartner".Translate(partner.LabelShortCap));
            }

            /// <summary>
            /// 在头像右下角绘制伤痕竖条：1 条 = 1 处永久疤痕/缺失部位，最多 3 条。
            ///
            /// 位置与爱心图标对齐：两者底边都落在原版底部图标带之上的同一条基线
            /// （rect.yMax - VanillaIconBand*scale - margin），视觉上齐平，不会一高一低。
            /// 从右往左排列，与右上角角色图标方向一致；条数越多向左延伸，不影响头像中心。
            /// 颜色琥珀橙（<see cref="ScarMarkDef.BarColor"/>），与深红角色图标区分。
            /// </summary>
            private static void DrawScarBars(Rect rect, Pawn pawn)
            {
                if (!AESettings.showScarBars) return;

                int bars = ScarMarkDef.GetBarCount(pawn);
                if (bars <= 0) return;

                // 缩放系数与原版/角色图标一致，随 ColonistBar.Scale 同步
                float scale = rect.width / ColonistBar.BaseSize.x;
                float barWidth = ScarBarWidth * scale;
                float barHeight = ScarBarHeight * scale;
                float spacing = ScarBarSpacing * scale;
                float margin = BaseMargin * scale;

                // 与 DrawBondHeart 使用同一基线：底边 = 底部图标带之上、再内缩 margin
                float bottom = rect.yMax - VanillaIconBand * scale - margin;
                float y = bottom - barHeight;
                float x = rect.xMax - barWidth - margin;

                Color prevColor = GUI.color;
                Color barColor = ScarMarkDef.BarColor;
                // 跟随殖民者栏条目透明度（换图/拖拽时与原版图标一起淡出）
                barColor.a = prevColor.a;
                GUI.color = barColor;
                for (int i = 0; i < bars; i++)
                {
                    GUI.DrawTexture(new Rect(x, y, barWidth, barHeight), ScarBarTexture);
                    x -= barWidth + spacing;
                }
                GUI.color = prevColor;
            }

            /// <summary>程序化生成 1×1 白色纹理（RGBA32），供 GUIScale 染色绘制纯色块。</summary>
            private static Texture2D CreateWhiteTexture()
            {
                Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                tex.SetPixel(0, 0, Color.white);
                tex.Apply();
                tex.filterMode = FilterMode.Point;
                return tex;
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
                // 扫描范围收窄为"仅玩家阵营"时，地图上不再绘制非玩家单位标记
                if (!AESettings.markIncludeNonPlayer) return;
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
        ///   SSS/SS/S=亮紫 / A/B=白 / C/D=灰
        /// - 自定义评级也按相同规则着色（颜色规则同自动）
        /// - PawnNameColorOf 是 RimWorld 原生名字颜色查询入口，被 GenMapUI.DrawPawnLabel 调用
        ///   Postfix 修改 __result 会同时影响殖民者栏与地图名字标签，视觉一致
        ///
        /// 覆盖范围（保守策略，保留原生身份颜色）：
        /// - 仅对玩家阵营人类 like 殖民者生效（PawnSuitabilityChecker.CanManageGear）
        /// - 排除囚犯/奴隶/精神状态：这些状态有原生身份颜色，不应被评级颜色覆盖
        /// - X 档（无法从事暴力活动）保持原生颜色，不参与评级着色
        ///
        /// 颜色与评级对应（S+ 统一紫色）：
        /// - SSS：亮紫 (0.75, 0.30, 1.00)
        /// - SS：亮紫 (0.75, 0.30, 1.00)
        /// - S：亮紫 (0.75, 0.30, 1.00)
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
            // 颜色选择依据：S+ 统一紫色以突出高价值
            private static readonly Color TierColorSPlus = new Color(0.75f, 0.30f, 1.00f);  // 亮紫
            private static readonly Color TierColorAB = new Color(1.00f, 1.00f, 1.00f);     // 白色
            private static readonly Color TierColorCD = new Color(0.55f, 0.55f, 0.55f);     // 灰色

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
                    case CombatTier.SSS:
                    case CombatTier.SS:
                    case CombatTier.S: return TierColorSPlus;
                    case CombatTier.A:
                    case CombatTier.B: return TierColorAB;
                    case CombatTier.C:
                    case CombatTier.D: return TierColorCD;
                    default: return new Color(0f, 0f, 0f, 0f);  // X 档不覆盖
                }
            }
        }
    }
}
