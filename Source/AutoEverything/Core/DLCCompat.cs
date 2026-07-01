using System;
using RimWorld;
using Verse;

namespace AutoEverything.Core
{
    /// <summary>
    /// DLC 兼容性检测工具：集中包装所有 DLC 特定 API，
    /// 避免未加载 DLC 时直接调用导致 TypeLoadException。
    /// </summary>
    public static class DLCCompat
    {
        // 缓存 DLC 加载状态，避免每次访问 ModsConfig 反复读取
        private static readonly bool Anomaly = ModsConfig.AnomalyActive;
        private static readonly bool Biotech = ModsConfig.BiotechActive;
        private static readonly bool Ideology = ModsConfig.IdeologyActive;

        // 异常去重 ID 前缀：每个方法独立偏移，避免不同方法异常互相覆盖
        private const int GhoulErrorIdBase = 0xA100;
        private const int SlaveErrorIdBase = 0xA200;
        private const int ChildErrorIdBase = 0xA300;
        private const int IdeologyErrorIdBase = 0xA400;

        /// <summary>
        /// 判断是否为食尸鬼（Anomaly DLC 的变异体）。
        /// 食尸鬼无法使用武器与装备，必须完全排除在装备管理外。
        /// </summary>
        public static bool IsGhoul(Pawn pawn)
        {
            if (!Anomaly || pawn == null) return false;
            try { return pawn.IsGhoul; }
            catch (Exception ex)
            {
                // 规则要求：异常用 Log.ErrorOnce 防重复，不能静默吞异常
                // 食尸鬼判定失败可能导致食尸鬼被错误注入 Comp，违反设计原则
                Log.ErrorOnce("[AutoEverything] DLCCompat.IsGhoul 异常: " + ex.Message,
                    (pawn?.thingIDNumber ?? 0) ^ GhoulErrorIdBase);
                return false;
            }
        }

        /// <summary>
        /// 判断是否为奴隶（Biotech DLC 引入的 FromDlc 概念）。
        /// </summary>
        public static bool IsSlave(Pawn pawn)
        {
            if (!Biotech || pawn == null) return false;
            try { return pawn.IsSlave; }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] DLCCompat.IsSlave 异常: " + ex.Message,
                    (pawn?.thingIDNumber ?? 0) ^ SlaveErrorIdBase);
                return false;
            }
        }

        /// <summary>
        /// 判断是否为非成年（Biotech DLC 的发育阶段）。
        /// </summary>
        public static bool IsChild(Pawn pawn)
        {
            if (!Biotech || pawn == null) return false;
            try { return !pawn.DevelopmentalStage.Adult(); }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] DLCCompat.IsChild 异常: " + ex.Message,
                    (pawn?.thingIDNumber ?? 0) ^ ChildErrorIdBase);
                return false;
            }
        }

        /// <summary>
        /// 查询文化对武器的态度（需 Ideology DLC）。
        /// 返回值：0=None（无态度）, 1=Noble（尊崇）, 2=Despised（鄙夷）。
        /// 未加载 DLC / Pawn 无 Ideo / 查询失败时返回 0。
        /// 用 int 而非 IdeoWeaponDisposition 枚举，避免调用方依赖 DLC 类型。
        /// </summary>
        public static int GetWeaponDisposition(Pawn pawn, ThingDef weaponDef)
        {
            if (!Ideology || pawn == null || weaponDef == null) return 0;
            if (pawn.Ideo == null) return 0;
            try
            {
                // 遍历所有戒律，筛选武器戒律，取首个非 None 态度
                // 多个 Precept_Weapon 通常不会对同一武器给出冲突态度，取首个即可
                // 用 PreceptsListForReading（List<Precept>）避免 IEnumerable 索引限制
                var precepts = pawn.Ideo.PreceptsListForReading;
                for (int i = 0; i < precepts.Count; i++)
                {
                    if (!(precepts[i] is Precept_Weapon weaponPrecept)) continue;
                    var disposition = weaponPrecept.GetDispositionForWeapon(weaponDef);
                    if (disposition != IdeoWeaponDisposition.None)
                        return (int)disposition;
                }
                return 0;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[AutoEverything] DLCCompat.GetWeaponDisposition 异常: " + ex.Message,
                    pawn.thingIDNumber ^ weaponDef.shortHash ^ IdeologyErrorIdBase);
                return 0;
            }
        }
    }
}
