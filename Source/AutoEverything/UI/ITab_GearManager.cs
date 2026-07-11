using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using AutoEverything.Core;
using AutoEverything.RoleEvaluation;
using AutoEverything.AutoEquipment;
using AutoEverything.AutoEquipment.Scoring;
using AutoEverything.Allocation;

namespace AutoEverything.UI
{
    /// <summary>
    /// Pawn 检视面板的自定义标签页：展示角色、情境、装备状态与自定义评级。
    /// 全局装备重配规则（战斗价值公式、护甲偏好等）移至 Dialog_GlobalReallocate 对话框，
    /// 点击"全局装备重配"按钮后弹出对话框，确认后才执行重配。
    ///
    /// UI 设计：使用带颜色底色的徽章（Badge）区分类别——
    ///   角色：蓝/红/绿/橙/灰等按角色类型区分
    ///   情境：红=战斗、橙=狩猎、青=寒冷、橙红=炎热、蓝=工作、白=日常
    ///   评级：钻金=SSS、亮金橙=SS、金=S、紫=A、蓝=B、绿=C、灰=D、红=X
    ///   护甲偏好：暗红=重甲[前排]、黄=自由[后排]、绿=轻甲[工人]
    ///
    /// 食尸鬼处理：食尸鬼也显示此面板，展示评级/战斗价值等信息供玩家参考，
    /// 但不参与自动万物分配。面板会显示"食尸鬼"徽章与"不参与自动万物"提示。
    ///
    /// 纹理加载机制：
    /// [StaticConstructorOnStartup] 仅让 RimWorld 在主线程"额外"触发一次静态构造，
    /// 但 ThingDef.ResolveReferences 在工作线程调用 InspectTabManager.GetSharedInstance
    /// 时会提前触发静态构造，导致 ContentFinder 跨线程访问崩溃。
    /// 因此纹理字段初始化为 null，通过 LongEventHandler.ExecuteWhenFinished 延迟到
    /// 主线程 PlayLoad 完成后填充；在此之前 FillTab 降级为纯色块。
    /// </summary>
    [StaticConstructorOnStartup]
    public class ITab_GearManager : ITab
    {
        // FillTab 缓存：避免每帧重算角色/情境/评级等
        // 60 tick（约 1 秒）刷新一次；选中的 Pawn 变化时立即刷新
        private const int CacheInterval = 60;
        private static int cacheTick = -1;
        private static int cachedPawnId = -1;
        private static Role cachedRole;
        private static GearContext cachedContext;
        private static CombatTier cachedTier;
        private static ArmorPreference cachedArmorPref;
        private static float cachedCombatValue;
        private static float cachedPawnValue;
        // 评级识别码缓存：GetSystemTier 涉及 9 技能查询 + 配偶递归，每帧重算浪费
        private static string cachedPawnLookupName;
        private static CombatTier cachedAutoTier;
        private static bool cachedHasCustom;
        private static CombatTier cachedCustomTier;

        // ScrollView 滚动位置：static 保持位置，切换 Pawn 时不重置
        private static Vector2 scrollPos = Vector2.zero;
        // ScrollView 内容高度：首帧用默认值，之后按上一帧实际绘制高度更新
        // 避免硬编码高度导致新增内容时底部被裁剪
        private static float cachedContentHeight = 590f;

        // ===================== 徽章图片缓存 =====================
        // 不能用静态字段初始化器调用 ContentFinder：ThingDef.ResolveReferences 在工作线程
        // 触发本类型静态构造（InspectTabManager.GetSharedInstance），导致跨线程访问崩溃。
        // [StaticConstructorOnStartup] 无法阻止——它只是让 RimWorld 在主线程"额外"触发一次，
        // 但若类型被更早访问，静态构造仍会在当时线程立即执行。
        // 解决方案：字段初始化为 null，通过 LongEventHandler.ExecuteWhenFinished 延迟到
        // 主线程加载完成后填充；FillTab 调用前若仍为 null 则降级为纯色块。
        private static Dictionary<CombatTier, Texture2D> tierBadgeTextures;
        private static Dictionary<Role, Texture2D> roleBadgeTextures;

        static ITab_GearManager()
        {
            // 延迟到主线程 PlayLoad 完成后加载纹理，避免跨线程 ContentFinder 访问
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                tierBadgeTextures = LoadTierBadgeTextures();
                roleBadgeTextures = LoadRoleBadgeTextures();
            });
        }

        static Dictionary<CombatTier, Texture2D> LoadTierBadgeTextures()
        {
            var dict = new Dictionary<CombatTier, Texture2D>();
            foreach (CombatTier t in System.Enum.GetValues(typeof(CombatTier)))
            {
                string path = "UI/Icons/Tier/Tier_" + t;
                Texture2D tex = ContentFinder<Texture2D>.Get(path, false);
                if (tex != null) dict[t] = tex;
            }
            return dict;
        }

        static Dictionary<Role, Texture2D> LoadRoleBadgeTextures()
        {
            var dict = new Dictionary<Role, Texture2D>();
            foreach (Role r in System.Enum.GetValues(typeof(Role)))
            {
                string path = "UI/Icons/Role/Role_" + r;
                Texture2D tex = ContentFinder<Texture2D>.Get(path, false);
                if (tex != null) dict[r] = tex;
            }
            return dict;
        }

        // ===================== 颜色常量 =====================
        // 集中定义避免散落的 new Color，便于统一调整与主题适配
        private static readonly Color ColorSectionBg = new Color(0.18f, 0.20f, 0.22f, 0.45f);
        private static readonly Color ColorSectionTitle = new Color(0.88f, 0.88f, 0.92f);
        private static readonly Color ColorLabelGray = new Color(0.70f, 0.70f, 0.70f);
        private static readonly Color ColorDescGray = new Color(0.62f, 0.62f, 0.62f);
        private static readonly Color ColorWarningBg = new Color(0.55f, 0.18f, 0.15f, 0.55f);
        private static readonly Color ColorWarningBorder = new Color(0.85f, 0.35f, 0.30f);
        private static readonly Color ColorWarningText = new Color(1.0f, 0.65f, 0.55f);
        private static readonly Color ColorPrimaryBtnBg = new Color(0.22f, 0.45f, 0.65f, 0.85f);

        public ITab_GearManager()
        {
            labelKey = "AE_Tab";

            // 高度容纳徽章区与状态摘要 + 底部 4 勾选框 + 1 全局重配按钮
            // 4 勾选框（评级/工作/装备/星标）+ 1 全局重配按钮
            size = new Vector2(360f, 632f);
        }

        public override bool IsVisible
        {
            get
            {
                // 通过 BasePawn 注入 ITab 时，动物/机械族等也会创建实例
                // 此处过滤：仅玩家阵营人类like 才显示
                // 食尸鬼也允许显示（仅展示评级，不参与装备分配）
                return SelPawn is Pawn pawn
                    && pawn.Faction == Faction.OfPlayer
                    && PawnSuitabilityChecker.CanManageGear(pawn);
            }
        }

        protected override void FillTab()
        {
            if (!(SelPawn is Pawn pawn)) return;

            var comp = pawn.GetComp<CompGearManager>();
            // 食尸鬼可能没有 comp（被排除注入），仍允许显示评级信息
            bool isGhoul = DLCCompat.IsGhoul(pawn);

            // 底部区预留高度：4 勾选框 + 1 按钮行 + 5 间隔
            // 4 勾选框：评级/工作/装备/星标
            // 1 按钮行：全局重配按钮
            float buttonHeight = 30f;
            float buttonGap = 8f;
            float checkboxHeight = 24f;

            Rect rect = new Rect(0f, 0f, size.x, size.y).ContractedBy(10f);

            // 内容区高度 = 总高 - 底部区（4 勾选框 + 1 按钮行 + 5 间隔）
            Rect contentRect = new Rect(rect.x, rect.y, rect.width, rect.height - (checkboxHeight * 4 + buttonHeight * 1 + buttonGap * 5));

            // ===================== 缓存计算展示数据 =====================
            // FillTab 每帧调用，角色/情境/评级计算涉及技能与特质查询，缓存 60 tick 避免重复计算
            int tick = Find.TickManager.TicksGame;
            int pawnId = pawn.thingIDNumber;
            if (tick - cacheTick >= CacheInterval || pawnId != cachedPawnId)
            {
                cacheTick = tick;
                cachedPawnId = pawnId;
                // 食尸鬼也可能有 CombatEvaluator.GetCombatTier/ComputeCombatValue，
                // 用于玩家参考其价值（即使不参与分配）
                cachedRole = comp != null ? comp.CurrentRole : RoleDetector.DetectRole(pawn);
                cachedContext = ContextDetector.GetContext(pawn);
                cachedTier = CombatEvaluator.GetCombatTier(pawn);
                cachedArmorPref = RoleDetector.GetArmorPreference(cachedRole);
                cachedCombatValue = CombatEvaluator.ComputeCombatValue(pawn);
                cachedPawnValue = CombatEvaluator.ComputePawnValueScore(pawn);
                // 评级识别码：GetSystemTier 含配偶豁免（与评级标签一致），
                // 涉及 9 技能查询 + 配偶递归，纳入缓存避免每帧重算
                cachedPawnLookupName = CombatEvaluator.GetPawnLookupName(pawn);
                cachedAutoTier = CombatEvaluator.GetSystemTier(pawn);
                cachedHasCustom = AESettings.TryGetCustomTier(cachedPawnLookupName, out cachedCustomTier);
            }

            Role role = cachedRole;
            GearContext context = cachedContext;
            CombatTier tier = cachedTier;
            ArmorPreference armorPref = cachedArmorPref;
            float combatValue = cachedCombatValue;
            float pawnValue = cachedPawnValue;
            string pawnName = cachedPawnLookupName;
            CombatTier autoTier = cachedAutoTier;
            bool hasCustom = cachedHasCustom;

            // ===================== ScrollView 包裹内容区 =====================
            // 内部 inner rect 从 (0,0) 开始，宽度比 outer 少 16f 预留滚动条
            // 高度用上一帧实际绘制值，首帧用默认 590f，后续自适应内容增减
            Rect innerRect = new Rect(0f, 0f, contentRect.width - 16f, cachedContentHeight);
            Widgets.BeginScrollView(contentRect, ref scrollPos, innerRect);

            Listing_Standard l = new Listing_Standard();
            l.Begin(innerRect);

            // ===================== 顶部标题 =====================
            Text.Font = GameFont.Medium;
            l.Label("AE_TabTitle".Translate());
            Text.Font = GameFont.Small;
            l.Gap(4f);

            // ===================== Section 1: 殖民者状态 =====================
            BeginSection(l, "AE_Section_PawnStatus".Translate());

            // 徽章区：角色 / 情境 / 评级 / 护甲偏好
            // 食尸鬼额外显示"食尸鬼"徽章，替代护甲偏好（食尸鬼不分配护甲）
            DrawBadgeRow(l, role, context, tier, armorPref, isGhoul);

            l.Gap(4f);

            // 数值摘要：战斗价值 / 价值评分
            DrawStatRow(l, combatValue, pawnValue);

            // 食尸鬼警告卡片：用带边框的警告块替代 Tiny 文字，更显眼
            if (isGhoul)
            {
                l.Gap(4f);
                DrawWarningCard(l, "AE_GhoulHint".Translate());
            }

            EndSection(l);

            // ===================== Section 2: 装备摘要 =====================
            BeginSection(l, "AE_Section_Equipment".Translate());
            DrawEquipmentSummary(l, pawn, comp);
            EndSection(l);

            // ===================== Section 3: 操作（锁定/角色覆盖） =====================
            if (!isGhoul && comp != null)
            {
                BeginSection(l, "AE_Section_Controls".Translate());

                // 锁定 checkbox + Tooltip
                Rect lockRect = l.GetRect(24f);
                Widgets.CheckboxLabeled(lockRect, "AE_LockGear".Translate(), ref comp.locked);
                TooltipHandler.TipRegion(lockRect, "AE_TT_LockGear".Translate());

                // 角色覆盖 checkbox + Tooltip
                Rect overrideRect = l.GetRect(24f);
                Widgets.CheckboxLabeled(overrideRect, "AE_OverrideRole".Translate(), ref comp.overrideRole);
                TooltipHandler.TipRegion(overrideRect, "AE_TT_OverrideRole".Translate());

                if (comp.overrideRole)
                {
                    l.Gap(2f);
                    List<FloatMenuOption> options = new List<FloatMenuOption>();
                    foreach (Role r in System.Enum.GetValues(typeof(Role)))
                    {
                        // 闭包捕获：必须用局部变量，避免循环变量全部指向最后一个枚举值
                        Role localRole = r;
                        options.Add(new FloatMenuOption(
                            ("AE_Role_" + r).Translate(),
                            () => comp.manualRole = localRole));
                    }
                    if (l.ButtonText("AE_Role".Translate() + ": " + ("AE_Role_" + comp.manualRole).Translate()))
                    {
                        Find.WindowStack.Add(new FloatMenu(options));
                    }
                }

                EndSection(l);
            }

            // ===================== 自定义评级识别码（食尸鬼也显示，玩家可参考） =====================
            // 描述过长，移到 Tooltip，节省垂直空间
            GUI.color = ColorSectionTitle;
            l.Label("AE_ReallocRules_CustomTier".Translate());
            GUI.color = Color.white;

            // 显示当前 Pawn 的识别码：系统档固定，自定义档写入括号
            // 系统档含配偶豁免（GetSystemTier），与评级标签一致
            // pawnName/autoTier/hasCustom 已在 60 tick 缓存块内计算，避免每帧重算评级

            string tierCode = hasCustom
                ? autoTier + "(" + cachedCustomTier + ")#" + pawnName
                : autoTier + "#" + pawnName;

            Rect tierCodeRect = l.GetRect(22f);
            GUI.color = ColorLabelGray;
            Text.Anchor = TextAnchor.MiddleLeft;
            bool prevWrap = Text.WordWrap;
            Text.WordWrap = false;
            GameFont prevFont = Text.Font;
            string tierCodeLabel = "AE_ReallocRules_CurrentTier".Translate() + ": " + tierCode;
            // 超宽时缩字号避免截断（tierCode 可能很长，如 "当前评级: SSS(SSS)#王五"）
            if (Text.CalcSize(tierCodeLabel).x > tierCodeRect.width)
            {
                Text.Font = GameFont.Tiny;
            }
            Widgets.Label(tierCodeRect, tierCodeLabel);
            Text.Font = prevFont;
            Text.WordWrap = prevWrap;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            TooltipHandler.TipRegion(tierCodeRect, "AE_ReallocRules_CustomTier_Desc".Translate());

            // 两按钮并排：设置自定义档次 / 清除自定义
            Rect tierBtnRect = l.GetRect(28f);
            float tierBtnWidth = (tierBtnRect.width - 8f) * 0.5f;
            if (Widgets.ButtonText(new Rect(tierBtnRect.x, tierBtnRect.y, tierBtnWidth, 28f),
                                   "AE_ReallocRules_SetCustomTier".Translate()))
            {
                // 弹出 FloatMenu 选择档次 SSS/SS/S/A/B/C/D/X
                List<FloatMenuOption> tierOptions = new List<FloatMenuOption>();
                // 倒序展示：SSS 在最上
                for (int t = (int)CombatTier.SSS; t >= (int)CombatTier.X; t--)
                {
                    CombatTier localTier = (CombatTier)t;
                    tierOptions.Add(new FloatMenuOption(
                        localTier + " - " + ("AE_Tier_" + localTier).Translate(),
                        () =>
                        {
                            AESettings.SetCustomTier(pawnName, localTier);
                        }));
                }
                Find.WindowStack.Add(new FloatMenu(tierOptions));
            }
            if (Widgets.ButtonText(new Rect(tierBtnRect.x + tierBtnWidth + 8f, tierBtnRect.y, tierBtnWidth, 28f),
                                   "AE_ReallocRules_ClearCustomTier".Translate()))
            {
                if (hasCustom)
                {
                    AESettings.ClearCustomTier(pawnName);
                }
            }
            l.Gap(4f);

            l.End();
            // 按实际绘制高度更新缓存，下一帧 ScrollView 使用正确高度避免裁剪
            cachedContentHeight = l.CurHeight + 20f;
            Widgets.EndScrollView();

            // ===================== 底部区：3 勾选框 + 1 按钮 =====================
            // 1. 人员自动评级勾选框：勾选立即执行 + 启用周期自动；取消勾选清除所有评级标签恢复原名
            Rect tierCheckRect = new Rect(
                rect.x,
                contentRect.yMax + buttonGap,
                rect.width,
                checkboxHeight);

            bool prevWrap1 = Text.WordWrap;
            Text.WordWrap = false;
            bool prevTierTag = AESettings.autoTierTag;
            Widgets.CheckboxLabeled(tierCheckRect, "AE_AutoTierTag".Translate(), ref AESettings.autoTierTag);
            Text.WordWrap = prevWrap1;
            TooltipHandler.TipRegion(tierCheckRect, "AE_TT_AutoTierTag".Translate());
            // 状态变化检测：勾选时立即执行；取消勾选时清除评级标签
            if (AESettings.autoTierTag != prevTierTag)
            {
                if (AESettings.autoTierTag)
                {
                    AutoExecutor.TriggerTierNow();
                }
                else
                {
                    int cleared = AESettings.ClearTierTagsFromAllPawns();
                    Messages.Message(
                        "AE_TierTag_ClearResult".Translate(cleared),
                        MessageTypeDefOf.TaskCompletion);
                }
            }

            // 2. 工作自动配置勾选框：勾选立即执行 + 启用周期自动；取消勾选仅停止自动（保留当前分配）
            Rect workCheckRect = new Rect(
                rect.x,
                tierCheckRect.yMax + buttonGap,
                rect.width,
                checkboxHeight);

            bool prevWrap2 = Text.WordWrap;
            Text.WordWrap = false;
            bool prevWork = AESettings.autoWorkEnabled;
            Widgets.CheckboxLabeled(workCheckRect, "AE_AutoWorkConfig".Translate(), ref AESettings.autoWorkEnabled);
            Text.WordWrap = prevWrap2;
            TooltipHandler.TipRegion(workCheckRect, "AE_TT_AutoWorkConfig".Translate());
            // 状态变化检测：勾选时立即执行；取消勾选仅停止自动（无副作用）
            if (AESettings.autoWorkEnabled && AESettings.autoWorkEnabled != prevWork)
            {
                AutoExecutor.TriggerWorkNow();
            }

            // 3. 装备自动重配勾选框：勾选立即执行 + 启用周期自动；取消勾选仅停止自动（保留当前装备）
            Rect gearCheckRect = new Rect(
                rect.x,
                workCheckRect.yMax + buttonGap,
                rect.width,
                checkboxHeight);

            bool prevWrap3 = Text.WordWrap;
            Text.WordWrap = false;
            bool prevGear = AESettings.autoGearReallocate;
            Widgets.CheckboxLabeled(gearCheckRect, "AE_AutoGearReallocate".Translate(), ref AESettings.autoGearReallocate);
            Text.WordWrap = prevWrap3;
            TooltipHandler.TipRegion(gearCheckRect, "AE_TT_AutoGearReallocate".Translate());
            // 状态变化检测：勾选时立即执行；取消勾选仅停止自动（无副作用）
            if (AESettings.autoGearReallocate && AESettings.autoGearReallocate != prevGear)
            {
                AutoExecutor.TriggerGearNow();
            }

            // 4. 高价值非殖民者星标勾选框：勾选时统计+消息提示；取消勾选时头顶图标由 Harmony 补丁实时停止绘制
            Rect markCheckRect = new Rect(
                rect.x,
                gearCheckRect.yMax + buttonGap,
                rect.width,
                checkboxHeight);

            bool prevWrap4 = Text.WordWrap;
            Text.WordWrap = false;
            bool prevMark = AESettings.autoMarkPawn;
            Widgets.CheckboxLabeled(markCheckRect, "AE_AutoMarkPawn".Translate(), ref AESettings.autoMarkPawn);
            Text.WordWrap = prevWrap4;
            TooltipHandler.TipRegion(markCheckRect, "AE_TT_AutoMarkPawn".Translate());
            // 状态变化：勾选时统计当前高价值非殖民者并弹消息；取消勾选时头顶图标自动消失（补丁实时检查开关）
            if (AESettings.autoMarkPawn != prevMark && AESettings.autoMarkPawn)
            {
                AutoExecutor.TriggerMarkNow();
            }

            // 5. 全局装备重配按钮（保留原逻辑，打开 Dialog_GlobalReallocate）
            // 食尸鬼面板也显示此按钮（统一入口），但 GlobalAllocator 内部会跳过食尸鬼
            Rect buttonRect = new Rect(
                rect.x,
                markCheckRect.yMax + buttonGap,
                rect.width,
                buttonHeight);

            // 主操作按钮：先绘制底色，再叠加 ButtonText
            // ButtonText 默认会用 GUI.backgroundColor，这里临时改色突出主操作
            Color prevBtnBg = GUI.backgroundColor;
            GUI.backgroundColor = ColorPrimaryBtnBg;
            if (Widgets.ButtonText(buttonRect, "AE_GlobalReallocate".Translate()))
            {
                Find.WindowStack.Add(new Dialog_GlobalReallocate());
            }
            GUI.backgroundColor = prevBtnBg;
            TooltipHandler.TipRegion(buttonRect, "AE_TT_GlobalReallocate".Translate());
        }

        // ===================== Section 卡片绘制 =====================

        /// <summary>
        /// 开始一个带标题与浅色背景的 Section 卡片。
        /// 配合 EndSection 使用：BeginSection 绘制标题与背景起始，EndSection 绘制底部分隔。
        /// 实现方式：在 Listing_Standard 流中插入一个标题行 + 留出内边距。
        /// 注：参数 title 应为已翻译字符串，方法内不再重复 Translate。
        /// </summary>
        private void BeginSection(Listing_Standard l, string title)
        {
            l.Gap(6f);
            // Section 标题行：浅色文字 + 半透明背景
            Rect titleRect = l.GetRect(22f);
            Widgets.DrawBoxSolid(titleRect, ColorSectionBg);
            GUI.color = ColorSectionTitle;
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Small;
            // 左侧缩进 6f 让标题不紧贴边框
            bool prevWrap = Text.WordWrap;
            Text.WordWrap = false;
            Widgets.Label(new Rect(titleRect.x + 6f, titleRect.y, titleRect.width - 6f, titleRect.height),
                "• " + title);
            Text.WordWrap = prevWrap;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            l.Gap(2f);
        }

        /// <summary>
        /// 结束 Section 卡片：绘制底部分隔与底部内边距。
        /// </summary>
        private void EndSection(Listing_Standard l)
        {
            l.GapLine();
        }

        /// <summary>
        /// 绘制警告卡片：带边框与警告色背景，用于食尸鬼等需要醒目提示的场景。
        /// 高度自适应：根据文本长度用 Text.CalcHeight 计算实际需要的高度，
        /// 避免长文本被截断显示不全。
        /// </summary>
        private void DrawWarningCard(Listing_Standard l, string text)
        {
            // 先用 Tiny 字体计算文本实际需要的高度
            // 内缩进 6f 两侧 = 12f，所以计算宽度 = 列宽 - 12f
            Text.Font = GameFont.Tiny;
            float textWidth = l.ColumnWidth - 12f;
            float textHeight = Text.CalcHeight(text, textWidth);
            // 卡片高度 = 文本高度 + 上下内边距 12f，最小 32f
            float cardHeight = System.Math.Max(textHeight + 12f, 32f);
            Text.Font = GameFont.Small;

            Rect rect = l.GetRect(cardHeight);

            // 警告色背景
            Widgets.DrawBoxSolid(rect, ColorWarningBg);
            // 警告色边框
            GUI.color = ColorWarningBorder;
            Widgets.DrawBox(rect, 1);
            GUI.color = Color.white;

            // 警告文字：Tiny 字体，左对齐，自动换行
            Color prev = GUI.color;
            GUI.color = ColorWarningText;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            // 文字内缩进 6f 避免紧贴边框
            Widgets.Label(rect.ContractedBy(6f), text);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            GUI.color = prev;
        }

        // ===================== 徽章绘制工具 =====================

        /// <summary>
        /// 绘制徽章行：角色 / 情境 / 评级 / 护甲偏好（或食尸鬼徽章）。
        /// 徽章自适应宽度占满整行，每个徽章等宽分配剩余空间。
        /// 每个徽章挂载 Tooltip，鼠标悬停显示判定规则。
        /// </summary>
        private void DrawBadgeRow(Listing_Standard l, Role role, GearContext context,
            CombatTier tier, ArmorPreference armorPref, bool isGhoul)
        {
            // 徽章行高度 26f
            Rect badgeRow = l.GetRect(26f);
            float y = badgeRow.y;
            float h = 24f;
            float gap = 6f;

            // 食尸鬼：4 个徽章 = 角色 + 情境 + 评级 + 食尸鬼徽章
            // 普通殖民者：4 个徽章 = 角色 + 情境 + 评级 + 护甲偏好
            int badgeCount = 4;
            float totalGap = gap * (badgeCount - 1);
            float badgeWidth = (badgeRow.width - totalGap) / badgeCount;

            float x = badgeRow.x;

            // 1. 角色徽章 + Tooltip（判定规则）
            // 优先使用图片徽章（UI/Icons/Role/Role_X），无图或纹理未加载时回退纯色块
            Rect roleRect = new Rect(x, y, badgeWidth, h);
            if (roleBadgeTextures != null && roleBadgeTextures.TryGetValue(role, out Texture2D roleTex))
            {
                DrawRoleBadgeWithIcon(roleRect, roleTex, GetRoleColor(role), ("AE_Role_" + role).Translate());
            }
            else
            {
                DrawBadge(roleRect, ("AE_Role_" + role).Translate(), GetRoleColor(role));
            }
            TooltipHandler.TipRegion(roleRect, ("AE_TT_Role_" + role).Translate());
            x += badgeWidth + gap;

            // 2. 情境徽章 + Tooltip（触发条件）
            // 食尸鬼强制显示"闲置"：其 CurJob 可能是等待类，ContextDetector 已返回 Normal，
            // 但 Normal 翻译"日常"对食尸鬼有误导，"闲置"更准确
            // 食尸鬼 Tooltip 同步使用闲置说明，避免徽章与提示不一致
            Rect ctxRect = new Rect(x, y, badgeWidth, h);
            string ctxText = isGhoul ? "AE_Context_Idle".Translate() : ("AE_Context_" + context).Translate();
            DrawBadge(ctxRect, ctxText, GetContextColor(context));
            string ctxTip = isGhoul ? "AE_TT_Context_Idle".Translate() : ("AE_TT_Context_" + context).Translate();
            TooltipHandler.TipRegion(ctxRect, ctxTip);
            x += badgeWidth + gap;

            // 3. 评级徽章 + Tooltip（计算来源）
            // 优先使用图片徽章（Text UI/Icons/Tier/Tier_X），无图或纹理未加载时回退纯色块
            Rect tierRect = new Rect(x, y, badgeWidth, h);
            if (tierBadgeTextures != null && tierBadgeTextures.TryGetValue(tier, out Texture2D tierTex))
            {
                DrawTierBadgeWithIcon(tierRect, tierTex, GetTierColor(tier));
            }
            else
            {
                DrawBadge(tierRect, tier.ToString(), GetTierColor(tier));
            }
            TooltipHandler.TipRegion(tierRect, "AE_TT_Tier".Translate());
            x += badgeWidth + gap;

            // 4. 护甲偏好徽章 + Tooltip / 食尸鬼徽章
            if (isGhoul)
            {
                Rect ghoulRect = new Rect(x, y, badgeWidth, h);
                DrawBadge(ghoulRect, "AE_Badge_Ghoul".Translate(), new Color(0.6f, 0.2f, 0.6f));
                TooltipHandler.TipRegion(ghoulRect, "AE_GhoulHint".Translate());
            }
            else
            {
                Rect apRect = new Rect(x, y, badgeWidth, h);
                DrawBadge(apRect, ("AE_ArmorPref_" + armorPref).Translate(), GetArmorPrefColor(armorPref));
                TooltipHandler.TipRegion(apRect, ("AE_TT_ArmorPref_" + armorPref).Translate());
            }
        }

        /// <summary>
        /// 绘制单个徽章：带底色 + 居中文字。
        /// 关闭 WordWrap 防止中文换行导致显示不全（超宽截断优于换行撑乱布局）。
        /// </summary>
        private void DrawBadge(Rect rect, string text, Color bgColor)
        {
            // 保存原颜色，绘制后恢复
            Color prevColor = GUI.color;
            Color prevBg = GUI.backgroundColor;
            bool prevWrap = Text.WordWrap;

            // 半透明底色：让徽章不抢眼但清晰可辨
            Color bg = bgColor;
            bg.a = 0.85f;
            GUI.color = bg;
            // DrawBoxSolid 会用 GUI.color 填充
            Widgets.DrawBoxSolid(rect, bg);

            // 边框：深一点的颜色
            GUI.color = Color.white * 0.5f;
            Widgets.DrawBox(rect, 1);

            // 文字：白色或黑色，按底色亮度选择
            float brightness = bgColor.r * 0.299f + bgColor.g * 0.587f + bgColor.b * 0.114f;
            GUI.color = brightness > 0.5f ? Color.black : Color.white;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Tiny;
            Text.WordWrap = false;
            Widgets.Label(rect, text);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = prevWrap;

            GUI.color = prevColor;
            GUI.backgroundColor = prevBg;
        }

        /// <summary>
        /// 绘制带图标的评级徽章：左侧小圆图标 + 右侧评级字母。
        /// 图标来自 Textures/UI/Icons/Tier/Tier_X.png，圆形带底色。
        /// </summary>
        private void DrawTierBadgeWithIcon(Rect rect, Texture2D icon, Color bgColor)
        {
            Color prevColor = GUI.color;
            Color prevBg = GUI.backgroundColor;

            // 半透明底色
            Color bg = bgColor;
            bg.a = 0.85f;
            Widgets.DrawBoxSolid(rect, bg);

            // 边框
            GUI.color = Color.white * 0.5f;
            Widgets.DrawBox(rect, 1);

            // 左侧图标：正方形，边长 = 徽章高度 - 4
            float iconSize = rect.height - 4f;
            Rect iconRect = new Rect(rect.x + 3f, rect.y + 2f, iconSize, iconSize);
            GUI.color = Color.white;
            if (icon != null)
            {
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
            }

            // 右侧字母（图标内已含字母，此处留空避免重复，仅保持布局）
            Text.Anchor = TextAnchor.UpperLeft;

            GUI.color = prevColor;
            GUI.backgroundColor = prevBg;
        }

        /// <summary>
        /// 绘制带图标的角色徽章：左侧小圆图标 + 右侧中文角色名。
        /// 与评级徽章不同——角色图标内无文字，需在图标右侧显示角色名（如"格斗者"）。
        /// 关闭 WordWrap：中文角色名（如"和平主义者"5字）超宽时截断而非换行，避免撑乱徽章行。
        /// 图标缩小到 14f 给文字留更多空间。
        /// </summary>
        private void DrawRoleBadgeWithIcon(Rect rect, Texture2D icon, Color bgColor, string label)
        {
            Color prevColor = GUI.color;
            Color prevBg = GUI.backgroundColor;
            TextAnchor prevAnchor = Text.Anchor;
            bool prevWrap = Text.WordWrap;

            // 半透明底色
            Color bg = bgColor;
            bg.a = 0.85f;
            Widgets.DrawBoxSolid(rect, bg);

            // 边框
            GUI.color = Color.white * 0.5f;
            Widgets.DrawBox(rect, 1);

            // 左侧图标：缩小到 14f 给文字留空间（原 height-4=20f 占用过多）
            float iconSize = 14f;
            Rect iconRect = new Rect(rect.x + 3f, rect.y + (rect.height - iconSize) * 0.5f, iconSize, iconSize);
            GUI.color = Color.white;
            if (icon != null)
            {
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
            }

            // 右侧角色名：图标右侧剩余空间，垂直居中，关闭换行
            float labelX = iconRect.xMax + 4f;
            float labelWidth = rect.xMax - labelX - 3f;
            if (labelWidth > 0f)
            {
                Rect labelRect = new Rect(labelX, rect.y, labelWidth, rect.height);
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.Font = GameFont.Tiny;
                Text.WordWrap = false;
                GUI.color = Color.white;
                Widgets.Label(labelRect, label);
                Text.Font = GameFont.Small;
                Text.WordWrap = prevWrap;
            }

            Text.Anchor = prevAnchor;
            GUI.color = prevColor;
            GUI.backgroundColor = prevBg;
        }

        /// <summary>
        /// 绘制数值摘要：战斗价值 + 价值评分，各占一整行。
        /// 设计为垂直堆叠避免半宽截断：中文标签 + 数值在半宽内易被截断。
        /// 行高 24f 留足上下内边距，避免 Tiny 字号文字贴边显示不全。
        /// </summary>
        private void DrawStatRow(Listing_Standard l, float combatValue, float pawnValue)
        {
            // 战斗价值（整行）
            Rect cvRect = l.GetRect(32f);
            DrawStatBadge(cvRect, "AE_Badge_CombatValue".Translate(), combatValue.ToString("F1"),
                new Color(0.2f, 0.4f, 0.6f));
            TooltipHandler.TipRegion(cvRect, "AE_TT_CombatValue".Translate());

            l.Gap(2f);

            // 价值评分（整行）
            Rect pvRect = l.GetRect(32f);
            DrawStatBadge(pvRect, "AE_Badge_PawnValue".Translate(), pawnValue.ToString("F1"),
                new Color(0.3f, 0.3f, 0.5f));
            TooltipHandler.TipRegion(pvRect, "AE_TT_PawnValue".Translate());
        }

        /// <summary>
        /// 绘制带标签+数值的小徽章。
        /// 左对齐与武器标签保持视觉一致。关闭 WordWrap 防止数值换行。
        /// </summary>
        private void DrawStatBadge(Rect rect, string label, string value, Color bgColor)
        {
            Color prev = GUI.color;
            bool prevWrap = Text.WordWrap;

            Color bg = bgColor;
            bg.a = 0.85f;
            Widgets.DrawBoxSolid(rect, bg);

            GUI.color = Color.white * 0.5f;
            Widgets.DrawBox(rect, 1);

            // 文字：标签:数值（左对齐），关闭换行避免长数值截断
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Tiny;
            Text.WordWrap = false;
            Widgets.Label(rect.ContractedBy(6f), label + ": " + value);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            Text.WordWrap = prevWrap;

            GUI.color = prev;
        }

        /// <summary>
        /// 绘制装备摘要：主武器 → 副武器 → 已穿戴衣物（小标签）。
        /// 主/副武器用带半透明底色的整行标签；护甲数量用小标签行附在最后。
        /// </summary>
        private void DrawEquipmentSummary(Listing_Standard l, Pawn pawn, CompGearManager comp)
        {
            // 主武器（整行）
            string primaryWeapon = pawn.equipment?.Primary?.LabelShort ?? "AE_None".Translate();
            DrawGearTag(l, "AE_PrimaryWeapon".Translate(), primaryWeapon, new Color(0.25f, 0.35f, 0.50f));

            // 副武器（整行）
            // C# 7.3 不支持 string 与 TaggedString 之间的条件表达式，先转 string
            string sidearmLabel = (comp != null && comp.sidearm != null)
                ? comp.sidearm.LabelShort
                : "AE_None".Translate().ToString();
            DrawGearTag(l, "AE_Sidearm".Translate(), sidearmLabel, new Color(0.30f, 0.30f, 0.40f));

            // 已穿戴衣物（小标签，紧贴装备摘要末尾）
            int wornCount = pawn.apparel?.WornApparel.Count ?? 0;
            Rect apparelRect = l.GetRect(18f);
            DrawSmallTag(apparelRect, "AE_WornApparel".Translate(), wornCount.ToString(),
                new Color(0.35f, 0.35f, 0.4f));
        }

        /// <summary>
        /// 绘制小标签：高度更矮、字号 Tiny，用于辅助信息（如护甲数量）。
        /// 与主装备标签视觉区分，避免抢占主武器视觉焦点。
        /// 关闭 WordWrap；labelWidth 用 Text.CalcSize 动态计算，确保中文标签完整显示。
        /// </summary>
        private void DrawSmallTag(Rect rect, string label, string value, Color bgColor)
        {
            Color bg = bgColor;
            bg.a = 0.5f;
            Widgets.DrawBoxSolid(rect, bg);

            Color prev = GUI.color;
            bool prevWrap = Text.WordWrap;
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Tiny;
            Text.WordWrap = false;

            // 动态计算标签宽度（+冒号+留白），避免固定 70f 截断中文标签
            string labelText = label + ":";
            Vector2 labelSize = Text.CalcSize(labelText);
            float labelWidth = labelSize.x + 4f;
            float padLeft = 6f;

            GUI.color = ColorLabelGray;
            Widgets.Label(new Rect(rect.x + padLeft, rect.y, labelWidth, rect.height), labelText);
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + padLeft + labelWidth, rect.y, rect.width - padLeft - labelWidth - 4f, rect.height), value);

            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            Text.WordWrap = prevWrap;
            GUI.color = prev;
        }

        /// <summary>
        /// 绘制带半透明底色的装备标签行（通过 Listing_Standard 取行）。
        /// 标签与值都左对齐，避免居中/左对齐混用造成视觉混乱。
        /// 底色让武器名称更显眼，便于玩家快速识别装备。
        /// </summary>
        private void DrawGearTag(Listing_Standard l, string label, string value, Color bgColor)
        {
            Rect rect = l.GetRect(24f);
            DrawGearTagOnRect(rect, label, value, bgColor);
        }

        /// <summary>
        /// 在指定 Rect 绘制带半透明底色的装备标签。
        /// 布局：[标签:] [值]，全部左对齐，底色半透明。
        /// 关闭 WordWrap；labelWidth 用 Text.CalcSize 动态计算，避免固定 60f 截断中文标签。
        /// </summary>
        private void DrawGearTagOnRect(Rect rect, string label, string value, Color bgColor)
        {
            // 半透明底色
            Color bg = bgColor;
            bg.a = 0.55f;
            Widgets.DrawBoxSolid(rect, bg);

            // 左对齐绘制标签+值
            Color prev = GUI.color;
            bool prevWrap = Text.WordWrap;
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Small;
            Text.WordWrap = false;

            // 动态计算标签宽度（原固定 60f 会截断"已穿戴的衣物"等长标签）
            string labelText = label + ":";
            Vector2 labelSize = Text.CalcSize(labelText);
            float labelWidth = labelSize.x + 4f;
            float padLeft = 6f;

            // 标签（灰色）
            GUI.color = ColorLabelGray;
            Widgets.Label(new Rect(rect.x + padLeft, rect.y, labelWidth, rect.height), labelText);

            // 值（白色）
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + padLeft + labelWidth, rect.y, rect.width - padLeft - labelWidth - 6f, rect.height), value);

            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            Text.WordWrap = prevWrap;
            GUI.color = prev;
        }

        // ===================== 颜色定义 =====================

        /// <summary>
        /// 获取角色对应的徽章颜色。
        /// </summary>
        private Color GetRoleColor(Role role)
        {
            switch (role)
            {
                case Role.Shooter: return new Color(0.29f, 0.56f, 0.85f);  // 蓝
                case Role.Brawler: return new Color(0.85f, 0.29f, 0.29f);  // 红
                case Role.Doctor: return new Color(0.29f, 0.85f, 0.48f);  // 绿
                case Role.Hunter: return new Color(0.85f, 0.63f, 0.29f);  // 橙
                case Role.Worker: return new Color(0.6f, 0.6f, 0.6f);     // 灰
                case Role.Pacifist: return new Color(0.7f, 0.7f, 0.7f);     // 浅灰
                case Role.Leader: return new Color(0.85f, 0.77f, 0.29f);  // 金
                default: return new Color(0.8f, 0.8f, 0.8f);     // 白灰
            }
        }

        /// <summary>
        /// 获取情境对应的徽章颜色。
        /// </summary>
        private Color GetContextColor(GearContext context)
        {
            switch (context)
            {
                case GearContext.Combat: return new Color(0.85f, 0.2f, 0.2f);   // 红
                case GearContext.Work: return new Color(0.2f, 0.5f, 0.85f);   // 蓝
                case GearContext.Hunting: return new Color(0.85f, 0.5f, 0.2f);   // 橙
                case GearContext.Cold: return new Color(0.2f, 0.7f, 0.85f);   // 青
                case GearContext.Hot: return new Color(0.85f, 0.4f, 0.2f);   // 橙红
                default: return new Color(0.8f, 0.8f, 0.8f);    // 白灰
            }
        }

        /// <summary>
        /// 获取评级对应的徽章颜色。
        /// SSS=钻金、SS=亮金橙、S=金、A=紫、B=蓝、C=绿、D=灰、X=红
        /// </summary>
        private Color GetTierColor(CombatTier tier)
        {
            switch (tier)
            {
                case CombatTier.SSS: return new Color(1.0f, 0.93f, 0.55f);  // 钻金（最高档，比金更亮）
                case CombatTier.SS: return new Color(1.0f, 0.75f, 0.20f);  // 亮金橙（次高档）
                case CombatTier.S: return new Color(1.0f, 0.84f, 0.0f);   // 金
                case CombatTier.A: return new Color(0.61f, 0.35f, 0.71f); // 紫
                case CombatTier.B: return new Color(0.2f, 0.6f, 0.85f);   // 蓝
                case CombatTier.C: return new Color(0.18f, 0.8f, 0.44f);  // 绿
                case CombatTier.D: return new Color(0.58f, 0.65f, 0.65f); // 灰
                default: return new Color(0.85f, 0.2f, 0.2f);   // 红（X）
            }
        }

        /// <summary>
        /// 获取护甲偏好对应的徽章颜色。
        /// Heavy=暗红、Flexible=黄、Light=绿
        /// </summary>
        private Color GetArmorPrefColor(ArmorPreference pref)
        {
            switch (pref)
            {
                case ArmorPreference.Heavy: return new Color(0.75f, 0.22f, 0.17f);  // 暗红
                case ArmorPreference.Flexible: return new Color(0.95f, 0.77f, 0.06f);  // 黄
                default: return new Color(0.15f, 0.68f, 0.38f);  // 绿
            }
        }
    }
}