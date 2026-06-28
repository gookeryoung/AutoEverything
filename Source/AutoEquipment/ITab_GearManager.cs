using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace AutoEquipment
{
    /// <summary>
    /// Pawn 检视面板的自定义标签页：展示角色、情境与装备状态，
    /// 并提供锁定/角色覆盖控制。食尸鬼不显示此面板。
    /// </summary>
    public class ITab_GearManager : ITab
    {
        private Vector2 scrollPos;
        private float lastHeight;

        public ITab_GearManager()
        {
            labelKey = "AE_Tab";
            // 扩大面板：容纳全局重配规则的可调项
            size = new Vector2(340f, 620f);
        }

        public override bool IsVisible
        {
            get
            {
                // 通过 BasePawn 注入 ITab 时，动物/机械族等也会创建实例
                // 此处过滤：仅玩家阵营人类like 且非食尸鬼才显示
                return SelPawn is Pawn pawn
                    && pawn.Faction == Faction.OfPlayer
                    && PawnSuitabilityChecker.CanManageGear(pawn)
                    && !DLCCompat.IsGhoul(pawn);
            }
        }

        protected override void FillTab()
        {
            if (!(SelPawn is Pawn pawn)) return;

            var comp = pawn.GetComp<CompGearManager>();
            if (comp == null) return;

            // 按钮固定在面板最下方，独立于 ScrollView，避免滚动时被隐藏
            float buttonHeight = 30f;
            float buttonGap = 10f;

            Rect rect = new Rect(0f, 0f, size.x, size.y).ContractedBy(10f);
            Rect scrollRect = rect;
            scrollRect.height -= (buttonHeight + buttonGap);

            // 内容高度按需扩展，ScrollView 会滚动
            Rect contentRect = new Rect(0f, 0f, scrollRect.width - 16f, lastHeight);

            Widgets.BeginScrollView(scrollRect, ref scrollPos, contentRect);

            Listing_Standard l = new Listing_Standard();
            l.Begin(contentRect);

            // ===================== 顶部：当前 Pawn 状态 =====================
            Text.Font = GameFont.Medium;
            l.Label("AE_TabTitle".Translate());
            Text.Font = GameFont.Small;
            l.Gap();

            // 当前角色（DEBUG 模式下附加战斗价值档次，如 王五[S]）
            Role role = comp.CurrentRole;
            string tierSuffix = AEDebug.IsActive
                ? " [" + SidearmAllocator.GetCombatTier(pawn) + "]"
                : "";
            l.Label("AE_CurrentRole".Translate() + ": " + ("AE_Role_" + role).Translate() + tierSuffix);

            // 当前情境
            GearContext context = ContextDetector.GetContext(pawn);
            l.Label("AE_CurrentContext".Translate() + ": " + ("AE_Context_" + context).Translate());
            l.Gap();

            // 锁定开关
            l.CheckboxLabeled("AE_LockGear".Translate(), ref comp.locked);
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Text.Font = GameFont.Tiny;
            l.Label("AE_LockGear_Desc".Translate());
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            l.Gap();

            // 角色覆盖
            l.CheckboxLabeled("AE_OverrideRole".Translate(), ref comp.overrideRole);
            if (comp.overrideRole)
            {
                l.Gap(4f);
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

            l.GapLine();

            // ===================== 中部：当前装备摘要 =====================
            l.Label("AE_PrimaryWeapon".Translate() + ": "
                + (pawn.equipment?.Primary?.LabelShort ?? "AE_None".Translate()));

            // 副武器
            if (comp.sidearm != null)
            {
                l.Label("AE_Sidearm".Translate() + ": " + comp.sidearm.LabelShort);
            }

            l.GapLine();

            // 已穿戴防具摘要
            if (pawn.apparel?.WornApparel != null)
            {
                l.Label("AE_WornApparel".Translate() + " (" + pawn.apparel.WornApparel.Count + "):");
                foreach (Apparel apparel in pawn.apparel.WornApparel)
                {
                    l.Label("  - " + apparel.LabelShort);
                }
            }

            // ===================== 底部：全局重配规则（直接列出，无需打开新窗口） =====================
            l.GapLine();
            Text.Font = GameFont.Medium;
            l.Label("AE_ReallocRules_Title".Translate());
            Text.Font = GameFont.Small;
            l.Gap();

            // ---- 战斗价值公式 ----
            GUI.color = new Color(0.85f, 0.85f, 0.85f);
            l.Label("AE_ReallocRules_CombatValue".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.75f, 0.75f, 0.75f);
            l.Label("AE_ReallocRules_CombatValue_Formula".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            l.Gap(4f);

            // 兴趣乘数（3 个 Slider）
            l.Label("AE_ReallocRules_PassionMult".Translate());
            l.Label("  " + "AE_ReallocRules_PassionNone".Translate() + ": " + AESettings.cvPassionNoneMult.ToString("F1"));
            AESettings.cvPassionNoneMult = l.Slider(AESettings.cvPassionNoneMult, 0.5f, 3.0f);
            l.Label("  " + "AE_ReallocRules_PassionMinor".Translate() + ": " + AESettings.cvPassionMinorMult.ToString("F1"));
            AESettings.cvPassionMinorMult = l.Slider(AESettings.cvPassionMinorMult, 0.5f, 3.0f);
            l.Label("  " + "AE_ReallocRules_PassionMajor".Translate() + ": " + AESettings.cvPassionMajorMult.ToString("F1"));
            AESettings.cvPassionMajorMult = l.Slider(AESettings.cvPassionMajorMult, 0.5f, 3.0f);
            l.Gap(4f);

            // 技能权重
            l.Label("AE_ReallocRules_SkillWeight".Translate() + ": " + AESettings.cvSkillWeight.ToString("F2"));
            AESettings.cvSkillWeight = l.Slider(AESettings.cvSkillWeight, 0.5f, 2.0f);
            l.Gap(4f);

            // ---- 特质加分 ----
            GUI.color = new Color(0.85f, 0.85f, 0.85f);
            l.Label("AE_ReallocRules_TraitBonus".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            l.Label("AE_ReallocRules_TraitBonus_Desc".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            l.Label("  " + "AE_ReallocRules_Tough".Translate() + ": " + AESettings.cvToughBonus.ToString("F0"));
            AESettings.cvToughBonus = l.Slider(AESettings.cvToughBonus, -50f, 100f);
            l.Label("  " + "AE_ReallocRules_TriggerHappy".Translate() + ": " + AESettings.cvTriggerHappyPenalty.ToString("F0"));
            AESettings.cvTriggerHappyPenalty = l.Slider(AESettings.cvTriggerHappyPenalty, -50f, 50f);
            l.Label("  " + "AE_ReallocRules_CarefulShooter".Translate() + ": " + AESettings.cvCarefulShooterBonus.ToString("F0"));
            AESettings.cvCarefulShooterBonus = l.Slider(AESettings.cvCarefulShooterBonus, -50f, 100f);
            l.Gap(4f);

            // ---- 保护规则 ----
            GUI.color = new Color(0.85f, 0.85f, 0.85f);
            l.Label("AE_ReallocRules_ProtectSection".Translate());
            GUI.color = Color.white;
            l.CheckboxLabeled("AE_ReallocRules_DropWeapons".Translate(), ref AESettings.reallocateDropWeapons);
            l.CheckboxLabeled("AE_ReallocRules_RespectDrafted".Translate(), ref AESettings.reallocateRespectDrafted);
            l.CheckboxLabeled("AE_ReallocRules_RespectLocked".Translate(), ref AESettings.reallocateRespectLocked);
            l.CheckboxLabeled("AE_ReallocRules_RespectBiocoded".Translate(), ref AESettings.reallocateRespectBiocoded);

            l.End();

            // 记录内容高度，供下次绘制使用
            lastHeight = l.CurHeight + 20f;

            Widgets.EndScrollView();

            // 全局重配按钮：固定面板最下方，占满宽度
            Rect buttonRect = new Rect(
                scrollRect.x,
                scrollRect.yMax + buttonGap,
                scrollRect.width,
                buttonHeight);

            if (Widgets.ButtonText(buttonRect, "AE_GlobalReallocate".Translate()))
            {
                int triggered = GlobalAllocator.ReallocateAll();
                Messages.Message(
                    "AE_GlobalReallocateResult".Translate(triggered),
                    MessageTypeDefOf.PositiveEvent);
            }
        }
    }
}
