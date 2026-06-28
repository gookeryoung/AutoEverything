using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace AutoEquipment
{
    /// <summary>
    /// 武器与防具评分器：基于角色、情境、意识形态与环境条件计算装备适配度。
    /// 评分越高代表越适合该 Pawn。
    /// </summary>
    public static class GearScorer
    {
        // ===================== 武器评分 =====================

        /// <summary>
        /// 为 Pawn 评分武器适配度，分数越高越适合。
        /// </summary>
        public static float ScoreWeapon(Pawn pawn, Thing weapon, Role role, GearContext context)
        {
            if (weapon?.def == null) return -1000f;

            // 生物编码/人格绑定武器：仅允许归属者使用
            // CompBladelinkWeapon 继承自 CompBiocodable，一次检查覆盖两类
            var biocomp = weapon.TryGetComp<CompBiocodable>();
            if (biocomp != null && biocomp.Biocoded)
            {
                if (biocomp.CodedPawn == pawn)
                    return 9000f; // 归属此 Pawn，永远保留
                else
                    return -9000f; // 已绑定他人，绝不装备
            }

            float score = 0f;
            bool wantsMelee = RoleDetector.PrefersMelee(role);
            bool isMelee = weapon.def.IsMeleeWeapon;
            bool isRanged = weapon.def.IsRangedWeapon;

            // 角色契合度：匹配武器类型给予大量加成
            if (wantsMelee && isMelee) score += 100f;
            else if (!wantsMelee && isRanged) score += 100f;
            else if (!wantsMelee && isMelee) score -= 50f;
            // 修复：格斗者（wantsMelee）绝不主动装备远程武器
            // 即使无近战武器可用或处于狩猎情境也返回极端负分
            // 让格斗者宁愿空手也不拿枪
            else if (wantsMelee && isRanged) return -9000f;

            // 情境：狩猎需要远程武器
            if (context == GearContext.Hunting)
            {
                if (isRanged) score += 80f;
                else score -= 100f;
                // 狩猎偏好射程更长的武器
                if (isRanged)
                {
                    float range = GetWeaponRange(weapon);
                    score += range * 1.5f;
                }
            }

            // DPS / 伤害输出
            if (isMelee)
            {
                float dps = weapon.GetStatValue(StatDefOf.MeleeWeapon_AverageDPS);
                score += dps * 5f;
            }
            else if (isRanged)
            {
                float dmgMult = weapon.GetStatValue(StatDefOf.RangedWeapon_DamageMultiplier);
                score += dmgMult * 30f;
                float cooldown = weapon.GetStatValue(StatDefOf.RangedWeapon_Cooldown);
                if (cooldown > 0) score += (1f / cooldown) * 10f;
            }

            // 耐久度：损坏的武器扣分
            if (weapon.HitPoints < weapon.MaxHitPoints)
            {
                float hpPct = (float)weapon.HitPoints / weapon.MaxHitPoints;
                score *= hpPct;
            }

            // 品质加成
            if (weapon.TryGetQuality(out QualityCategory quality))
                score += ((int)quality - 2) * 10f; // Normal=0, Good=+10, Excellent=+20

            // 技能匹配：射击技能提升远程评分，近战技能提升近战评分
            // 注意：必须用括号包裹 ?? 0，否则 ?? 优先级低于 * 会导致乘数永远失效
            if (pawn.skills != null)
            {
                if (isRanged)
                    score += (pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0) * 2f;
                if (isMelee)
                    score += (pawn.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0) * 2f;
            }

            // 意识形态武器偏好
            if (pawn.Ideo != null)
            {
                foreach (var precept in pawn.Ideo.PreceptsListForReading)
                {
                    string preceptName = precept.def.defName;
                    if (preceptName.Contains("Weapon") || preceptName.Contains("Melee")
                        || preceptName.Contains("Ranged"))
                    {
                        bool disapproved = preceptName.Contains("Disapproved")
                            || preceptName.Contains("Despised")
                            || preceptName.Contains("Horrible");
                        float preceptScore = disapproved ? -30f : 30f;

                        if (isMelee && preceptName.Contains("Melee"))
                            score += preceptScore;
                        if (isRanged && preceptName.Contains("Ranged"))
                            score += preceptScore;
                    }
                }
            }

            // 特质偏好
            if (pawn.story?.traits != null)
            {
                // 鲁莽射手：偏好快速武器
                var triggerHappy = TraitDef.Named("ShootingAccuracy");
                if (triggerHappy != null && pawn.story.traits.HasTrait(triggerHappy, -1) && isRanged)
                {
                    float cooldown = weapon.GetStatValue(StatDefOf.RangedWeapon_Cooldown);
                    if (cooldown < 1.5f) score += 20f;
                }
                // 谨慎射手：偏好精准武器
                if (triggerHappy != null && pawn.story.traits.HasTrait(triggerHappy, 1) && isRanged)
                {
                    float range = GetWeaponRange(weapon);
                    if (range > 25f) score += 20f;
                }
            }

            // 可疑评分日志：真实武器却得 0 或负分时记录
            if (score <= 0f && (isRanged || isMelee))
            {
                Log.Warning($"[AutoEquipment] ScoreWeapon: 可疑评分 {score:F1} for {pawn.LabelShort} + '{weapon.def.defName}' (role={role}, context={context}, isMelee={isMelee}, isRanged={isRanged}, wantsMelee={wantsMelee})");
            }

            return score;
        }

        /// <summary>
        /// 获取武器射程（替代 LINQ FirstOrDefault，避免 Tick 路径分配闭包）。
        /// </summary>
        private static float GetWeaponRange(Thing weapon)
        {
            if (weapon.def.Verbs == null) return 0f;
            for (int i = 0; i < weapon.def.Verbs.Count; i++)
            {
                var verb = weapon.def.Verbs[i];
                if (verb != null) return verb.range;
            }
            return 0f;
        }

        // ===================== 防具评分 =====================

        /// <summary>
        /// 为 Pawn 评分防具适配度，分数越高越适合。
        /// </summary>
        public static float ScoreApparel(Pawn pawn, Apparel apparel, Role role, GearContext context)
        {
            if (apparel?.def == null) return -1000f;

            float score = 0f;

            // 基础防护值：使用 GetStatValueAbstract 保证已穿戴与未穿戴评分一致
            // GetStatValue 会因装备状态变化导致评分不一致
            ThingDef stuff = apparel.Stuff;
            float armor = apparel.def.GetStatValueAbstract(StatDefOf.ArmorRating_Sharp, stuff)
                + apparel.def.GetStatValueAbstract(StatDefOf.ArmorRating_Blunt, stuff) * 0.5f;
            float insulation = apparel.def.GetStatValueAbstract(StatDefOf.Insulation_Cold, stuff)
                + apparel.def.GetStatValueAbstract(StatDefOf.Insulation_Heat, stuff);

            // 基于情境的评分
            switch (context)
            {
                case GearContext.Combat:
                    // 战斗：护甲为王
                    score += armor * 200f;
                    float moveSpeedOffset = GetMoveSpeedOffset(apparel);
                    score += moveSpeedOffset * 20f;
                    break;

                case GearContext.Work:
                    // 工作：偏好技能加成
                    score += ScoreApparelForWork(pawn, apparel, role);
                    // 仍需基础护甲
                    score += armor * 30f;
                    float workMoveOffset = GetMoveSpeedOffset(apparel);
                    score += workMoveOffset * 15f;
                    break;

                case GearContext.Cold:
                    // 寒冷：保温至关重要
                    score += apparel.def.GetStatValueAbstract(StatDefOf.Insulation_Cold, stuff) * 50f;
                    score += armor * 20f;
                    break;

                case GearContext.Hot:
                    // 炎热：隔热与轻便优先
                    score += apparel.def.GetStatValueAbstract(StatDefOf.Insulation_Heat, stuff) * 50f;
                    // 惩罚重甲
                    score -= armor * 30f;
                    break;

                default:
                    // 平衡：各类属性都要
                    score += armor * 50f;
                    score += insulation * 10f;
                    break;
            }

            // 品质加成
            if (apparel.TryGetQuality(out QualityCategory quality))
                score += ((int)quality - 2) * 15f;

            // 美感（针对有美感偏好的 Pawn）
            if (pawn.story?.traits?.HasTrait(TraitDef.Named("Beauty"), 2) == true)
                score += apparel.GetStatValue(StatDefOf.Beauty) * 5f;

            // 意识形态偏好
            score += ScoreApparelForIdeology(pawn, apparel);

            // 皇家头衔要求
            score += ScoreApparelForRoyalty(pawn, apparel);

            // 当前已穿戴：小幅加成作为平局决胜
            // 因评分已统一使用 GetStatValueAbstract，此加成仅需防止近乎相同评分的反复切换
            if (pawn.apparel?.WornApparel?.Contains(apparel) == true)
                score += 5f;

            // 耐久度：损坏的防具扣分
            if (apparel.HitPoints < apparel.MaxHitPoints)
            {
                float hpPct = (float)apparel.HitPoints / apparel.MaxHitPoints;
                score *= hpPct;
            }

            // 沾染尸体：大幅扣分
            if (apparel.WornByCorpse)
                score -= 100f;

            // 可疑评分日志：非沾染却得负分
            if (score <= 0f && !apparel.WornByCorpse)
            {
                Log.Warning($"[AutoEquipment] ScoreApparel: 可疑评分 {score:F1} for {pawn.LabelShort} + '{apparel.def.defName}' (role={role}, context={context})");
            }

            return score;
        }

        /// <summary>
        /// 获取防具的移动速度偏移（替代 LINQ Where + FirstOrDefault）。
        /// </summary>
        private static float GetMoveSpeedOffset(Apparel apparel)
        {
            if (apparel.def.equippedStatOffsets == null) return 0f;
            for (int i = 0; i < apparel.def.equippedStatOffsets.Count; i++)
            {
                var mod = apparel.def.equippedStatOffsets[i];
                if (mod.stat == StatDefOf.MoveSpeed) return mod.value;
            }
            return 0f;
        }

        private static float ScoreApparelForWork(Pawn pawn, Apparel apparel, Role role)
        {
            float score = 0f;

            switch (role)
            {
                case Role.Doctor:
                    score += apparel.GetStatValue(StatDefOf.MedicalSurgerySuccessChance, true, -1) * 100f;
                    score += apparel.GetStatValue(StatDefOf.MedicalTendQuality, true, -1) * 80f;
                    break;

                case Role.Worker:
                    // 通用工作速度
                    score += apparel.GetStatValue(StatDefOf.WorkSpeedGlobal, true, -1) * 60f;
                    break;

                case Role.Shooter:
                case Role.Brawler:
                    // 战斗角色工作中仍重视护甲
                    score += apparel.GetStatValue(StatDefOf.ArmorRating_Sharp) * 80f;
                    break;
            }

            return score;
        }

        private static float ScoreApparelForIdeology(Pawn pawn, Apparel apparel)
        {
            float score = 0f;

            if (pawn.Ideo == null) return score;

            try
            {
                // 检查意识形态角色要求的防具
                var ideoRole = pawn.Ideo.GetRole(pawn);
                if (ideoRole != null)
                {
                    // 拥有意识形态角色的 Pawn 穿戴配套防具有加成
                    score += 20f;
                }

                // 检查防具是否符合意识形态风格
                if (apparel.StyleDef != null && pawn.Ideo.style != null)
                {
                    score += 10f;
                }
            }
            catch (Exception ex)
            {
                // 评分失败不应中断流程，记录后继续
                Log.WarningOnce($"[AutoEquipment] 意识形态防具评分失败: {ex.Message}", pawn.thingIDNumber ^ 0x4C7A);
            }

            return score;
        }

        private static float ScoreApparelForRoyalty(Pawn pawn, Apparel apparel)
        {
            // 检查 Pawn 是否拥有要求特定防具的皇家头衔
            if (pawn.royalty == null) return 0f;

            float score = 0f;
            foreach (var title in pawn.royalty.AllTitlesForReading)
            {
                if (title.def.requiredApparel != null)
                {
                    foreach (var req in title.def.requiredApparel)
                    {
                        if (req.ApparelMeetsRequirement(apparel.def, false))
                            score += 40f;
                    }
                }
            }
            return score;
        }

        // ===================== 副武器评分 =====================

        /// <summary>
        /// 为 Pawn 评分副武器（备用武器）：远程 Pawn 配近战副武器，
        /// 近战 Pawn 配远程副武器。
        /// </summary>
        public static float ScoreSidearm(Pawn pawn, Thing weapon, Role role)
        {
            if (weapon?.def == null) return -1000f;

            bool isMelee = weapon.def.IsMeleeWeapon;
            bool isRanged = weapon.def.IsRangedWeapon;
            bool wantsMelee = RoleDetector.PrefersMelee(role);

            float score = 0f;

            // 副武器应为主武器的相反类型
            if (wantsMelee && isRanged)
                score += 50f; // 格斗者偏好远程副武器
            else if (!wantsMelee && isMelee)
                score += 50f; // 射手偏好近战副武器
            else
                score -= 30f; // 与主武器同类型 = 副武器价值低

            // 近战副武器：偏好快速轻便的武器（匕首、短剑）
            if (isMelee)
            {
                float dps = weapon.GetStatValue(StatDefOf.MeleeWeapon_AverageDPS);
                score += dps * 3f;
                float mass = weapon.GetStatValue(StatDefOf.Mass);
                score -= mass * 5f;
            }

            // 远程副武器：偏好手枪/冲锋枪（短射程、快射速）
            if (isRanged)
            {
                float cooldown = weapon.GetStatValue(StatDefOf.RangedWeapon_Cooldown);
                if (cooldown < 1.5f) score += 20f;
                float mass = weapon.GetStatValue(StatDefOf.Mass);
                score -= mass * 3f;
            }

            // 品质
            if (weapon.TryGetQuality(out QualityCategory quality))
                score += ((int)quality - 2) * 5f;

            return score;
        }
    }
}
