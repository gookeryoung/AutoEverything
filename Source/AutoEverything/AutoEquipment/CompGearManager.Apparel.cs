using System.Collections.Generic;
using RimWorld;
using Verse;
using AutoEverything.AutoEquipment.Scoring;
using Verse.AI;
using AutoEverything.RoleEvaluation;
using AutoEverything.Allocation;
using AutoEverything.Core;

namespace AutoEverything.AutoEquipment
{
    public partial class CompGearManager
    {
        // ===================== 防具 =====================

        private void EvaluateApparel(Role role, GearContext context, bool contextChanged)
        {
            if (Pawn.apparel == null) return;

            // 即使情境变化也不每 tick 评估，防具更换较慢
            if (!contextChanged && (Find.TickManager.TicksGame + tickOffset) % (AESettings.evaluateInterval * 3) != 0)
                return;

            // 检查意识形态裸体偏好
            bool prefersNudity = false;
            if (Pawn.Ideo != null)
            {
                foreach (var precept in Pawn.Ideo.PreceptsListForReading)
                {
                    if (precept.def.defName.Contains("Nudity") && precept.def.defName.Contains("Approved"))
                        prefersNudity = true;
                }
            }
            if (TraitDefCache.Nudist != null && Pawn.story?.traits?.HasTrait(TraitDefCache.Nudist) == true)
                prefersNudity = true;

            if (prefersNudity)
            {
                if (AEDebug.IsActive) AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(Pawn)} EvaluateApparel: 跳过（偏好裸体）");
                return;
            }

            int wornCount = Pawn.apparel.WornApparel.Count;
            if (AEDebug.IsActive) AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(Pawn)} EvaluateApparel: role={role}, context={context}, contextChanged={contextChanged}, 穿戴 {wornCount} 件");

            // 先纠错：卸下远程角色错误持有的护盾腰带（护盾阻挡远程射击）
            // 放在 BeltAllocator 前：先卸下再分配，避免刚卸下又被重新分配
            RemoveWrongShieldBelt(role);

            // 纠错：非奴隶不应穿戴奴隶项圈（Ideology DLC 奴隶专用装备）
            RemoveSlaveCollar();

            // 腰带附件全局分配：纯近战角色（射击无火）优先装备护盾/消防背包
            // 受 3000 tick 全局周期控制，确保全局至少 1 人消防背包
            BeltAllocator.AllocateForPawn(Pawn);

            // 寻找最佳可用防具（非首个满足阈值的）
            Apparel bestApparel = null;
            float bestScore = -999f;
            float bestWornScore = 0f;
            int candidatesChecked = 0;

            foreach (Thing thing in Pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Apparel))
            {
                if (!(thing is Apparel apparel)) continue;
                if (apparel.IsForbidden(Pawn)) continue;
                if (!Pawn.CanReserve(apparel) || !Pawn.CanReach(apparel, PathEndMode.ClosestTouch, Danger.Some)) continue;

                // 检查 Pawn 是否能穿戴（身体部位、性别）
                if (!ApparelUtility.HasPartsToWear(Pawn, apparel.def)) continue;
                if (apparel.def.apparel?.gender != Gender.None && apparel.def.apparel.gender != Pawn.gender) continue;
                var bioApp = apparel.TryGetComp<CompBiocodable>();
                if (bioApp != null && bioApp.Biocoded && bioApp.CodedPawn != Pawn) continue;

                // 遵守装备策略限制
                if (Pawn.outfits?.CurrentApparelPolicy?.filter != null
                    && !Pawn.outfits.CurrentApparelPolicy.filter.Allows(apparel))
                    continue;

                candidatesChecked++;
                // 监测路径：用 WithBreakdown 版本
                bool monitorApparel = DebugMonitor.monitorEnabled && DebugMonitor.monitorApparelScore;
                float newScore;
                ScoreBreakdown newBreakdown = null;
                if (monitorApparel)
                {
                    newBreakdown = GearScorer.ScoreApparelWithBreakdown(Pawn, apparel, role, context);
                    newScore = newBreakdown.Vetoed ? newBreakdown.VetoScore : newBreakdown.Total;
                }
                else
                {
                    newScore = GearScorer.ScoreApparel(Pawn, apparel, role, context);
                }

                if (newScore <= 0f || newScore <= bestScore) continue;

                // 与同槽位已穿戴防具比较
                bool blocked = false;
                float conflictWornScore = 0f;
                Apparel conflictWorn = null;
                foreach (Apparel worn in Pawn.apparel.WornApparel)
                {
                    if (!ApparelUtility.CanWearTogether(worn.def, apparel.def, Pawn.RaceProps.body))
                    {
                        if (Pawn.apparel.IsLocked(worn)) { blocked = true; break; }
                        float ws = GearScorer.ScoreApparel(Pawn, worn, role, context);
                        if (ws > conflictWornScore)
                        {
                            conflictWornScore = ws;
                            conflictWorn = worn;
                        }
                    }
                }
                if (blocked) continue;

                // 必须以阈值超越已穿戴评分
                if (newScore <= conflictWornScore * (1f + AESettings.upgradeThreshold)) continue;

                bestApparel = apparel;
                bestScore = newScore;
                bestWornScore = conflictWornScore;

                // 监测：候选防具评分
                if (monitorApparel && newBreakdown != null)
                {
                    DebugMonitor.ReportApparelScore(Pawn, apparel, newBreakdown, conflictWorn, conflictWornScore);
                }
            }

            if (bestApparel != null)
            {
                // 决策日志走 AEDebug.Log，与 EvaluateWeapon 一致，避免 50 Pawn 同时换装刷屏
                string conflictInfo = BuildConflictApparelInfo(role, context, bestApparel);
                if (AEDebug.IsActive) AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(Pawn)} EvaluateApparel 决策: 切换到 {bestApparel.LabelShort} (score={bestScore:F1}) 替换穿戴 (score={bestWornScore:F1}, threshold={AESettings.upgradeThreshold:F2}). 检查 {candidatesChecked} 候选. 冲突防具: {conflictInfo}");
                var job = JobMaker.MakeJob(JobDefOf.Wear, bestApparel);
                Pawn.jobs.TryTakeOrderedJob(job, Verse.AI.JobTag.Misc);

                // 监测：上报换装事件
                DebugMonitor.ReportSwap(Pawn, "AE_Slot_Apparel".Translate(), null, bestApparel, bestWornScore, bestScore);
            }
            else
            {
                if (AEDebug.IsActive) AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(Pawn)} EvaluateApparel: 无升级. 检查 {candidatesChecked} 候选");

                // 过渡防具：赤身且无匹配防具时，穿戴任意可用防具过渡
                if (Pawn.apparel.WornApparel.Count == 0)
                {
                    TryFallbackApparel(role, context);
                }
            }
        }

        /// <summary>
        /// 过渡防具兜底：赤身且无匹配防具时，穿戴最不差的可用防具作为过渡。
        /// 仍排除 Veto 的防具（如护盾腰带对非 Brawler——比赤身更糟，会阻挡远程射击）。
        /// 设计意图：赤身受温度/美观惩罚，过渡防具在下次评估时会被更好的匹配替换。
        /// 用 ScoreBreakdown.Total 比较，选评分最高的过渡防具（即使是沾染/低品质也胜过赤身）。
        /// 性能：复用 EvaluateFast 的 fastBreakdown，避免每件候选创建 ScoreBreakdown+List。
        /// </summary>
        private void TryFallbackApparel(Role role, GearContext context)
        {
            Apparel fallbackApparel = null;
            float fallbackScore = -99999f;

            var pipeline = ScoringPipelineFactory.GetApparelPipeline();
            GearWeights weights = GearPolicyEngine.GetWeights();

            foreach (Thing thing in Pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Apparel))
            {
                if (!(thing is Apparel apparel)) continue;
                if (apparel.IsForbidden(Pawn)) continue;
                if (!Pawn.CanReserve(apparel) || !Pawn.CanReach(apparel, PathEndMode.ClosestTouch, Danger.Some)) continue;
                if (!ApparelUtility.HasPartsToWear(Pawn, apparel.def)) continue;
                if (apparel.def.apparel?.gender != Gender.None && apparel.def.apparel.gender != Pawn.gender) continue;
                var bioApp = apparel.TryGetComp<CompBiocodable>();
                if (bioApp != null && bioApp.Biocoded && bioApp.CodedPawn != Pawn) continue;
                // 遵守装备策略限制（尊重玩家 outfit 设置）
                if (Pawn.outfits?.CurrentApparelPolicy?.filter != null
                    && !Pawn.outfits.CurrentApparelPolicy.filter.Allows(apparel))
                    continue;

                // 性能路径：复用 fastBreakdown，读 Vetoed/Total
                // Veto 的防具（如护盾腰带）仍排除——对远程角色比赤身更糟
                ScoreBreakdown bd = pipeline.EvaluateFast(Pawn, apparel, role, context, weights);
                if (bd.Vetoed) continue;
                if (bd.Total <= fallbackScore) continue;

                fallbackScore = bd.Total;
                fallbackApparel = apparel;
            }

            if (fallbackApparel != null)
            {
                // 决策日志走 AEDebug.Log，与 EvaluateWeapon 一致，避免 50 Pawn 同时换装刷屏
                if (AEDebug.IsActive) AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(Pawn)} 过渡防具: 赤身无匹配, 穿戴 '{fallbackApparel.LabelShort}' (score={fallbackScore:F1}, role={role})");
                var job = JobMaker.MakeJob(JobDefOf.Wear, fallbackApparel);
                Pawn.jobs.TryTakeOrderedJob(job, Verse.AI.JobTag.Misc);
            }
        }

        /// <summary>
        /// 检测并卸下错误持有的护盾腰带。
        /// 护盾腰带仅属于重甲前排（Brawler），远程角色/轻甲工人穿着会阻挡远程射击。
        /// 当 Pawn 角色非 Brawler 且正穿着护盾腰带时，卸下并丢到脚下。
        /// 复用 GlobalAllocator 的 apparel 卸下模式：pawn.apparel.Remove + GenDrop.TryDropSpawn。
        /// </summary>
        private void RemoveWrongShieldBelt(Role role)
        {
            if (role == Role.Brawler) return;
            if (Pawn.apparel?.WornApparel == null) return;

            List<Apparel> worn = Pawn.apparel.WornApparel;
            // 倒序遍历：卸下时列表会变，倒序避免索引错位
            for (int i = worn.Count - 1; i >= 0; i--)
            {
                Apparel ap = worn[i];
                if (!GearDefClassifier.IsShieldBelt(ap)) continue;
                // 尊重玩家锁定的单件装备（与 EvaluateApparel 冲突检测一致）
                if (Pawn.apparel.IsLocked(ap)) continue;

                Pawn.apparel.Remove(ap);
                Thing dropped;
                if (GenDrop.TryDropSpawn(ap, Pawn.Position, Pawn.Map, ThingPlaceMode.Near, out dropped))
                {
                    // 纠错日志走 AEDebug.Log，与 EvaluateWeapon 一致，避免战斗中集中纠错刷屏
                    if (AEDebug.IsActive) AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(Pawn)} 卸下错误护盾腰带 '{ap.LabelShort}' (role={role}，护盾阻挡远程射击)");
                }
                // belt 层最多一件护盾腰带，卸下后即返回
                return;
            }
        }

        /// <summary>
        /// 检测并卸下非奴隶身上的奴隶项圈。
        /// 奴隶项圈是 Ideology DLC 为奴隶设计的装备，非奴隶穿着无意义。
        /// 评分阶段已由 ApparelForbiddenScorer Veto，本方法处理旧存档/玩家手动穿戴的残留。
        /// 复用 RemoveWrongShieldBelt 的卸下模式：apparel.Remove + GenDrop.TryDropSpawn。
        /// </summary>
        private void RemoveSlaveCollar()
        {
            if (DLCCompat.IsSlave(Pawn)) return;  // 奴隶保留项圈
            if (Pawn.apparel?.WornApparel == null) return;

            List<Apparel> worn = Pawn.apparel.WornApparel;
            for (int i = worn.Count - 1; i >= 0; i--)
            {
                Apparel ap = worn[i];
                if (!GearDefClassifier.IsSlaveCollar(ap)) continue;
                if (Pawn.apparel.IsLocked(ap)) continue;

                Pawn.apparel.Remove(ap);
                Thing dropped;
                if (GenDrop.TryDropSpawn(ap, Pawn.Position, Pawn.Map, ThingPlaceMode.Near, out dropped))
                {
                    // 纠错日志走 AEDebug.Log，与 EvaluateWeapon 一致，避免旧存档加载集中纠错刷屏
                    if (AEDebug.IsActive) AEDebug.Log(() => $"[AutoEverything] {AEDebug.Label(Pawn)} 卸下奴隶项圈 '{ap.LabelShort}' (非奴隶不应穿戴)");
                }
                return;
            }
        }

        /// <summary>
        /// 构建冲突防具日志字符串（使用 for 循环替代 LINQ，避免 Tick 路径闭包分配）。
        /// </summary>
        private string BuildConflictApparelInfo(Role role, GearContext context, Apparel bestApparel)
        {
            var sb = new System.Text.StringBuilder();
            var worn = Pawn.apparel.WornApparel;
            bool first = true;
            for (int i = 0; i < worn.Count; i++)
            {
                Apparel w = worn[i];
                if (!ApparelUtility.CanWearTogether(w.def, bestApparel.def, Pawn.RaceProps.body))
                {
                    if (!first) sb.Append(", ");
                    sb.Append(w.LabelShort).Append("=").Append(GearScorer.ScoreApparel(Pawn, w, role, context).ToString("F1"));
                    first = false;
                }
            }
            return sb.ToString();
        }
    }
}
