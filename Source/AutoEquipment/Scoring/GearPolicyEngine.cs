using System.Collections.Generic;
using RimWorld;
using Verse;
using AutoEquipment.Scoring;

namespace AutoEquipment
{
    /// <summary>
    /// 装备策略调度层：根据全局预设方案与角色/情境返回对应权重。
    /// 设计模式：策略模式——不同预设方案是不同的策略实现。
    /// </summary>
    public static class GearPolicyEngine
    {
        /// <summary>
        /// 当前全局预设方案。
        /// 默认 Standard，玩家可在设置界面切换。
        /// </summary>
        public static GearPreset ActivePreset { get; private set; } = GearPreset.Standard;

        /// <summary>
        /// 玩家自定义权重覆盖（0 表示用预设默认）。
        /// 玩家可通过权重微调弹窗调整单个权重。
        /// </summary>
        private static GearWeights customOverride = new GearWeights
        {
            w_skill = 0f,
            w_passionMinor = 0f,
            w_passionMajor = 0f,
            w_dps = 0f,
            w_dmg = 0f,
            w_cooldown = 0f,
            w_range = 0f,
            w_quality = 0f,
            w_armor = 0f,
            w_insulation = 0f,
            w_movespeed = 0f,
            w_workspeed = 0f,
            upgradeThreshold = 0f
        };

        /// <summary>
        /// Pawn 级角色覆盖：thingID -> 角色。
        /// 玩家可在 ITab 中对单个 Pawn 临时覆盖角色。
        /// </summary>
        private static readonly Dictionary<int, Role> pawnRoleOverrides = new Dictionary<int, Role>();

        /// <summary>
        /// 切换全局预设方案。
        /// </summary>
        public static void SwitchPreset(GearPreset preset)
        {
            if (ActivePreset == preset) return;
            ActivePreset = preset;
            Log.Message($"[AutoEquipment] 全局方案切换: {preset}");
        }

        /// <summary>
        /// 设置自定义权重覆盖值。
        /// 字段名为 0 表示用预设默认。
        /// </summary>
        public static void SetCustomOverride(GearWeights custom)
        {
            customOverride = custom;
        }

        /// <summary>
        /// 设置 Pawn 级角色覆盖。
        /// </summary>
        public static void SetPawnRoleOverride(Pawn pawn, Role role)
        {
            if (pawn == null) return;
            pawnRoleOverrides[pawn.thingIDNumber] = role;
        }

        /// <summary>
        /// 清除 Pawn 级角色覆盖。
        /// </summary>
        public static void ClearPawnRoleOverride(Pawn pawn)
        {
            if (pawn == null) return;
            pawnRoleOverrides.Remove(pawn.thingIDNumber);
        }

        /// <summary>
        /// 解析 Pawn 实际应用的角色（考虑覆盖）。
        /// </summary>
        public static Role ResolveRole(Pawn pawn)
        {
            if (pawn == null) return Role.Default;

            if (pawnRoleOverrides.TryGetValue(pawn.thingIDNumber, out Role overrideRole))
                return overrideRole;

            return RoleDetector.DetectRole(pawn);
        }

        /// <summary>
        /// 获取角色+情境对应的评分权重。
        /// 优先级：自定义覆盖 > 预设方案。
        /// </summary>
        public static GearWeights GetWeights(Role role, GearContext context)
        {
            GearWeights baseWeights = ActivePreset.GetDefaultWeights();

            // 应用自定义覆盖（非零字段覆盖默认值）
            if (customOverride.w_skill > 0f) baseWeights.w_skill = customOverride.w_skill;
            if (customOverride.w_passionMinor > 0f) baseWeights.w_passionMinor = customOverride.w_passionMinor;
            if (customOverride.w_passionMajor > 0f) baseWeights.w_passionMajor = customOverride.w_passionMajor;
            if (customOverride.w_dps > 0f) baseWeights.w_dps = customOverride.w_dps;
            if (customOverride.w_dmg > 0f) baseWeights.w_dmg = customOverride.w_dmg;
            if (customOverride.w_cooldown > 0f) baseWeights.w_cooldown = customOverride.w_cooldown;
            if (customOverride.w_range > 0f) baseWeights.w_range = customOverride.w_range;
            if (customOverride.w_quality > 0f) baseWeights.w_quality = customOverride.w_quality;
            if (customOverride.w_armor > 0f) baseWeights.w_armor = customOverride.w_armor;
            if (customOverride.w_insulation > 0f) baseWeights.w_insulation = customOverride.w_insulation;
            if (customOverride.w_movespeed > 0f) baseWeights.w_movespeed = customOverride.w_movespeed;
            if (customOverride.w_workspeed > 0f) baseWeights.w_workspeed = customOverride.w_workspeed;
            if (customOverride.upgradeThreshold > 0f) baseWeights.upgradeThreshold = customOverride.upgradeThreshold;

            return baseWeights;
        }

        /// <summary>
        /// 存档持久化：保存预设方案与自定义权重。
        /// </summary>
        public static void ExposeData()
        {
            GearPreset preset = ActivePreset;
            Scribe_Values.Look(ref preset, "ae_activePreset", GearPreset.Standard);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                ActivePreset = preset;
            }

            // 自定义权重存档
            float w_skill = customOverride.w_skill;
            float w_passionMinor = customOverride.w_passionMinor;
            float w_passionMajor = customOverride.w_passionMajor;
            float w_dps = customOverride.w_dps;
            float w_dmg = customOverride.w_dmg;
            float w_cooldown = customOverride.w_cooldown;
            float w_range = customOverride.w_range;
            float w_quality = customOverride.w_quality;
            float w_armor = customOverride.w_armor;
            float w_insulation = customOverride.w_insulation;
            float w_movespeed = customOverride.w_movespeed;
            float w_workspeed = customOverride.w_workspeed;

            Scribe_Values.Look(ref w_skill, "ae_w_skill", 0f);
            Scribe_Values.Look(ref w_passionMinor, "ae_w_passionMinor", 0f);
            Scribe_Values.Look(ref w_passionMajor, "ae_w_passionMajor", 0f);
            Scribe_Values.Look(ref w_dps, "ae_w_dps", 0f);
            Scribe_Values.Look(ref w_dmg, "ae_w_dmg", 0f);
            Scribe_Values.Look(ref w_cooldown, "ae_w_cooldown", 0f);
            Scribe_Values.Look(ref w_range, "ae_w_range", 0f);
            Scribe_Values.Look(ref w_quality, "ae_w_quality", 0f);
            Scribe_Values.Look(ref w_armor, "ae_w_armor", 0f);
            Scribe_Values.Look(ref w_insulation, "ae_w_insulation", 0f);
            Scribe_Values.Look(ref w_movespeed, "ae_w_movespeed", 0f);
            Scribe_Values.Look(ref w_workspeed, "ae_w_workspeed", 0f);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                customOverride = new GearWeights
                {
                    w_skill = w_skill,
                    w_passionMinor = w_passionMinor,
                    w_passionMajor = w_passionMajor,
                    w_dps = w_dps,
                    w_dmg = w_dmg,
                    w_cooldown = w_cooldown,
                    w_range = w_range,
                    w_quality = w_quality,
                    w_armor = w_armor,
                    w_insulation = w_insulation,
                    w_movespeed = w_movespeed,
                    w_workspeed = w_workspeed
                };
            }
        }
    }
}
