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
            size = new Vector2(300f, 450f);
        }

        public override bool IsVisible
        {
            get
            {
                Pawn pawn = SelPawn as Pawn;

                return pawn != null
                    && pawn.Faction == Faction.OfPlayer
                    && !DLCCompat.IsGhoul(pawn); // 食尸鬼不显示装备管理面板
            }
        }

        protected override void FillTab()
        {
            Pawn pawn = SelPawn as Pawn;

            if (pawn == null) return;

            var comp = pawn.GetComp<CompGearManager>();
            if (comp == null) return;

            Rect rect = new Rect(0f, 0f, size.x, size.y).ContractedBy(10f);
            Rect scrollRect = rect;
            scrollRect.height -= 20f; // 为锁定开关预留空间

            Rect contentRect = new Rect(0f, 0f, scrollRect.width - 16f, lastHeight);

            Widgets.BeginScrollView(scrollRect, ref scrollPos, contentRect);

            Listing_Standard l = new Listing_Standard();
            l.Begin(contentRect);

            // 标题
            Text.Font = GameFont.Medium;
            l.Label("AE_TabTitle".Translate());
            Text.Font = GameFont.Small;
            l.Gap();

            // 当前角色
            Role role = comp.CurrentRole;
            l.Label("AE_CurrentRole".Translate() + ": " + ("AE_Role_" + role).Translate());

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

            // 当前装备
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

            l.End();

            lastHeight = contentRect.height;

            Widgets.EndScrollView();
        }
    }
}
