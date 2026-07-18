using System.Collections.Generic;
using RimWorld;
using Verse;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.Core
{
    /// <summary>
    /// AESettings 的评级标签部分（partial）。
    /// 职责：殖民者 Nick 评级前缀的"应用/清除/排序"逻辑，以及殖民者栏重排。
    /// 持久化字段（tierTagOriginals / tierTagOriginalEntries）的 Scribe 读写仍在主文件 ExposeData 中，
    /// 此处仅提供运行时操作方法。
    /// </summary>
    public partial class AESettings
    {
        // ===================== 全局评级标签 =====================
        // 设计：玩家点击"全局人物评级"按钮后，自动给所有殖民者的 Nick 加上系统评级前缀，
        //   格式："S#王五"（系统档 S + # + 原 Nick）
        //   自定义评级时仍按系统档计算前缀（与 AEDebug.Label 不同，此处只显示系统档）
        // 用 thingIDNumber → 原 Nick 映射保存原名，便于恢复
        // 持久化：通过 tierTagOriginalEntries 存档，避免重启后丢失导致误剥离玩家手动改的 Nick
        private static readonly Dictionary<int, string> tierTagOriginals = new Dictionary<int, string>();
        // 存档载体：List<string> 格式 "thingIDNumber|原Nick"，加载后重建字典
        private static List<string> tierTagOriginalEntries;
        private const string TIER_TAG_PREFIX_SEPARATOR = "#";

        // 殖民者栏排序 Role 缓存：Role 不在 TierCacheService 范围，仍需独立预计算
        // Tier/Value 缓存统一走 TierCacheService（排序前 Invalidate 保证最新）
        private static readonly Dictionary<Pawn, int> sortRoleCache = new Dictionary<Pawn, int>();

        /// <summary>
        /// 给所有玩家殖民者（含食尸鬼）的 Nick 加上系统评级前缀，格式 "S#王五"。
        /// 已加过前缀的会先剥离再重加，保证评级最新。
        /// 跳过动物、机械族、奴隶、未成年。
        /// 注：食尸鬼也按相同规则评级，但不参与装备分配——玩家可一眼分辨其价值。
        /// </summary>
        public static int ApplyTierTagsToAllPawns()
        {
            int touched = 0;
            // 收集所有需要评级的 Pawn：殖民者 + 食尸鬼（统一走 PawnCollector，避免两处收集逻辑重复）
            List<Pawn> pawns = PawnCollector.AllManagedPawns();

            foreach (Pawn pawn in pawns)
            {
                if (!PawnSuitabilityChecker.CanManageGear(pawn)) continue;
                // 注：食尸鬼不再排除——用户要求食尸鬼也评级

                NameTriple nt = pawn.Name as NameTriple;
                if (nt == null) continue;

                string currentNick = nt.Nick ?? string.Empty;
                // 剥离已有评级前缀，得到"纯净名"
                string cleanNick = TierTagHelper.Strip(currentNick);

                // 首次应用：保存原名到字典（若已存在则保留最早的）
                int pid = pawn.thingIDNumber;
                if (!tierTagOriginals.ContainsKey(pid))
                {
                    tierTagOriginals[pid] = cleanNick;
                }
                else
                {
                    // 已保存过原名，使用保存值作为"纯净名"
                    cleanNick = tierTagOriginals[pid];
                }

                // 计算当前系统评级（含配偶豁免，不含自定义评级覆盖）
                CombatTier tier = CombatEvaluator.GetSystemTier(pawn);
                string newNick = tier + TIER_TAG_PREFIX_SEPARATOR + cleanNick;

                if (newNick != currentNick)
                {
                    pawn.Name = new NameTriple(nt.First, newNick, nt.Last);
                    touched++;
                }
            }
            return touched;
        }

        /// <summary>
        /// 清除所有殖民者（含食尸鬼）Nick 上的评级前缀，恢复原名。
        /// 优先从 tierTagOriginals 取原名；若字典无（重启后），尝试从 Nick 解析剥离。
        /// </summary>
        public static int ClearTierTagsFromAllPawns()
        {
            int touched = 0;
            // 收集殖民者 + 食尸鬼，与 Apply 保持一致（统一走 PawnCollector）
            List<Pawn> pawns = PawnCollector.AllManagedPawns();

            foreach (Pawn pawn in pawns)
            {
                NameTriple nt = pawn.Name as NameTriple;
                if (nt == null) continue;

                string currentNick = nt.Nick ?? string.Empty;
                if (!TierTagHelper.HasPrefix(currentNick)) continue;

                int pid = pawn.thingIDNumber;
                string cleanNick;
                if (tierTagOriginals.TryGetValue(pid, out string saved))
                {
                    cleanNick = saved;
                }
                else
                {
                    cleanNick = TierTagHelper.Strip(currentNick);
                }

                if (cleanNick != currentNick)
                {
                    pawn.Name = new NameTriple(nt.First, cleanNick, nt.Last);
                    touched++;
                }
            }
            tierTagOriginals.Clear();
            SyncTierTagOriginalEntries();
            return touched;
        }

        /// <summary>
        /// 应用评级前缀并按评级降序重排殖民者栏。
        /// 排序规则：先按 CombatTier 降序（S→A→B→C→D→X），同档内按 ComputeCombatValue 降序。
        /// S 档殖民者排在殖民者栏左侧，X 档（和平主义者等）排在最右。
        /// </summary>
        public static int ApplyTierTagsAndSortByValue()
        {
            int touched = ApplyTierTagsToAllPawns();
            ReorderColonistBar(ComparePawnByTierThenValueDesc);
            return touched;
        }

        /// <summary>
        /// 应用评级前缀并按角色分组重排殖民者栏。
        /// 角色顺序：Brawler → Shooter → Doctor → Worker → Pacifist → Hunter → Leader → Default。
        /// 同角色内按评级降序（S→A→B→C→D→X），同档内按战斗价值降序。
        /// </summary>
        public static int ApplyTierTagsAndSortByRole()
        {
            int touched = ApplyTierTagsToAllPawns();
            ReorderColonistBar(ComparePawnByRoleThenValueDesc);
            return touched;
        }

        /// <summary>
        /// 应用评级前缀并按 Mod 选项配置的默认排序重排殖民者栏。
        /// 玩家在 Mod 选项里配置 defaultSortMode，ITab "全局人物评级"按钮调用此方法。
        /// None 模式下仅应用前缀不重排。
        /// </summary>
        public static int ApplyTierTagsWithDefaultSort()
        {
            int touched = ApplyTierTagsToAllPawns();
            switch (defaultSortMode)
            {
                case ColonistBarSortMode.None:
                    // 不重排：保留殖民者栏原顺序
                    break;
                case ColonistBarSortMode.ByTierThenValue:
                    ReorderColonistBar(ComparePawnByTierThenValueDesc);
                    break;
                case ColonistBarSortMode.ByRoleThenTier:
                    ReorderColonistBar(ComparePawnByRoleThenValueDesc);
                    break;
                case ColonistBarSortMode.ByCombatValue:
                    ReorderColonistBar(ComparePawnByCombatValueOnlyDesc);
                    break;
            }
            return touched;
        }

        /// <summary>
        /// 重排殖民者栏：按比较器排序后写入 displayOrder，刷新殖民者栏。
        /// RimWorld 殖民者栏顺序由 Pawn.playerSettings.displayOrder 决定，
        /// 修改后调 Find.ColonistBar.MarkColonistsDirty() 刷新缓存。
        /// 注意：displayOrder 仅在同一 map/caravan group 内有效，跨 group 顺序由地图/旅商顺序决定。
        /// </summary>
        private static void ReorderColonistBar(System.Comparison<Pawn> comparison)
        {
            var pawns = new List<Pawn>();
            foreach (Pawn pawn in PawnsFinder.AllMaps_FreeColonists)
            {
                if (pawn != null && pawn.playerSettings != null)
                    pawns.Add(pawn);
            }

            // 玩家点按钮触发，需立即反映当前评级/价值：失效 TierCacheService 中相关 Pawn 的缓存
            // Role 不在 TierCacheService 范围，仍需独立预计算
            for (int i = 0; i < pawns.Count; i++)
                TierCacheService.Invalidate(pawns[i]);

            sortRoleCache.Clear();
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                sortRoleCache[p] = GetRoleOrder(RoleDetector.DetectRole(p));
            }

            pawns.Sort(comparison);

            for (int i = 0; i < pawns.Count; i++)
            {
                pawns[i].playerSettings.displayOrder = i;
            }

            Find.ColonistBar.MarkColonistsDirty();
        }

        /// <summary>
        /// 评级降序比较器：先按 CombatTier 降序（S→A→B→C→D→X），
        /// 同档内再按 ComputeCombatValue 降序。
        /// 设计意图：和平主义者（X 档）即使技能高也排在最右，
        /// 避免高技能的 X 档殖民者挤占 S/A 档位置。
        /// </summary>
        private static int ComparePawnByTierThenValueDesc(Pawn a, Pawn b)
        {
            // CombatTier 枚举值：X=0, D=1, C=2, B=3, A=4, S=5，降序即 S 在前
            // ReorderColonistBar 调用前已 Invalidate 这两个 Pawn，首次访问会重算并缓存
            CombatTier ta = TierCacheService.GetTier(a);
            CombatTier tb = TierCacheService.GetTier(b);
            if (ta != tb) return tb.CompareTo(ta);
            return TierCacheService.GetValue(b).CompareTo(TierCacheService.GetValue(a));
        }

        /// <summary>
        /// 仅按战斗价值降序比较器：不区分评级，纯按 ComputeCombatValue 排序。
        /// 注意：高技能的和平主义者（X 档）可能挤占 S/A 档前列位置，
        /// 仅适用于玩家明确希望"纯战斗价值优先"的场景。
        /// </summary>
        private static int ComparePawnByCombatValueOnlyDesc(Pawn a, Pawn b)
        {
            return TierCacheService.GetValue(b).CompareTo(TierCacheService.GetValue(a));
        }

        /// <summary>
        /// 角色优先级 + 评级降序比较器。
        /// 角色顺序：Brawler(0) → Shooter(1) → Doctor(2) → Worker(3) → Pacifist(4) → Hunter(5) → Leader(6) → Default(99)。
        /// 同角色内按评级降序（S→A→B→C→D→X），同档内按战斗价值降序。
        /// </summary>
        private static int ComparePawnByRoleThenValueDesc(Pawn a, Pawn b)
        {
            int ra = sortRoleCache[a];
            int rb = sortRoleCache[b];
            if (ra != rb) return ra.CompareTo(rb);
            return ComparePawnByTierThenValueDesc(a, b);
        }

        /// <summary>
        /// 获取角色排序优先级：数字小的排前面。
        /// 顺序设计：前排战斗（Brawler/Shooter）→ 后勤（Doctor/Worker/Pacifist）→ 其他（Hunter/Leader）→ 默认。
        /// </summary>
        private static int GetRoleOrder(Role role)
        {
            switch (role)
            {
                case Role.Brawler: return 0;
                case Role.Shooter: return 1;
                case Role.Doctor: return 2;
                case Role.Worker: return 3;
                case Role.Pacifist: return 4;
                case Role.Hunter: return 5;
                case Role.Leader: return 6;
                default: return 99;
            }
        }

        /// <summary>
        /// 获取排序模式的可读标签（用于设置界面按钮与 FloatMenu 选项）。
        /// </summary>
        private static string GetSortModeLabel(ColonistBarSortMode mode)
        {
            return ("AE_SortMode_" + mode).Translate();
        }

        /// <summary>
        /// 把 tierTagOriginals 字典同步到存档载体 tierTagOriginalEntries。
        /// 在 Apply/Clear 后调用，确保下次存档时持久化最新状态。
        /// </summary>
        private static void SyncTierTagOriginalEntries()
        {
            tierTagOriginalEntries.Clear();
            foreach (var kvp in tierTagOriginals)
            {
                // 格式 "thingIDNumber|原Nick"，原 Nick 不应含 | 字符（RimWorld Nick 通常不含）
                tierTagOriginalEntries.Add(kvp.Key + "|" + kvp.Value);
            }
        }

        /// <summary>
        /// 检查 Nick 是否已有评级前缀（格式：单字母 + #）。
        /// 公开供 AEDebug.Label 调用，避免 Nick 已被"全局人物评级"按钮改名后
        /// 再次拼接出 "S#S#王五" 双重前缀。
        /// </summary>
        public static bool HasTierTagPrefixOnLabel(string label)
        {
            return TierTagHelper.HasPrefix(label);
        }
    }
}
