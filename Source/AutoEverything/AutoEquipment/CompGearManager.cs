using RimWorld;
using Verse;
using AutoEverything.RoleEvaluation;
using AutoEverything.Core;

namespace AutoEverything.AutoEquipment
{
    public class CompProperties_GearManager : CompProperties
    {
        public CompProperties_GearManager() { compClass = typeof(CompGearManager); }
    }

    /// <summary>
    /// 挂载在 Pawn 上的组件：作为全局 Tick 入口驱动 AutoExecutor，
    /// 并缓存角色供 ITab 显示。装备评估功能已移除。
    /// </summary>
    public class CompGearManager : ThingComp
    {
        // 缓存角色（周期性重算，供 ITab 显示）
        public Role cachedRole = Role.Default;
        private int roleCacheTick = -9999;
        private const int RoleCacheInterval = 2500;

        public Pawn Pawn => (Pawn)parent;

        public Role CurrentRole
        {
            get
            {
                int tick = Find.TickManager.TicksGame;
                if (tick - roleCacheTick > RoleCacheInterval)
                {
                    cachedRole = RoleDetector.DetectRole(Pawn);
                    roleCacheTick = tick;
                }
                return cachedRole;
            }
        }

        public override void CompTick()
        {
            if (!AESettings.enabled) return;

            // 全局自动执行（工作重配 + 人员评级 + 星标）：静态门控，每 60 tick 检查一次
            // 放在自移除检查之前：即使本 Pawn 即将被移除，全局自动执行仍应为其他殖民者运行
            AutoExecutor.TryTick();

            if (Pawn.Dead || Pawn.Downed || Pawn.Map == null) return;

            // 兜底防御：旧存档可能已注入动物/机械族等不适用类别的 Comp，
            // 在此一次性移除并返回，避免 Tick 路径持续空转
            if (!PawnSuitabilityChecker.CanManageGear(Pawn))
            {
                if (parent?.AllComps != null && parent.AllComps.Contains(this))
                    parent.AllComps.Remove(this);
                return;
            }

            // 食尸鬼（Anomaly DLC 变异体）不参与管理，静默自移除
            if (DLCCompat.IsGhoul(Pawn))
            {
                if (parent?.AllComps != null && parent.AllComps.Contains(this))
                    parent.AllComps.Remove(this);
                return;
            }

            // 定期清理已死亡/消失 Pawn 在 RoleDetector/ContextDetector 字典中的残留条目
            // 内部有门控，仅周期性执行一次实际清理
            RoleDetector.CleanupDeadPawns();
            ContextDetector.CleanupDeadPawns();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
        }

        public override string CompInspectStringExtra()
        {
            if (!(parent is Pawn)) return null;
            if (!AESettings.enabled || Pawn.Dead) return null;
            // 翻译角色名：使用 AE_Role_* 键获取本地化文本（如"射手"），避免输出枚举名"Shooter"
            return "AE_Role".Translate() + ": " + ("AE_Role_" + CurrentRole).Translate();
        }
    }
}
