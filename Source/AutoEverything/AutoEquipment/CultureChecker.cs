using System.Collections.Generic;
using RimWorld;
using Verse;
using AutoEverything.Core;

namespace AutoEverything.AutoEquipment
{
    /// <summary>
    /// 文化/心情影响判定器：评估 apparel 对 Pawn 的意识形态与心情影响。
    ///
    /// 两类判定（综合违反判定 + 符合要求加分，符合需求 req-01 第 3 条）：
    /// 1. ideo 违反：apparel 覆盖 ideo 强制要求的 bodyPartGroup，但 def 不匹配 → 扣分（避免分配）
    /// 2. ideo apparel 符合：apparel.def 是 ideo 强制要求的 apparelDef → 加分
    ///
    /// 注：原计划含"偏好材质加分"，但 RimWorld 1.6 中 Ideo 类未暴露 PreferredApparelStuffs 公共属性，
    /// 故移除 stuff 偏好功能。保留违反扣分与符合要求加分，已能覆盖 ideo 主要影响场景。
    ///
    /// 设计简化：
    /// - 不真正模拟穿戴后整体 ideo 违反情况（需 pawn.apparel 全状态），仅评估单件 apparel 与 ideo 的契合度
    /// - 调用方 GearAllocator 在分配时按评分排序，违反扣分会自然降低优先级
    /// - DLC API 必须检查 ModsConfig.IdeologyActive
    /// </summary>
    internal static class CultureChecker
    {
        /// <summary>
        /// 计算 apparel 对 Pawn 的文化契合分（不含护甲值与定位契合）。
        /// 返回值：正=符合偏好加分；负=违反扣分；0=无影响
        /// </summary>
        public static float GetCultureScore(Pawn pawn, Apparel apparel)
        {
            if (pawn == null || apparel == null) return 0f;
            if (!ModsConfig.IdeologyActive) return 0f;

            Ideo ideo = pawn.Ideo;
            if (ideo == null) return 0f;

            float score = 0f;

            // 1. ideo 违反检查：apparel 覆盖某 ideo 要求的 bodyPartGroup 但 def 不匹配
            if (ViolatesIdeoRequirement(apparel, ideo))
            {
                score -= AESettings.geCultureViolationPenalty;
            }

            // 2. apparel 符合 ideo 要求：apparel.def 是某 ideo requirement 的 requiredDef
            if (MatchesIdeoRequirement(apparel, ideo))
            {
                score += AESettings.geCultureRequirementBonus;
            }

            return score;
        }

        /// <summary>
        /// 检查 apparel 是否违反 ideo 要求：
        /// apparel 覆盖了 ideo 强制要求的 bodyPartGroup，但 apparel.def 不是要求的 apparelDef。
        /// 设计：apparel 覆盖该 bodyPartGroup 会"霸占"该位置，导致 pawn 无法穿上 ideo 要求的 apparel → 视为违反。
        ///
        /// RimWorld 1.6 API：
        /// - PreceptDef.roleApparelRequirements: List&lt;PreceptApparelRequirement&gt;
        /// - PreceptApparelRequirement.requirement: ApparelRequirement
        /// - ApparelRequirement.bodyPartGroupsMatchAny: List&lt;BodyPartGroupDef&gt;
        /// - ApparelRequirement.requiredDefs: List&lt;ThingDef&gt;
        /// </summary>
        private static bool ViolatesIdeoRequirement(Apparel apparel, Ideo ideo)
        {
            ApparelProperties props = apparel.def?.apparel;
            if (props == null) return false;
            List<BodyPartGroupDef> bodyParts = props.bodyPartGroups;
            if (bodyParts == null || bodyParts.Count == 0) return false;

            List<Precept> precepts = ideo.PreceptsListForReading;
            for (int i = 0; i < precepts.Count; i++)
            {
                Precept precept = precepts[i];
                List<PreceptApparelRequirement> reqs = precept.def?.roleApparelRequirements;
                if (reqs == null) continue;
                for (int j = 0; j < reqs.Count; j++)
                {
                    PreceptApparelRequirement req = reqs[j];
                    ApparelRequirement apparelReq = req?.requirement;
                    if (apparelReq == null) continue;

                    List<BodyPartGroupDef> reqBodyParts = apparelReq.bodyPartGroupsMatchAny;
                    List<ThingDef> reqDefs = apparelReq.requiredDefs;
                    if (reqBodyParts == null || reqBodyParts.Count == 0) continue;

                    // apparel 覆盖该 bodyPartGroup 且 def 不在 requiredDefs 中 → 违反
                    bool coversReqBodyPart = false;
                    for (int k = 0; k < reqBodyParts.Count; k++)
                    {
                        if (bodyParts.Contains(reqBodyParts[k]))
                        {
                            coversReqBodyPart = true;
                            break;
                        }
                    }
                    if (!coversReqBodyPart) continue;

                    bool defMatches = false;
                    if (reqDefs != null)
                    {
                        for (int k = 0; k < reqDefs.Count; k++)
                        {
                            if (reqDefs[k] == apparel.def)
                            {
                                defMatches = true;
                                break;
                            }
                        }
                    }
                    if (!defMatches) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 检查 apparel.def 是否是 ideo 强制要求的 apparelDef（任何 requirement 的 requiredDefs）。
        /// </summary>
        private static bool MatchesIdeoRequirement(Apparel apparel, Ideo ideo)
        {
            List<Precept> precepts = ideo.PreceptsListForReading;
            for (int i = 0; i < precepts.Count; i++)
            {
                List<PreceptApparelRequirement> reqs = precepts[i].def?.roleApparelRequirements;
                if (reqs == null) continue;
                for (int j = 0; j < reqs.Count; j++)
                {
                    ApparelRequirement apparelReq = reqs[j]?.requirement;
                    if (apparelReq == null) continue;
                    List<ThingDef> reqDefs = apparelReq.requiredDefs;
                    if (reqDefs == null) continue;
                    for (int k = 0; k < reqDefs.Count; k++)
                    {
                        if (reqDefs[k] == apparel.def) return true;
                    }
                }
            }
            return false;
        }
    }
}
