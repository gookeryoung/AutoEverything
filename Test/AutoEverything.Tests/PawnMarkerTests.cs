using System;
using System.Collections.Generic;
using AutoEverything.AutoMarkPawn;
using AutoEverything.Core;
// 类型别名：PawnMarker.MarkerCategory 是嵌套类型，用别名避免到处写 PawnMarker.MarkerCategory
using MarkerCategory = AutoEverything.AutoMarkPawn.PawnMarker.MarkerCategory;

namespace AutoEverything.Tests
{
    /// <summary>
    /// PawnMarker 纯逻辑核心的单元测试。
    ///
    /// 覆盖范围：
    /// 1. GetMarkerCategoryCore — 6 类别判定 + 优先级顺序（Prisoner>Slave>Colonist>Enemy>Neutral>WildHuman）
    /// 2. FormatMessageCore — 空列表/单条/边界 maxListed/超过 maxListed 加省略号
    /// 3. ComputeNewlyMarkedIds — dedup 跟踪：首次全通知/重复不通知/resetTracking 强制全通知
    ///
    /// 设计原则：测试不依赖 RimWorld 运行时（无 Pawn/Map/Faction 实例），
    /// 仅调用 internal static 纯逻辑方法，构造 struct 输入即可验证。
    ///
    /// 注：GetMarkerColor 已删除（星标统一为深红色常量，见 HarmonyPatches.StarColor），
    /// 按类别变色逻辑不再存在，颜色测试同步移除。
    /// </summary>
    public static class PawnMarkerTests
    {
        public static int RunAll()
        {
            int failures = 0;
            int total = 0;

            failures += RunCategoryTests(ref total);
            failures += RunFormatMessageTests(ref total);
            failures += RunComputeNewlyMarkedTests(ref total);

            return failures;
        }

        // ════════════════════════════════════════════════════════════
        // 1. GetMarkerCategoryCore：6 类别判定 + 优先级顺序
        // ════════════════════════════════════════════════════════════

        private static int RunCategoryTests(ref int total)
        {
            int failures = 0;

            // ── 单一类别判定（每个类别只置 1 个 flag）────────────────
            CheckCategory(Input(prisoner: true), MarkerCategory.Prisoner, "囚犯 → Prisoner", ref failures, ref total);
            CheckCategory(Input(slave: true), MarkerCategory.Slave, "奴隶 → Slave", ref failures, ref total);
            CheckCategory(Input(colonist: true), MarkerCategory.Colonist, "殖民者 → Colonist", ref failures, ref total);
            CheckCategory(Input(hostile: true), MarkerCategory.Enemy, "敌对 → Enemy", ref failures, ref total);
            CheckCategory(Input(hasFaction: true), MarkerCategory.Neutral, "中立/盟友 → Neutral", ref failures, ref total);
            CheckCategory(Input(), MarkerCategory.WildHuman, "无派系 → WildHuman", ref failures, ref total);

            // ── 优先级顺序：Prisoner > Slave > Colonist > Enemy > Neutral > WildHuman ──
            // 同时设置多个 flag 时，优先级高的胜出
            CheckCategory(Input(prisoner: true, slave: true), MarkerCategory.Prisoner,
                "囚犯+奴隶 → Prisoner（优先级最高）", ref failures, ref total);
            CheckCategory(Input(prisoner: true, colonist: true), MarkerCategory.Prisoner,
                "囚犯+殖民者 → Prisoner", ref failures, ref total);
            CheckCategory(Input(slave: true, colonist: true), MarkerCategory.Slave,
                "奴隶+殖民者 → Slave（奴隶优先于殖民者）", ref failures, ref total);
            CheckCategory(Input(colonist: true, hostile: true), MarkerCategory.Colonist,
                "殖民者+敌对 → Colonist（殖民者优先于敌对，殖民者不会对自己敌对）", ref failures, ref total);
            CheckCategory(Input(hostile: true, hasFaction: true), MarkerCategory.Enemy,
                "敌对+有派系 → Enemy（敌对优先于中立）", ref failures, ref total);
            CheckCategory(Input(hasFaction: true, colonist: false), MarkerCategory.Neutral,
                "有派系+非殖民者 → Neutral", ref failures, ref total);

            // ── 边界：全 false → WildHuman ─────────────────────────
            CheckCategory(new PawnMarker.CategoryInput(), MarkerCategory.WildHuman,
                "全 false → WildHuman（无任何派系/状态）", ref failures, ref total);

            // ── 边界：hasFaction=true 但其他 false → Neutral ──────────
            // 注意：HasFaction=true 且非殖民者+非敌对 → Neutral（有派系但不属于玩家阵营）
            CheckCategory(Input(hasFaction: true, hostile: false, colonist: false), MarkerCategory.Neutral,
                "有派系+非殖民者+非敌对 → Neutral", ref failures, ref total);

            Console.WriteLine($"[PawnMarkerTests/Category] {total - failures}/{total} passed");
            return failures;
        }

        private static PawnMarker.CategoryInput Input(
            bool prisoner = false, bool slave = false, bool colonist = false,
            bool hostile = false, bool hasFaction = false)
        {
            return new PawnMarker.CategoryInput
            {
                IsPrisonerOfColony = prisoner,
                IsSlaveOfColony = slave,
                IsColonistFaction = colonist,
                IsHostileTo = hostile,
                HasFaction = hasFaction
            };
        }

        private static void CheckCategory(PawnMarker.CategoryInput input, PawnMarker.MarkerCategory expected,
            string label, ref int failures, ref int total)
        {
            total++;
            PawnMarker.MarkerCategory actual = PawnMarker.GetMarkerCategoryCore(input);
            if (actual != expected)
            {
                Console.WriteLine($"  FAIL: {label}: expected {expected}, got {actual}");
                failures++;
            }
        }

        // ════════════════════════════════════════════════════════════
        // 2. FormatMessageCore：消息格式化
        // ════════════════════════════════════════════════════════════

        private static int RunFormatMessageTests(ref int total)
        {
            int failures = 0;

            const string Header = "HEADER";
            const string More = "MORE";
            const string None = "NONE";

            // ── 空列表：返回 noneText ───────────────────────────────
            CheckFormat(null, Header, More, None, 8, None,
                "null entries → noneText", ref failures, ref total);
            CheckFormat(new List<PawnMarker.MessageEntry>(), Header, More, None, 8, None,
                "empty entries → noneText", ref failures, ref total);

            // ── 单条：header + 1 行 ─────────────────────────────────
            var one = new List<PawnMarker.MessageEntry>
            {
                new PawnMarker.MessageEntry { CategoryLabel = "殖民者", Name = "张三", Tier = CombatTier.SS }
            };
            CheckFormat(one, Header, More, None, 8,
                "HEADER\n- 殖民者 张三 (SS)",
                "1 entry → header + 1 line", ref failures, ref total);

            // ── 多条 ≤ maxListed：完整列表 ─────────────────────────
            var three = new List<PawnMarker.MessageEntry>
            {
                new PawnMarker.MessageEntry { CategoryLabel = "殖民者", Name = "A", Tier = CombatTier.S },
                new PawnMarker.MessageEntry { CategoryLabel = "敌对", Name = "B", Tier = CombatTier.SS },
                new PawnMarker.MessageEntry { CategoryLabel = "野生", Name = "C", Tier = CombatTier.SSS }
            };
            CheckFormat(three, Header, More, None, 8,
                "HEADER\n- 殖民者 A (S)\n- 敌对 B (SS)\n- 野生 C (SSS)",
                "3 entries (≤8) → full list", ref failures, ref total);

            // ── 恰好 maxListed 条：完整列表，不显示省略 ─────────────
            var eight = BuildEntries(8);
            string expected8 = Header;
            for (int i = 0; i < 8; i++)
                expected8 += "\n- CAT" + i + " N" + i + " (S)";
            CheckFormat(eight, Header, More, None, 8, expected8,
                "exactly 8 entries (== maxListed) → full list, no MORE", ref failures, ref total);

            // ── 超过 maxListed 条：显示前 maxListed + moreText ──────────
            var nine = BuildEntries(9);
            string expected9 = Header;
            for (int i = 0; i < 8; i++)
                expected9 += "\n- CAT" + i + " N" + i + " (S)";
            expected9 += "\nMORE";
            CheckFormat(nine, Header, More, None, 8, expected9,
                "9 entries (>8) → first 8 + MORE", ref failures, ref total);

            // ── 自定义 maxListed=2 ─────────────────────────────────
            CheckFormat(three, Header, More, None, 2,
                "HEADER\n- 殖民者 A (S)\n- 敌对 B (SS)\nMORE",
                "3 entries with maxListed=2 → first 2 + MORE", ref failures, ref total);

            // ── moreText=null + 超过 maxListed：不显示 MORE（容错）──
            CheckFormat(nine, Header, null, None, 8, expected8,
                "9 entries with moreText=null → first 8, no MORE (容错)", ref failures, ref total);

            // ── maxListed=0：列表头后直接显示 MORE（边界）──────────
            CheckFormat(three, Header, More, None, 0,
                "HEADER\nMORE",
                "3 entries with maxListed=0 → header + MORE", ref failures, ref total);

            Console.WriteLine($"[PawnMarkerTests/FormatMessage] {total - failures}/{total} passed");
            return failures;
        }

        private static List<PawnMarker.MessageEntry> BuildEntries(int count)
        {
            var list = new List<PawnMarker.MessageEntry>(count);
            for (int i = 0; i < count; i++)
            {
                list.Add(new PawnMarker.MessageEntry
                {
                    CategoryLabel = "CAT" + i,
                    Name = "N" + i,
                    Tier = CombatTier.S
                });
            }
            return list;
        }

        private static void CheckFormat(
            List<PawnMarker.MessageEntry> entries, string headerText, string moreText, string noneText,
            int maxListed, string expected, string label, ref int failures, ref int total)
        {
            total++;
            string actual = PawnMarker.FormatMessageCore(entries, headerText, moreText, noneText, maxListed);
            // Normalize 换行符：StringBuilder.AppendLine 在 Windows 追加 \r\n，统一为 \n 与测试期望对齐
            actual = actual.Replace("\r\n", "\n");
            if (actual != expected)
            {
                Console.WriteLine($"  FAIL: {label}");
                Console.WriteLine($"        expected: [{expected}]");
                Console.WriteLine($"        actual:   [{actual}]");
                failures++;
            }
        }

        // ════════════════════════════════════════════════════════════
        // 3. ComputeNewlyMarkedIds：dedup 跟踪
        // ════════════════════════════════════════════════════════════

        private static int RunComputeNewlyMarkedTests(ref int total)
        {
            int failures = 0;

            // ── 空状态：首次扫描，全部为新发现 ─────────────────────
            CheckCompute(new HashSet<int>(), ListOf(), false, ListOf(),
                "empty prev + empty current → empty new", ref failures, ref total);
            CheckCompute(new HashSet<int>(), ListOf(1, 2, 3), false, ListOf(1, 2, 3),
                "empty prev + 3 current → 3 new (首次全通知)", ref failures, ref total);

            // ── 已通知集合包含全部当前 ID：无新发现 ─────────────────
            CheckCompute(SetOf(1, 2, 3), ListOf(1, 2, 3), false, ListOf(),
                "3 prev + 3 same current → 0 new (重复不通知)", ref failures, ref total);

            // ── 部分新增：仅新 ID 被通知 ──────────────────────────
            CheckCompute(SetOf(1, 2, 3), ListOf(3, 4, 5), false, ListOf(4, 5),
                "3 prev + 5 current (3 same + 2 new) → 2 new", ref failures, ref total);

            // ── 顺序无关：previous 含 1,2,3；current 是 3,4,5 → new 是 4,5（按 current 顺序）──
            CheckCompute(SetOf(1, 2, 3), ListOf(3, 1, 5), false, ListOf(5),
                "current [3,1,5] vs prev {1,2,3} → new [5]（仅未通知的）", ref failures, ref total);

            // ── resetTracking=true：所有 current 都视为新发现（toggle on 场景）──
            CheckCompute(SetOf(1, 2, 3), ListOf(1, 2, 3), true, ListOf(1, 2, 3),
                "resetTracking=true + 3 same current → 3 new (强制全通知)", ref failures, ref total);
            CheckCompute(SetOf(1, 2, 3), ListOf(4, 5), true, ListOf(4, 5),
                "resetTracking=true + 2 new current → 2 new", ref failures, ref total);
            CheckCompute(SetOf(1, 2, 3), ListOf(), true, ListOf(),
                "resetTracking=true + empty current → empty new", ref failures, ref total);

            // ── 边界：current 包含重复 ID（理论上不应发生，但需容错）──
            CheckCompute(new HashSet<int>(), ListOf(1, 1, 2), false, ListOf(1, 1, 2),
                "empty prev + current [1,1,2] (含重复) → [1,1,2] (保留重复)", ref failures, ref total);

            Console.WriteLine($"[PawnMarkerTests/ComputeNewlyMarked] {total - failures}/{total} passed");
            return failures;
        }

        private static List<int> ListOf(params int[] values)
        {
            return new List<int>(values);
        }

        private static HashSet<int> SetOf(params int[] values)
        {
            return new HashSet<int>(values);
        }

        private static void CheckCompute(HashSet<int> previousNotified, List<int> currentIds,
            bool resetTracking, List<int> expected, string label, ref int failures, ref int total)
        {
            total++;
            List<int> actual = PawnMarker.ComputeNewlyMarkedIds(previousNotified, currentIds, resetTracking);
            if (!ListEquals(actual, expected))
            {
                Console.WriteLine($"  FAIL: {label}");
                Console.WriteLine($"        expected: [{ListToString(expected)}]");
                Console.WriteLine($"        actual:   [{ListToString(actual)}]");
                failures++;
            }
        }

        private static bool ListEquals(List<int> a, List<int> b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        private static string ListToString(List<int> list)
        {
            if (list == null) return "null";
            return string.Join(", ", list.ConvertAll(i => i.ToString()).ToArray());
        }
    }
}
