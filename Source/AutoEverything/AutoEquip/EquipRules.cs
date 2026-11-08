using System;
using System.Collections.Generic;
using System.Globalization;

namespace AutoEverything.AutoEquip
{
    /// <summary>
    /// 单个角色分组的装备白名单规则（纯数据，不依赖 RimWorld 运行时）。
    /// 顺序与 <c>RoleGroup</c> 一致：前排 / 射手 / 工人 / 贸易 / 其他。
    /// </summary>
    public struct EquipRoleRule
    {
        public bool Melee;      // 允许近战武器作为主武器
        public bool Ranged;     // 允许远程武器作为主武器
        public bool Thrown;     // 允许把投掷物（手雷/燃烧弹）放进背包
        public int Sidearms;    // 额外携带的远程武器数量（0-3）

        public bool AllowsAnyPrimary
        {
            get { return Melee || Ranged; }
        }
    }

    /// <summary>
    /// 角色装备规则的白名单解析/序列化。
    /// 存档载体用字符串 "角色#近战#远程#投掷#副武器数"，与角色分组顺序表一致，
    /// 避免新增/删除角色时数组越界，也便于旧存档缺项时自动回退默认值。
    /// </summary>
    public static class EquipRules
    {
        public const int RoleCount = 5;
        public const int MaxSidearms = 3;
        public const int MinSidearms = 0;

        /// <summary>五类角色的默认规则。射手允许远程 + 投掷 + 2 把副武器；前排默认近战。</summary>
        public static List<string> CreateDefaultEntries()
        {
            List<string> entries = new List<string>(RoleCount);
            for (int role = 0; role < RoleCount; role++)
            {
                entries.Add(Serialize(role, DefaultForRole(role)));
            }
            return entries;
        }

        public static EquipRoleRule DefaultForRole(int role)
        {
            EquipRoleRule rule = new EquipRoleRule();
            switch (role)
            {
                case 0: // 前排
                    rule.Melee = true;
                    break;
                case 1: // 射手
                    rule.Ranged = true;
                    rule.Thrown = true;
                    rule.Sidearms = 2;
                    break;
                case 4: // 其他
                    rule.Ranged = true;
                    rule.Sidearms = 1;
                    break;
                default: // 工人 / 贸易：远程自卫，不携带副武器
                    rule.Ranged = true;
                    break;
            }
            return rule;
        }

        public static EquipRoleRule GetRule(List<string> entries, int role)
        {
            if (role < 0 || role >= RoleCount) return DefaultForRole(0);
            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    int parsedRole;
                    EquipRoleRule parsed;
                    if (!TryParse(entries[i], out parsedRole, out parsed))
                        continue;
                    if (parsedRole == role) return parsed;
                }
            }
            return DefaultForRole(role);
        }

        public static void SetRule(List<string> entries, int role, EquipRoleRule rule)
        {
            if (entries == null || role < 0 || role >= RoleCount) return;

            rule.Sidearms = ClampSidearms(rule.Sidearms);
            for (int i = 0; i < entries.Count; i++)
            {
                int parsedRole;
                EquipRoleRule ignored;
                if (!TryParse(entries[i], out parsedRole, out ignored)) continue;
                if (parsedRole != role) continue;
                entries[i] = Serialize(role, rule);
                return;
            }
            entries.Add(Serialize(role, rule));
        }

        /// <summary>
        /// 加载存档后的完整性修复：补齐缺失角色、丢弃非法条目、钳制副武器数量。
        /// </summary>
        public static List<string> Normalize(List<string> entries)
        {
            List<string> result = new List<string>(RoleCount);
            for (int role = 0; role < RoleCount; role++)
            {
                EquipRoleRule rule = GetRule(entries, role);
                rule.Sidearms = ClampSidearms(rule.Sidearms);
                result.Add(Serialize(role, rule));
            }
            return result;
        }

        public static string Serialize(int role, EquipRoleRule rule)
        {
            return role.ToString(CultureInfo.InvariantCulture)
                + "#" + (rule.Melee ? "1" : "0")
                + "#" + (rule.Ranged ? "1" : "0")
                + "#" + (rule.Thrown ? "1" : "0")
                + "#" + ClampSidearms(rule.Sidearms).ToString(CultureInfo.InvariantCulture);
        }

        public static bool TryParse(string entry, out int role, out EquipRoleRule rule)
        {
            role = -1;
            rule = new EquipRoleRule();
            if (string.IsNullOrEmpty(entry)) return false;

            string[] parts = entry.Split(new char[] { '#' }, StringSplitOptions.None);
            if (parts.Length != 5) return false;
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out role))
                return false;
            if (role < 0 || role >= RoleCount) return false;

            rule.Melee = parts[1] == "1";
            rule.Ranged = parts[2] == "1";
            rule.Thrown = parts[3] == "1";

            int sidearms;
            if (!int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out sidearms))
                return false;
            rule.Sidearms = ClampSidearms(sidearms);
            return true;
        }

        public static int ClampSidearms(int value)
        {
            return Math.Max(MinSidearms, Math.Min(MaxSidearms, value));
        }
    }
}
