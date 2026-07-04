using RimWorld;
using UnityEngine;
using Verse;
using AutoEverything.Core;
using AutoEverything.Allocation;

namespace AutoEverything.UI
{
    /// <summary>
    /// 全局重配规则确认对话框。
    /// 设计意图：将可调规则从检视面板剥离，避免 ScrollView 过长；
    /// 玩家点击"全局重配"按钮后弹出此窗，调整规则后点"确认重配"才真正执行。
    /// 窗口尺寸自适应屏幕，内容用 ScrollView 包裹防止溢出。
    /// </summary>
    public class Dialog_GlobalReallocate : Window
    {
        // 静态滚动位置：避免每次重开窗口回到顶部
        private static Vector2 scrollPos;
        // 初始值给充足高度，避免首帧内容被截断
        // 实际内容在 DoWindowContents 末尾会重新计算并覆盖
        // static：与 scrollPos 一致，多次开关窗口保留高度记忆
        private static float contentHeight = 900f;

        public override Vector2 InitialSize
        {
            get
            {
                // 自适应屏幕高度：取屏幕高度的 85%，最小 540f，最大 900f
                // 宽度 480f 容纳 Slider 标签与数值
                float screenHeight = Verse.UI.screenHeight;
                float height = UnityEngine.Mathf.Clamp(screenHeight * 0.85f, 540f, 900f);
                return new Vector2(480f, height);
            }
        }

        public Dialog_GlobalReallocate()
        {
            // 必须设置：模态对话框，暂停游戏背景可点
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            forcePause = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            // 顶部标题
            Text.Font = GameFont.Medium;
            bool prevWrap = Text.WordWrap;
            Text.WordWrap = false;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 32f), "AE_ReallocRules_Title".Translate());
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(0f, 32f, inRect.width, 20f), "AE_ReallocRules_ConfirmDesc".Translate());
            Text.WordWrap = prevWrap;
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            // 底部按钮区预留
            float buttonHeight = 34f;
            float buttonGap = 8f;
            float topReserve = 58f;
            float bottomReserve = buttonHeight + buttonGap + 6f;

            Rect scrollRect = new Rect(0f, topReserve, inRect.width, inRect.height - topReserve - bottomReserve);
            Rect contentRect = new Rect(0f, 0f, scrollRect.width - 16f, contentHeight);

            Widgets.BeginScrollView(scrollRect, ref scrollPos, contentRect);

            Listing_Standard l = new Listing_Standard();
            l.Begin(contentRect);

            // ===================== 战斗价值公式 =====================
            GUI.color = new Color(0.85f, 0.85f, 0.85f);
            l.Label("AE_ReallocRules_CombatValue".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.75f, 0.75f, 0.75f);
            l.Label("AE_ReallocRules_CombatValue_Formula".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            l.Gap(4f);

            l.Label("AE_ReallocRules_PassionMult".Translate());
            l.Label("  " + "AE_ReallocRules_PassionNone".Translate() + ": " + AESettings.cvPassionNoneMult.ToString("F1"));
            AESettings.cvPassionNoneMult = l.Slider(AESettings.cvPassionNoneMult, 0.5f, 3.0f);
            l.Label("  " + "AE_ReallocRules_PassionMinor".Translate() + ": " + AESettings.cvPassionMinorMult.ToString("F1"));
            AESettings.cvPassionMinorMult = l.Slider(AESettings.cvPassionMinorMult, 0.5f, 3.0f);
            l.Label("  " + "AE_ReallocRules_PassionMajor".Translate() + ": " + AESettings.cvPassionMajorMult.ToString("F1"));
            AESettings.cvPassionMajorMult = l.Slider(AESettings.cvPassionMajorMult, 0.5f, 3.0f);
            l.Gap(4f);

            l.Label("AE_ReallocRules_SkillWeight".Translate() + ": " + AESettings.cvSkillWeight.ToString("F2"));
            AESettings.cvSkillWeight = l.Slider(AESettings.cvSkillWeight, 0.5f, 2.0f);
            l.Gap(8f);

            // ===================== 特质加分 =====================
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
            l.Gap(8f);

            // ===================== 保护规则 =====================
            GUI.color = new Color(0.85f, 0.85f, 0.85f);
            l.Label("AE_ReallocRules_ProtectSection".Translate());
            GUI.color = Color.white;
            l.CheckboxLabeled("AE_ReallocRules_DropWeapons".Translate(), ref AESettings.reallocateDropWeapons);
            l.CheckboxLabeled("AE_ReallocRules_RespectDrafted".Translate(), ref AESettings.reallocateRespectDrafted);
            l.CheckboxLabeled("AE_ReallocRules_RespectLocked".Translate(), ref AESettings.reallocateRespectLocked);
            l.CheckboxLabeled("AE_ReallocRules_RespectBiocoded".Translate(), ref AESettings.reallocateRespectBiocoded);
            l.Gap(8f);

            // ===================== 护甲分配规则 =====================
            GUI.color = new Color(0.85f, 0.85f, 0.85f);
            l.Label("AE_ReallocRules_ArmorPrefSection".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            l.Label("AE_ReallocRules_ArmorPref_Desc".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            l.CheckboxLabeled("AE_ReallocRules_ReallocApparel".Translate(), ref AESettings.reallocateApparel);

            l.End();
            contentHeight = l.CurHeight + 20f;

            Widgets.EndScrollView();

            // ===================== 底部按钮：确认重配 / 取消 =====================
            Rect btnRow = new Rect(0f, inRect.height - buttonHeight, inRect.width, buttonHeight);
            float btnWidth = (btnRow.width - 8f) * 0.5f;
            // 确认按钮（左侧）
            if (Widgets.ButtonText(new Rect(btnRow.x, btnRow.y, btnWidth, buttonHeight),
                                   "AE_ReallocRules_Confirm".Translate()))
            {
                int triggered = GlobalAllocator.ReallocateAll();
                Messages.Message(
                    "AE_GlobalReallocateResult".Translate(triggered),
                    MessageTypeDefOf.PositiveEvent);
                Close();
            }
            // 取消按钮（右侧）
            if (Widgets.ButtonText(new Rect(btnRow.x + btnWidth + 8f, btnRow.y, btnWidth, buttonHeight),
                                   "AE_ReallocRules_Cancel".Translate()))
            {
                Close();
            }
        }
    }
}