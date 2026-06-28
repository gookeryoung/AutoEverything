using Verse;

namespace AutoEquipment.Scoring.Weapon
{
    /// <summary>
    /// 武器射程查询工具：用 for 循环替代 LINQ FirstOrDefault，避免 Tick 路径分配闭包。
    /// </summary>
    public static class WeaponRangeHelper
    {
        /// <summary>
        /// 获取武器的最大射程。
        /// 遍历 Verbs 列表取第一个有效射程。
        /// </summary>
        public static float GetRange(Thing weapon)
        {
            if (weapon.def.Verbs == null) return 0f;

            for (int i = 0; i < weapon.def.Verbs.Count; i++)
            {
                var verb = weapon.def.Verbs[i];
                if (verb != null) return verb.range;
            }
            return 0f;
        }
    }
}
