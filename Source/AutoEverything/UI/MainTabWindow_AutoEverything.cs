using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using AutoEverything.Core;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.UI
{
    /// <summary>
    /// 底部主按钮【自动】对应的配置窗口。
    ///
    /// 设计目标：把原本分散在"Mod 选项 + 殖民者检视 ITab 底部勾选框"的配置，
    /// 统一收拢到玩家高频入口（底部主按钮，位于"世界"右侧），
    /// 并按功能分标签页展示各自的"判定配置项"——这些配置直接参与判定规则计算。
    ///
    /// 标签页：
    /// - 人员自动：殖民者栏角色分组 + 评级开关 + 评级判定阈值 + 战斗价值公式权重
    /// - 工作自动：工作分配开关 + 保底人数/技能保底/核心工作上限/触发周期
    ///   （角色分组配置只保留在人员自动页，工作自动页仅给出跳转提示，避免同一份配置重复展示）
    /// - 高价值自动：标记开关 + 高价值档位阈值 + 扫描范围 + 通知
    /// - 携带用药：自动携带数量/周期 + 用药方案开关
    /// - 通用调试：主开关、殖民者栏默认排序、调试日志
    ///
    /// 持久化：所有字段均为 AESettings 静态字段（ModSettings），
    /// 修改后立即 Write() 写盘，避免重启丢失（与 ITab 勾选框行为一致）。
    /// 判定参数变化后清空评级缓存，使新规则立即生效。
    /// </summary>
    public class MainTabWindow_AutoEverything : MainTabWindow
    {
        // 当前标签页（static：关闭再打开保持上次位置）
        private static ConfigTab currentTab = ConfigTab.Person;

        // ScrollView 滚动位置与内容高度缓存：
        // 内容高度按上一帧实际绘制高度更新，避免新增配置项时底部被裁剪
        private static Vector2 scrollPos = Vector2.zero;
        private static float cachedContentHeight = 620f;
        private static ConfigTab cachedHeightTab = ConfigTab.None;

        // 延迟写盘标记：FloatMenu 回调在下一帧才执行，由回调置位、DoWindowContents 统一处理
        private static bool settingsDirty;

        private enum ConfigTab
        {
            None = -1,
            Person = 0,
            Work = 1,
            Mark = 2,
            Carry = 3,
            General = 4
        }

        private static readonly Color ColorLabelGray = new Color(0.70f, 0.70f, 0.70f);
        private static readonly Color ColorSectionTitle = new Color(0.88f, 0.88f, 0.92f);
        private static readonly Color ColorSectionBg = new Color(0.18f, 0.20f, 0.22f, 0.45f);

        // 角色标题颜色：与殖民者栏分组框边框颜色保持一致，便于玩家对照
        private static readonly Color ColorRoleFront = new Color(0.85f, 0.30f, 0.25f);
        private static readonly Color ColorRoleShooter = new Color(0.90f, 0.70f, 0.20f);
        private static readonly Color ColorRoleWorker = new Color(0.35f, 0.70f, 0.40f);
        private static readonly Color ColorRoleTrade = new Color(0.65f, 0.50f, 0.85f);

        public override Vector2 InitialSize
        {
            get { return new Vector2(920f, 640f); }
        }

        /// <summary>
        /// 每次打开窗口都回到顶部。
        /// scrollPos 是静态缓存（用于关闭再打开保持位置），但人员自动页内容较长，
        /// 若玩家上次滚到中下部，再次打开会看不到顶部的"殖民者栏角色分组"及前排条件，
        /// 表现为"分组选项消失"。因此打开时强制复位滚动与高度缓存。
        /// </summary>
        public override void PreOpen()
        {
            base.PreOpen();
            scrollPos = Vector2.zero;
            cachedHeightTab = ConfigTab.None;
            cachedContentHeight = 620f;
        }

        public override void DoWindowContents(Rect inRect)
        {
            // 顶部标签栏
            const float TabHeight = 32f;
            Rect tabBarRect = new Rect(inRect.x, inRect.y, inRect.width, TabHeight);
            DrawTabBar(tabBarRect);

            // 内容区：标签栏下方
            Rect contentRect = new Rect(inRect.x, tabBarRect.yMax + 6f,
                inRect.width, inRect.height - TabHeight - 6f);

            // 主开关关闭时给出提示，但不隐藏配置（玩家仍可调整后一键开启）
            if (!AESettings.enabled)
            {
                Rect warnRect = new Rect(contentRect.x, contentRect.y, contentRect.width, 24f);
                GUI.color = new Color(1f, 0.72f, 0.55f);
                bool prevWrap = Text.WordWrap;
                Text.WordWrap = false;
                Widgets.Label(warnRect, "AE_CFG_MasterDisabled".Translate());
                Text.WordWrap = prevWrap;
                GUI.color = Color.white;
                contentRect.y += 28f;
                contentRect.height -= 28f;
            }

            // 标签切换时重置滚动与高度缓存（不同标签内容量差异大）
            if (cachedHeightTab != currentTab)
            {
                cachedHeightTab = currentTab;
                scrollPos = Vector2.zero;
                cachedContentHeight = 620f;
            }

            Rect viewRect = new Rect(0f, 0f, contentRect.width - 16f, cachedContentHeight);
            Widgets.BeginScrollView(contentRect, ref scrollPos, viewRect);

            Listing_Standard l = new Listing_Standard();
            l.Begin(viewRect);

            switch (currentTab)
            {
                case ConfigTab.Person:
                    DrawPersonTab(l);
                    break;
                case ConfigTab.Work:
                    DrawWorkTab(l);
                    break;
                case ConfigTab.Mark:
                    DrawMarkTab(l);
                    break;
                case ConfigTab.Carry:
                    DrawCarryTab(l);
                    break;
                default:
                    DrawGeneralTab(l);
                    break;
            }

            l.End();
            cachedContentHeight = l.CurHeight + 40f;
            Widgets.EndScrollView();

            // 统一写盘：任一配置变化即写入一次，避免重复 IO
            if (settingsDirty)
            {
                settingsDirty = false;
                AutoEverythingMod.settings?.Write();
                // 判定参数变化会使既有评级失效，立即清空缓存让新规则生效
                TierCacheService.Clear();
            }
        }

        // ===================== 标签栏 =====================

        private void DrawTabBar(Rect rect)
        {
            int count = 5;
            float gap = 4f;
            float width = (rect.width - gap * (count - 1)) / count;

            for (int i = 0; i < count; i++)
            {
                ConfigTab tab = (ConfigTab)i;
                Rect btnRect = new Rect(rect.x + i * (width + gap), rect.y, width, rect.height);

                if (Widgets.ButtonText(btnRect, GetTabLabelKey(tab).Translate()))
                {
                    currentTab = tab;
                }
                // 选中态：半透明高亮 + 边框（按钮自身背景不透明，必须绘制在按钮之后才可见）
                if (currentTab == tab)
                {
                    Widgets.DrawHighlightSelected(btnRect);
                    Widgets.DrawBox(btnRect, 1);
                }
            }
        }

        private static string GetTabLabelKey(ConfigTab tab)
        {
            switch (tab)
            {
                case ConfigTab.Person: return "AE_CFG_Tab_Person";
                case ConfigTab.Work: return "AE_CFG_Tab_Work";
                case ConfigTab.Mark: return "AE_CFG_Tab_Mark";
                case ConfigTab.Carry: return "AE_CFG_Tab_Carry";
                default: return "AE_CFG_Tab_General";
            }
        }

        // ===================== 人员自动 =====================

        /// <summary>
        /// 殖民者栏五类角色分组配置（前排/射手/工人/贸易/其他）。
        /// 只在人员自动页展示一次，工作自动页不再重复绘制同一份配置；
        /// 分组框支持右键拖拽换序，任一条件变化后立即重排殖民者栏。
        /// </summary>
        private static void DrawRoleGroupSection(Listing_Standard l)
        {
            // 分组开关不属于 RoleGroupRules，需单独记录；其余判定参数用规则快照对比
            bool beforeGroup = AESettings.groupColonistBarByRole;
            RoleGroupRules before = RoleGroupRules.FromSettings();

            BeginSection(l, "AE_CFG_Section_RoleGroup".Translate());

            DrawCheckbox(l, "AE_GroupByRole", ref AESettings.groupColonistBarByRole, "AE_TT_GroupByRole");
            DrawRuleText(l, "AE_CFG_RoleGroup_ForceHint".Translate());

            // 前排：食尸鬼 / 格斗者特质 / 格斗专家职位
            DrawRoleTitle(l, "AE_RoleGroup_Front", ColorRoleFront);
            bool[] front = { AESettings.groupFrontGhoul, AESettings.groupBrawlerTraitFront, AESettings.groupFrontMeleeSpecialist };
            DrawCheckboxRow(l,
                new[] { "AE_CFG_Front_Ghoul", "AE_CFG_Front_Brawler", "AE_CFG_Front_MeleeSpecialist" },
                front,
                new[] { "AE_TT_Front_Ghoul", "AE_TT_Front_Brawler", "AE_TT_Front_MeleeSpecialist" });
            AESettings.groupFrontGhoul = front[0];
            AESettings.groupBrawlerTraitFront = front[1];
            AESettings.groupFrontMeleeSpecialist = front[2];

            // 射手：射击专家职位 / 乱开枪特质 / 猎人
            DrawRoleTitle(l, "AE_RoleGroup_Shooter", ColorRoleShooter);
            bool[] shooter = { AESettings.groupShooterSpecialist, AESettings.groupTriggerHappyShooter, AESettings.groupHunterAsShooter };
            DrawCheckboxRow(l,
                new[] { "AE_CFG_Shooter_Specialist", "AE_CFG_Shooter_TriggerHappy", "AE_CFG_Shooter_Hunter" },
                shooter,
                new[] { "AE_TT_Shooter_Specialist", "AE_TT_Shooter_TriggerHappy", "AE_TT_Shooter_Hunter" });
            AESettings.groupShooterSpecialist = shooter[0];
            AESettings.groupTriggerHappyShooter = shooter[1];
            AESettings.groupHunterAsShooter = shooter[2];

            // 工人：无法施暴 / 生产专家职位 / 工作狂特质 / 生产兴趣双火
            DrawRoleTitle(l, "AE_RoleGroup_Worker", ColorRoleWorker);
            bool[] worker = { AESettings.groupWorkerPacifist, AESettings.groupWorkerProductionSpecialist,
                AESettings.groupIndustriousWorker, AESettings.groupCraftingPassionWorker };
            DrawCheckboxRow(l,
                new[] { "AE_CFG_Worker_Pacifist", "AE_CFG_Worker_ProductionSpecialist",
                    "AE_CFG_Worker_Industrious", "AE_CFG_Worker_CraftingMajor" },
                worker,
                new[] { "AE_TT_Worker_Pacifist", "AE_TT_Worker_ProductionSpecialist",
                    "AE_TT_Worker_Industrious", "AE_TT_Worker_CraftingMajor" });
            AESettings.groupWorkerPacifist = worker[0];
            AESettings.groupWorkerProductionSpecialist = worker[1];
            AESettings.groupIndustriousWorker = worker[2];
            AESettings.groupCraftingPassionWorker = worker[3];

            // 贸易：道德家职位 / 俊俏/沉鱼落雁特质 / 社交兴趣双火
            DrawRoleTitle(l, "AE_RoleGroup_Trade", ColorRoleTrade);
            bool[] trade = { AESettings.groupTradeMoralist, AESettings.groupBeautyTrade, AESettings.groupSocialPassionTrade };
            DrawCheckboxRow(l,
                new[] { "AE_CFG_Trade_Moralist", "AE_CFG_Trade_Beauty", "AE_CFG_Trade_SocialMajor" },
                trade,
                new[] { "AE_TT_Trade_Moralist", "AE_TT_Trade_Beauty", "AE_TT_Trade_SocialMajor" });
            AESettings.groupTradeMoralist = trade[0];
            AESettings.groupBeautyTrade = trade[1];
            AESettings.groupSocialPassionTrade = trade[2];

            // 技能/兴趣补充判定：强制条件未命中时的兜底规则
            DrawRuleText(l, "AE_CFG_RoleGroup_SkillTitle".Translate());

            DrawIntSlider(l, "AE_CFG_GroupFrontSkillMin", ref AESettings.groupFrontSkillMin, 5, 18, "AE_TT_GroupFrontSkillMin");
            DrawIntSlider(l, "AE_CFG_GroupShooterSkillMin", ref AESettings.groupShooterSkillMin, 5, 18, "AE_TT_GroupShooterSkillMin");
            DrawIntSlider(l, "AE_CFG_GroupTradeSkillMin", ref AESettings.groupTradeSkillMin, 5, 18, "AE_TT_GroupTradeSkillMin");
            DrawIntSlider(l, "AE_CFG_GroupWorkerMaxCombatSkill", ref AESettings.groupWorkerMaxCombatSkill, 0, 12,
                "AE_TT_GroupWorkerMaxCombatSkill");

            DrawCheckbox(l, "AE_CFG_GroupPassionPriority", ref AESettings.groupPassionPriority, "AE_TT_GroupPassionPriority");

            // 判定准则说明：数值随滑块实时变化，玩家可对照调整
            DrawRuleText(l, "AE_CFG_RoleGroupRule_Order".Translate());
            DrawRuleText(l, "AE_CFG_RoleGroupRule_Front".Translate(AESettings.groupFrontSkillMin));
            DrawRuleText(l, "AE_CFG_RoleGroupRule_Shooter".Translate(AESettings.groupShooterSkillMin));
            DrawRuleText(l, "AE_CFG_RoleGroupRule_Trade".Translate(AESettings.groupTradeSkillMin));
            DrawRuleText(l, "AE_CFG_RoleGroupRule_Worker".Translate(AESettings.groupWorkerMaxCombatSkill));
            DrawRuleText(l, "AE_CFG_RoleGroupRule_Other".Translate());
            DrawRuleText(l, "AE_CFG_RoleGroupRule_Drag".Translate());
            EndSection(l);

            // 开关或任一判定条件变化 → 立即重排殖民者栏
            if (beforeGroup != AESettings.groupColonistBarByRole
                || before.DiffersFrom(RoleGroupRules.FromSettings()))
            {
                AESettings.OnGroupByRoleChanged();
            }
        }

        private static void DrawPersonTab(Listing_Standard l)
        {
            DrawRoleGroupSection(l);

            BeginSection(l, "AE_CFG_Section_PersonSwitch".Translate());

            DrawCheckbox(l, "AE_AutoTierTag", ref AESettings.autoTierTag, "AE_TT_AutoTierTag");
            l.Gap(2f);

            // 两个动作按钮：立即评级 / 清除评级前缀
            Rect btnRect = l.GetRect(28f);
            float halfW = (btnRect.width - 8f) * 0.5f;
            if (Widgets.ButtonText(new Rect(btnRect.x, btnRect.y, halfW, 28f), "AE_CFG_ApplyTierNow".Translate()))
            {
                AutoExecutor.TriggerTierNow();
            }
            if (Widgets.ButtonText(new Rect(btnRect.x + halfW + 8f, btnRect.y, halfW, 28f),
                                   "AE_CFG_ClearTierNow".Translate()))
            {
                int cleared = AESettings.ClearTierTagsFromAllPawns();
                Messages.Message("AE_TierTag_ClearResult".Translate(cleared), MessageTypeDefOf.TaskCompletion);
            }
            EndSection(l);

            BeginSection(l, "AE_CFG_Section_TierRules".Translate());

            DrawIntSlider(l, "AE_CFG_TierAMajorMin", ref AESettings.tierAMajorMin, 1, 5, "AE_TT_TierAMajorMin");
            DrawIntSlider(l, "AE_CFG_TierAPassionTotal", ref AESettings.tierAPassionTotal, 1, 9, "AE_TT_TierAPassionTotal");
            DrawIntSlider(l, "AE_CFG_TierBMajorMin", ref AESettings.tierBMajorMin, 1, 5, "AE_TT_TierBMajorMin");
            DrawIntSlider(l, "AE_CFG_TierBPassionTotal", ref AESettings.tierBPassionTotal, 1, 9, "AE_TT_TierBPassionTotal");
            DrawIntSlider(l, "AE_CFG_TierSSWorkMajors", ref AESettings.tierSSWorkMajors, 1, 6, "AE_TT_TierSSWorkMajors");
            DrawIntSlider(l, "AE_CFG_TierSSSWorkMajors", ref AESettings.tierSSSWorkMajors, 1, 6, "AE_TT_TierSSSWorkMajors");

            DrawCheckbox(l, "AE_CFG_SpecialTalentToS", ref AESettings.tierSpecialTalentToS, "AE_TT_SpecialTalentToS");
            DrawCheckbox(l, "AE_CFG_BeautySocialToS", ref AESettings.tierBeautySocialToS, "AE_TT_BeautySocialToS");
            DrawCheckbox(l, "AE_CFG_SpouseExemption", ref AESettings.spouseExemptionEnabled, "AE_TT_SpouseExemption");

            if (AESettings.spouseExemptionEnabled)
            {
                CombatTier before = AESettings.spouseExemptionTier;
                DrawTierSelector(l, "AE_CFG_SpouseExemptionTier", before,
                    "AE_TT_SpouseExemptionTier", tier =>
                    {
                        AESettings.spouseExemptionTier = tier;
                        // FloatMenu 回调在下一帧执行，直接写盘避免玩家立即关闭面板时丢失
                        AutoEverythingMod.settings?.Write();
                        TierCacheService.Clear();
                    });
            }
            EndSection(l);

            BeginSection(l, "AE_CombatValueWeights".Translate());

            DrawFloatSlider(l, "AE_cvSkillWeight", ref AESettings.cvSkillWeight, 0.1f, 3.0f, "F2", "AE_TT_cvSkillWeight");
            DrawFloatSlider(l, "AE_cvPassionNoneMult", ref AESettings.cvPassionNoneMult, 0.1f, 3.0f, "F2", "AE_TT_cvPassionMult");
            DrawFloatSlider(l, "AE_cvPassionMinorMult", ref AESettings.cvPassionMinorMult, 0.1f, 3.0f, "F2", "AE_TT_cvPassionMult");
            DrawFloatSlider(l, "AE_cvPassionMajorMult", ref AESettings.cvPassionMajorMult, 0.1f, 3.0f, "F2", "AE_TT_cvPassionMult");
            DrawFloatSlider(l, "AE_cvToughBonus", ref AESettings.cvToughBonus, 0f, 100f, "F0", "AE_TT_cvTraitBonus");
            DrawFloatSlider(l, "AE_cvTriggerHappyPenalty", ref AESettings.cvTriggerHappyPenalty, -50f, 0f, "F0", "AE_TT_cvTraitBonus");
            DrawFloatSlider(l, "AE_cvCarefulShooterBonus", ref AESettings.cvCarefulShooterBonus, 0f, 50f, "F0", "AE_TT_cvTraitBonus");
            EndSection(l);
        }

        // ===================== 工作自动 =====================

        private static void DrawWorkTab(Listing_Standard l)
        {
            // 角色分组配置已统一收拢到人员自动页，此处只提示入口，避免同一份配置重复出现
            DrawRuleText(l, "AE_CFG_RoleGroup_MovedHint".Translate());

            BeginSection(l, "AE_CFG_Section_WorkSwitch".Translate());

            DrawCheckbox(l, "AE_AutoWorkConfig", ref AESettings.autoWorkEnabled, "AE_TT_AutoWorkConfig");
            l.Gap(2f);
            Rect btnRect = l.GetRect(28f);
            if (Widgets.ButtonText(btnRect, "AE_CFG_ApplyWorkNow".Translate()))
            {
                AutoExecutor.TriggerWorkNow();
            }
            EndSection(l);

            BeginSection(l, "AE_CFG_Section_WorkRules".Translate());

            DrawIntSlider(l, "AE_CFG_MaxCoreWorkCount", ref AESettings.maxCoreWorkCount, 1, 6, "AE_TT_MaxCoreWorkCount");
            DrawIntSlider(l, "AE_CFG_SkillFloorHighLevel", ref AESettings.skillFloorHighLevel, 0, 20, "AE_TT_SkillFloor");
            DrawIntSlider(l, "AE_CFG_SkillFloorMidLevel", ref AESettings.skillFloorMidLevel, 0, 20, "AE_TT_SkillFloor");
            DrawCheckbox(l, "AE_CFG_ResearchDowngrade", ref AESettings.researchServiceDowngrade, "AE_TT_ResearchDowngrade");
            DrawSecondsSlider(l, "AE_CFG_WorkInterval", ref AESettings.workIntervalTicks, 30, 300, "AE_TT_WorkInterval");
            DrawCheckbox(l, "AE_CFG_SkipInCombat", ref AESettings.skipAutoInCombat, "AE_TT_SkipInCombat");
            EndSection(l);

            l.Gap(4f);
            Text.Font = GameFont.Tiny;
            GUI.color = ColorLabelGray;
            l.Label("AE_CFG_WorkRulesHint".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        // ===================== 高价值自动 =====================

        private static void DrawMarkTab(Listing_Standard l)
        {
            BeginSection(l, "AE_CFG_Section_MarkSwitch".Translate());

            DrawCheckbox(l, "AE_AutoMarkPawn", ref AESettings.autoMarkPawn, "AE_TT_AutoMarkPawn");
            DrawCheckbox(l, "AE_CFG_MarkNotify", ref AESettings.markNotifyMessage, "AE_TT_MarkNotify");
            DrawCheckbox(l, "AE_CFG_MarkIncludeNonPlayer", ref AESettings.markIncludeNonPlayer, "AE_TT_MarkIncludeNonPlayer");
            l.Gap(2f);

            Rect btnRect = l.GetRect(28f);
            if (Widgets.ButtonText(btnRect, "AE_CFG_ScanMarkNow".Translate()))
            {
                AutoExecutor.TriggerMarkNow();
            }
            EndSection(l);

            BeginSection(l, "AE_CFG_Section_MarkRules".Translate());

            DrawTierSelector(l, "AE_CFG_MarkTier", AESettings.markHighValueTier,
                "AE_TT_MarkTier", tier =>
                {
                    AESettings.markHighValueTier = tier;
                    // 同上：回调延迟执行，立即写盘并清缓存
                    AutoEverythingMod.settings?.Write();
                    TierCacheService.Clear();
                });
            EndSection(l);

            l.Gap(4f);
            Text.Font = GameFont.Tiny;
            GUI.color = ColorLabelGray;
            l.Label("AE_CFG_MarkRulesHint".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        // ===================== 携带与用药 =====================

        private static void DrawCarryTab(Listing_Standard l)
        {
            BeginSection(l, "AE_CFG_Section_Carry".Translate());

            DrawCheckbox(l, "AE_AutoCarry", ref AESettings.autoCarryEnabled, "AE_TT_AutoCarry");
            DrawIntSlider(l, "AE_CFG_CarryFoodCount", ref AESettings.carryFoodCount, 0, 20, "AE_TT_CarryFoodCount");
            DrawSecondsSlider(l, "AE_CFG_CarryInterval", ref AESettings.carryIntervalTicks, 30, 300, "AE_TT_CarryInterval");
            l.Gap(2f);

            // 立即执行 / 恢复默认：并排放置，节省垂直空间
            Rect btnRow = l.GetRect(28f);
            float halfWidth = (btnRow.width - 8f) * 0.5f;
            Rect applyRect = new Rect(btnRow.x, btnRow.y, halfWidth, btnRow.height);
            Rect resetRect = new Rect(btnRow.x + halfWidth + 8f, btnRow.y, halfWidth, btnRow.height);
            if (Widgets.ButtonText(applyRect, "AE_CFG_ApplyCarryNow".Translate()))
            {
                AutoExecutor.TriggerCarryNow();
            }
            if (Widgets.ButtonText(resetRect, "AE_CFG_ResetCarryItems".Translate()))
            {
                AESettings.ResetCarryItemsToDefault();
                settingsDirty = true;
            }
            TooltipHandler.TipRegion(resetRect, "AE_TT_ResetCarryItems".Translate());
            EndSection(l);

            BeginSection(l, "AE_CFG_Section_CarryDrugs".Translate());
            DrawCarryItemGrid(l, CarryItemKind.Drug);
            DrawRuleText(l, "AE_CFG_CarryDrugHint".Translate());
            EndSection(l);

            BeginSection(l, "AE_CFG_Section_CarrySerums".Translate());
            DrawCheckbox(l, "AE_CFG_CarrySerumHighValueOnly", ref AESettings.carrySerumHighValueOnly,
                "AE_TT_CarrySerumHighValueOnly");
            DrawCarryItemGrid(l, CarryItemKind.Serum);
            EndSection(l);

            BeginSection(l, "AE_CFG_Section_Drug".Translate());

            DrawCheckbox(l, "AE_AutoDrugPolicy", ref AESettings.autoDrugPolicyEnabled, "AE_TT_AutoDrugPolicy");
            l.Gap(2f);

            Rect drugBtnRect = l.GetRect(28f);
            if (Widgets.ButtonText(drugBtnRect, "AE_CFG_ApplyDrugNow".Translate()))
            {
                AutoExecutor.TriggerDrugPolicyNow();
            }
            EndSection(l);
        }

        // 携带物品网格绘制缓冲（每帧重建，避免反复分配）
        private static readonly List<CarryItemDef> carryItemBuffer = new List<CarryItemDef>();

        // 单项携带数量上限（背包容量有限，超过意义不大）
        private const int CarryItemMaxCount = 10;

        /// <summary>
        /// 逐项携带数量：两列网格，每项"物品名 xN + 滑条"。
        /// 缺失 Def 的物品（缺 DLC/被 MOD 移除）不显示，避免出现空白行。
        /// </summary>
        private static void DrawCarryItemGrid(Listing_Standard l, CarryItemKind kind)
        {
            carryItemBuffer.Clear();
            CarryItemDef[] items = CarryItemCatalog.Items;
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].Kind != kind) continue;
                if (DefDatabase<ThingDef>.GetNamedSilentFail(items[i].DefName) == null) continue;
                carryItemBuffer.Add(items[i]);
            }

            const float rowHeight = 26f;
            const float gap = 8f;
            for (int i = 0; i < carryItemBuffer.Count; i += 2)
            {
                Rect row = l.GetRect(rowHeight);
                float cellWidth = (row.width - gap) * 0.5f;
                DrawCarryItemCell(new Rect(row.x, row.y, cellWidth, rowHeight), carryItemBuffer[i]);
                if (i + 1 < carryItemBuffer.Count)
                {
                    DrawCarryItemCell(new Rect(row.x + cellWidth + gap, row.y, cellWidth, rowHeight),
                        carryItemBuffer[i + 1]);
                }
            }
            l.Gap(4f);
        }

        private static void DrawCarryItemCell(Rect rect, CarryItemDef item)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(item.DefName);
            if (def == null) return;

            int count = AESettings.GetCarryItemCount(item.DefName);

            // 标签："物品名 xN"，关闭换行避免中文撑乱两列布局
            Rect labelRect = new Rect(rect.x, rect.y, Mathf.Max(0f, rect.width - 96f), rect.height);
            DrawGrayLabel(labelRect, def.LabelCap + " x" + count);
            TooltipHandler.TipRegion(labelRect, "AE_TT_CarryItemCount".Translate());

            Rect sliderRect = new Rect(rect.xMax - 92f, rect.y + 3f, 92f, rect.height - 6f);
            int newCount = Mathf.RoundToInt(Widgets.HorizontalSlider(sliderRect, count, 0, CarryItemMaxCount));
            if (newCount != count)
            {
                AESettings.SetCarryItemCount(item.DefName, newCount);
                settingsDirty = true;
            }
        }

        // ===================== 通用与调试 =====================
        private static void DrawGeneralTab(Listing_Standard l)
        {
            BeginSection(l, "AE_CFG_Section_General".Translate());

            DrawCheckbox(l, "AE_Enabled", ref AESettings.enabled, "AE_TT_Enabled");
            DrawCheckbox(l, "AE_DebugLogging", ref AESettings.debugLogging, "AE_DebugLogging_Desc");
            EndSection(l);

            BeginSection(l, "AE_CFG_Section_ColonistBar".Translate());

            // 默认排序方式：按钮点开 FloatMenu 选择
            Rect sortRect = l.GetRect(28f);
            if (Widgets.ButtonText(sortRect,
                    "AE_DefaultSortMode".Translate() + ": " + ("AE_SortMode_" + AESettings.defaultSortMode).Translate()))
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();
                foreach (ColonistBarSortMode mode in Enum.GetValues(typeof(ColonistBarSortMode)))
                {
                    ColonistBarSortMode local = mode;
                    options.Add(new FloatMenuOption(("AE_SortMode_" + local).Translate(), () =>
                    {
                        AESettings.defaultSortMode = local;
                        AutoEverythingMod.settings?.Write();
                    }));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
            TooltipHandler.TipRegion(sortRect, "AE_TT_DefaultSortMode".Translate());
            EndSection(l);

            l.Gap(6f);
            Text.Font = GameFont.Tiny;
            GUI.color = ColorLabelGray;
            l.Label("AE_CFG_GeneralHint".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        // ===================== 通用绘制工具 =====================

        /// <summary>
        /// Section 标题行：浅色底 + 标题文字，配合 EndSection 形成分组。
        /// </summary>
        private static void BeginSection(Listing_Standard l, string title)
        {
            l.Gap(6f);
            Rect titleRect = l.GetRect(24f);
            Widgets.DrawBoxSolid(titleRect, ColorSectionBg);

            bool prevWrap = Text.WordWrap;
            Text.WordWrap = false;
            GUI.color = ColorSectionTitle;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(titleRect.x + 6f, titleRect.y, titleRect.width - 6f, titleRect.height),
                "• " + title);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            Text.WordWrap = prevWrap;
            l.Gap(4f);
        }

        private static void EndSection(Listing_Standard l)
        {
            l.Gap(4f);
            l.GapLine();
        }

        /// <summary>
        /// 勾选框：统一关闭换行（中文超宽不撑乱布局）+ 挂 Tooltip，
        /// 值变化时标记写盘。
        /// </summary>
        private static void DrawCheckbox(Listing_Standard l, string labelKey, ref bool value, string tooltipKey)
        {
            Rect rect = l.GetRect(24f);
            bool prevWrap = Text.WordWrap;
            Text.WordWrap = false;
            bool prev = value;
            Widgets.CheckboxLabeled(rect, labelKey.Translate(), ref value);
            if (tooltipKey != null) TooltipHandler.TipRegion(rect, tooltipKey.Translate());
            Text.WordWrap = prevWrap;
            if (value != prev) settingsDirty = true;
        }

        /// <summary>
        /// 整数滑块：单行"标签: 数值" + 单行滑块，节省垂直空间。
        /// </summary>
        private static void DrawIntSlider(Listing_Standard l, string labelKey, ref int value,
            int min, int max, string tooltipKey)
        {
            Rect labelRect = l.GetRect(22f);
            DrawGrayLabel(labelRect, labelKey.Translate() + ": " + value);
            if (tooltipKey != null) TooltipHandler.TipRegion(labelRect, tooltipKey.Translate());

            Rect sliderRect = l.GetRect(20f);
            int prev = value;
            value = Mathf.RoundToInt(Widgets.HorizontalSlider(sliderRect, value, min, max));
            if (value != prev) settingsDirty = true;
        }

        /// <summary>
        /// 秒数滑块：界面显示秒，底层存 tick（*60），便于玩家理解触发周期。
        /// </summary>
        private static void DrawSecondsSlider(Listing_Standard l, string labelKey, ref int ticks,
            int minSec, int maxSec, string tooltipKey)
        {
            int sec = Mathf.RoundToInt(ticks / 60f);
            Rect labelRect = l.GetRect(22f);
            DrawGrayLabel(labelRect, labelKey.Translate() + ": " + sec + "s");
            if (tooltipKey != null) TooltipHandler.TipRegion(labelRect, tooltipKey.Translate());

            Rect sliderRect = l.GetRect(20f);
            int newSec = Mathf.RoundToInt(Widgets.HorizontalSlider(sliderRect, sec, minSec, maxSec));
            if (newSec != sec)
            {
                ticks = newSec * 60;
                settingsDirty = true;
            }
        }

        /// <summary>
        /// 浮点滑块：用于战斗价值公式权重。
        /// </summary>
        private static void DrawFloatSlider(Listing_Standard l, string labelKey, ref float value,
            float min, float max, string format, string tooltipKey)
        {
            Rect labelRect = l.GetRect(22f);
            DrawGrayLabel(labelRect, labelKey.Translate() + ": " + value.ToString(format));
            if (tooltipKey != null) TooltipHandler.TipRegion(labelRect, tooltipKey.Translate());

            Rect sliderRect = l.GetRect(20f);
            float prev = value;
            value = Widgets.HorizontalSlider(sliderRect, value, min, max);
            if (!Mathf.Approximately(value, prev)) settingsDirty = true;
        }

        /// <summary>
        /// 档位选择按钮：点击弹出 FloatMenu 从 SSS 到 X 倒序选择。
        /// 选择回调在下一帧执行，故通过 settingsDirty 统一写盘。
        /// </summary>
        private static void DrawTierSelector(Listing_Standard l, string labelKey, CombatTier current,
            string tooltipKey, Action<CombatTier> onPick)
        {
            Rect rect = l.GetRect(28f);
            if (Widgets.ButtonText(rect, labelKey.Translate() + ": " + current))
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();
                for (int t = (int)CombatTier.SSS; t >= (int)CombatTier.X; t--)
                {
                    CombatTier local = (CombatTier)t;
                    options.Add(new FloatMenuOption(
                        local + " - " + ("AE_Tier_" + local).Translate(),
                        () => onPick(local)));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
            if (tooltipKey != null) TooltipHandler.TipRegion(rect, tooltipKey.Translate());
        }

        /// <summary>
        /// 判定准则说明文字：灰色小字，按内容自动换行并预留高度（长文本不截断）。
        /// </summary>
        private static void DrawRuleText(Listing_Standard l, string text)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = ColorLabelGray;
            float height = Text.CalcHeight(text, l.ColumnWidth);
            Rect rect = l.GetRect(height);
            Widgets.Label(rect, text);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            l.Gap(2f);
        }

        /// <summary>
        /// 角色标题：左侧彩色竖条 + 角色名（与殖民者栏分组框颜色一致）。
        /// </summary>
        private static void DrawRoleTitle(Listing_Standard l, string labelKey, Color color)
        {
            Rect rect = l.GetRect(20f);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y + 4f, 4f, 12f), color);

            bool prevWrap = Text.WordWrap;
            Text.WordWrap = false;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = color;
            Widgets.Label(new Rect(rect.x + 8f, rect.y, rect.width - 8f, rect.height), labelKey.Translate());
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = prevWrap;
        }

        /// <summary>
        /// 单行多列勾选框：用于"逐角色强制定位条件"，比逐行 Checkbox 节省垂直空间。
        /// 注意：原生 CheckboxLabeled 只有 24x24 复选框本体可点击，中文标签较长时
        /// 玩家点在文字上会"勾选不到"；这里手动绘制复选框，并让整格标签区域都可点击。
        /// </summary>
        private static void DrawCheckboxRow(Listing_Standard l, string[] labelKeys, bool[] values,
            string[] tooltipKeys)
        {
            const float boxSize = 24f;
            Rect row = l.GetRect(boxSize);
            int count = labelKeys.Length;
            const float gap = 8f;
            float width = (row.width - gap * (count - 1)) / count;

            for (int i = 0; i < count; i++)
            {
                Rect cell = new Rect(row.x + i * (width + gap), row.y, width, boxSize);
                bool prev = values[i];

                // 悬停高亮提示整格可点击
                Widgets.DrawHighlightIfMouseover(cell);

                // 复选框本体
                Widgets.Checkbox(new Vector2(cell.x, cell.y), ref values[i]);

                // 标签区域：同时支持点击切换（整格可点）
                Rect labelRect = new Rect(cell.x + boxSize + 2f, cell.y,
                    Mathf.Max(0f, cell.width - boxSize - 2f), cell.height);
                bool prevWrap = Text.WordWrap;
                TextAnchor prevAnchor = Text.Anchor;
                Text.WordWrap = false;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(labelRect, labelKeys[i].Translate());
                Text.Anchor = prevAnchor;
                Text.WordWrap = prevWrap;

                if (Widgets.ButtonInvisible(labelRect))
                {
                    values[i] = !values[i];
                }

                if (tooltipKeys != null && i < tooltipKeys.Length && tooltipKeys[i] != null)
                {
                    TooltipHandler.TipRegion(cell, tooltipKeys[i].Translate());
                }
                if (values[i] != prev) settingsDirty = true;
            }
        }

        /// <summary>
        /// 灰色单行标签：关闭换行并垂直居中，避免中文超宽换行撑乱布局。
        /// </summary>
        private static void DrawGrayLabel(Rect rect, string text)
        {
            bool prevWrap = Text.WordWrap;
            Text.WordWrap = false;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = ColorLabelGray;
            Widgets.Label(rect, text);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = prevWrap;
        }
    }
}
