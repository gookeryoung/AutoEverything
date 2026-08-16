using System;
using AutoEverything.AutoCarry;

namespace AutoEverything.Tests
{
    /// <summary>
    /// PawnCarryChecker 纯逻辑核心的单元测试。
    ///
    /// 覆盖范围：
    /// 1. NeedSleepCore — null（不需要睡眠）与非 null（需要睡眠）判定
    /// 2. ShouldSkipForCarryCore — 昏迷/死亡/奴隶/食尸鬼等状态跳过判定
    ///
    /// 设计原则：测试不依赖 RimWorld 运行时（无 Pawn/DrugPolicy 实例），
    /// 仅调用 internal static 纯逻辑方法验证软过滤判定。
    /// </summary>
    public static class PawnCarryCheckerTests
    {
        public static int RunAll()
        {
            int failures = 0;
            int total = 0;

            failures += RunNeedSleepTests(ref total);
            failures += RunShouldSkipForCarryTests(ref total);

            return failures;
        }

        // ════════════════════════════════════════════════════════════
        // 1. NeedSleepCore：根据 Need_Rest 是否为 null 判定
        // ════════════════════════════════════════════════════════════

        private static int RunNeedSleepTests(ref int total)
        {
            int failures = 0;

            // ── null 表示不需要睡眠 ─────────────────────────────────
            CheckNeedSleep(null, false, "null → 不需要睡眠", ref failures, ref total);

            // ── 非 null 表示需要睡眠（任意对象都视为有睡眠需求）──────
            CheckNeedSleep(new object(), true, "非 null → 需要睡眠", ref failures, ref total);

            Console.WriteLine($"[PawnCarryCheckerTests/NeedSleep] {total - failures}/{total} passed");
            return failures;
        }

        private static void CheckNeedSleep(object restNeed, bool expected, string label,
            ref int failures, ref int total)
        {
            total++;
            bool actual = PawnCarryChecker.NeedSleepCore(restNeed);
            if (actual != expected)
            {
                Console.WriteLine($"  FAIL: {label}: expected {expected}, got {actual}");
                failures++;
            }
        }

        // ════════════════════════════════════════════════════════════
        // 2. ShouldSkipForCarryCore：昏迷/死亡/奴隶/食尸鬼等状态跳过判定
        // ════════════════════════════════════════════════════════════

        private static int RunShouldSkipForCarryTests(ref int total)
        {
            int failures = 0;

            // ── 正常殖民者：不跳过 ──
            CheckSkip(isNull: false, canManageGear: true, isGhoul: false, isSlave: false,
                isDeadOrDowned: false, canBeAwake: true, shouldSkipForMedical: false, isInRitual: false,
                expected: false, "正常殖民者：不跳过", ref failures, ref total);

            // ── 昏迷状态（canBeAwake=false）：跳过 ──
            CheckSkip(isNull: false, canManageGear: true, isGhoul: false, isSlave: false,
                isDeadOrDowned: false, canBeAwake: false, shouldSkipForMedical: false, isInRitual: false,
                expected: true, "昏迷状态（CanBeAwake=false）：跳过", ref failures, ref total);

            // ── 死亡：跳过 ──
            CheckSkip(isNull: false, canManageGear: true, isGhoul: false, isSlave: false,
                isDeadOrDowned: true, canBeAwake: true, shouldSkipForMedical: false, isInRitual: false,
                expected: true, "死亡/倒下：跳过", ref failures, ref total);

            // ── 食尸鬼：跳过 ──
            CheckSkip(isNull: false, canManageGear: true, isGhoul: true, isSlave: false,
                isDeadOrDowned: false, canBeAwake: true, shouldSkipForMedical: false, isInRitual: false,
                expected: true, "食尸鬼：跳过", ref failures, ref total);

            // ── 奴隶：跳过 ──
            CheckSkip(isNull: false, canManageGear: true, isGhoul: false, isSlave: true,
                isDeadOrDowned: false, canBeAwake: true, shouldSkipForMedical: false, isInRitual: false,
                expected: true, "奴隶：跳过", ref failures, ref total);

            // ── 医疗中：跳过 ──
            CheckSkip(isNull: false, canManageGear: true, isGhoul: false, isSlave: false,
                isDeadOrDowned: false, canBeAwake: true, shouldSkipForMedical: true, isInRitual: false,
                expected: true, "医疗中：跳过", ref failures, ref total);

            // ── 仪式参与中（含心灵仪式）：跳过 ──
            CheckSkip(isNull: false, canManageGear: true, isGhoul: false, isSlave: false,
                isDeadOrDowned: false, canBeAwake: true, shouldSkipForMedical: false, isInRitual: true,
                expected: true, "仪式参与中：跳过", ref failures, ref total);

            // ── 机械族（canManageGear=false）：跳过 ──
            CheckSkip(isNull: false, canManageGear: false, isGhoul: false, isSlave: false,
                isDeadOrDowned: false, canBeAwake: true, shouldSkipForMedical: false, isInRitual: false,
                expected: true, "机械族：跳过", ref failures, ref total);

            // ── null Pawn：跳过 ──
            CheckSkip(isNull: true, canManageGear: true, isGhoul: false, isSlave: false,
                isDeadOrDowned: false, canBeAwake: true, shouldSkipForMedical: false, isInRitual: false,
                expected: true, "null Pawn：跳过", ref failures, ref total);

            Console.WriteLine($"[PawnCarryCheckerTests/ShouldSkipForCarry] {total - failures}/{total} passed");
            return failures;
        }

        private static void CheckSkip(bool isNull, bool canManageGear, bool isGhoul, bool isSlave,
            bool isDeadOrDowned, bool canBeAwake, bool shouldSkipForMedical, bool isInRitual,
            bool expected, string label, ref int failures, ref int total)
        {
            total++;
            bool actual = PawnCarryChecker.ShouldSkipForCarryCore(
                isNull, canManageGear, isGhoul, isSlave,
                isDeadOrDowned, canBeAwake, shouldSkipForMedical, isInRitual);
            if (actual != expected)
            {
                Console.WriteLine($"  FAIL: {label}: expected {expected}, got {actual}");
                failures++;
            }
        }
    }
}
