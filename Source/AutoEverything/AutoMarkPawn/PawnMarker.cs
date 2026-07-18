using RimWorld;
using Verse;
using AutoEverything.Core;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.AutoMarkPawn
{
    /// <summary>
    /// 高价值非殖民者标记模块：为 S+ 档次的访客、敌人、交易者、野生人类等
    /// 在头顶实时绘制鲜艳红色星标 "★"。
    ///
    /// 设计目的：
    /// - 玩家一眼可辨高价值非殖民者（S/SS/SSS 档），便于优先俘虏、招募或警惕
    /// - 不修改任何 Pawn 的 Nick/Name，避免影响派系交互界面、交易显示、战斗通知
    /// - 视觉标记纯前端绘制（Harmony Postfix），安全可逆，无存档副作用
    ///
    /// 标记范围（非殖民者人类）：
    /// - 敌对派系敌人（来袭突袭/袭营的敌方 Pawn）
    /// - 友好派系访客（来访的 Visitor）
    /// - 交易者（派系/轨道交易商）
    /// - 野生人类/难民/流浪者
    ///
    /// 视觉实现：
    /// - HarmonyPatches.PawnUIOverlay_DrawPawnGUIOverlay_Patch 在 DrawPawnGUIOverlay Postfix 中绘制
    /// - 头顶位置：pawn.DrawPos 上方，世界坐标转屏幕坐标
    /// - 颜色：Color(1.0f, 0.15f, 0.15f) 鲜艳红色
    ///
    /// 触发方式：
    /// - 实时绘制：Harmony 补丁每帧调用（DrawPawnGUIOverlay 由游戏每帧触发）
    /// - 消息提示：ITab 勾选时统计当前非殖民者高价值对象数量并弹消息
    ///
    /// 缓存：评级查询统一走 <see cref="TierCacheService"/>（2500 tick TTL，自动 cleanup），
    /// 不再自维护缓存字典，避免与 AESettings.Sort/WorkAllocator 重复实现。
    /// </summary>
    public static class PawnMarker
    {
        /// <summary>
        /// 判断 Pawn 是否为高价值（S+ 档次，含自定义评级覆盖）。
        /// CombatEvaluator.GetCombatTier 优先返回自定义评级，未命中则返回自动判定。
        /// 评级查询走 TierCacheService（2500 tick 缓存），避免每帧重算。
        /// </summary>
        public static bool IsHighValue(Pawn pawn)
        {
            if (pawn == null) return false;
            return TierCacheService.GetTier(pawn) >= CombatTier.S;
        }

        /// <summary>
        /// 判断 Pawn 是否为可标记的非殖民者对象：
        /// - 已生成在地图上（Spawned）
        /// - 未死亡（Dead）
        /// - 非玩家阵营（Faction != OfPlayer），涵盖敌人/访客/交易者/野生人类
        /// - 人类like 种族（CanManageGear），排除动物/机械族/昆虫
        ///
        /// 倒下（Downed）的 Pawn 仍标记：便于玩家优先俘虏高价值敌人。
        /// 食尸鬼不排除：非殖民者食尸鬼也按评级标记供玩家参考。
        /// </summary>
        public static bool IsMarkableTarget(Pawn pawn)
        {
            if (pawn == null) return false;
            if (!pawn.Spawned) return false;
            if (pawn.Dead) return false;
            if (pawn.Faction == Faction.OfPlayer) return false;
            if (!PawnSuitabilityChecker.CanManageGear(pawn)) return false;
            return true;
        }

        /// <summary>
        /// 统计当前所有地图上非殖民者高价值对象数量。
        /// 供 ITab 勾选时消息提示用，周期路径不调用（避免扫描开销）。
        /// </summary>
        public static int CountMarkablePawns()
        {
            int count = 0;
            foreach (Map map in Find.Maps)
            {
                if (map == null) continue;
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (!IsMarkableTarget(pawn)) continue;
                    if (IsHighValue(pawn)) count++;
                }
            }
            return count;
        }
    }
}
