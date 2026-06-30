# 护盾腰带双保险：评分 Veto + 自动卸下错误持有

> 目标：修复"远程角色主动寻找护盾腰带"的 BUG，并实现"护盾腰带持有者带远程装备时自动识别角色并脱掉护盾腰带"。
> 遵循现有 Veto 模式与 GlobalAllocator 的 apparel 卸下模式，保持架构一致。

## 背景（Context）

用户发现两个问题：
1. **远程角色主动寻找护盾腰带**：当前 `BeltAllocator` 已 gate 候选人到 `ArmorPreference.Heavy`（Brawler），但防具评分管线（`ScoringPipelineFactory.GetApparelPipeline`）中**没有护盾腰带的硬否决**。若因角色判定瞬变、玩家手动操作、或未来代码变更导致远程角色进入护盾腰带评分路径，他们会因为护盾腰带的高护甲值而被分配——这会阻挡他们的远程射击，是严重 BUG。
2. **已穿护盾腰带的远程角色无法自动脱掉**：当前 `CompGearManager.EvaluateApparel` 只做"寻找更好的防具升级"，**不会主动卸下错误的已穿戴防具**。若一个 Pawn 角色从 Brawler 变为 Shooter（或玩家手动给他穿了护盾腰带），系统不会自动脱掉护盾腰带，导致远程武器失效。

统一规则：**护盾腰带仅属于重甲前排（Brawler/Heavy），其他角色一律拒绝/卸下**。

## 当前状态分析

### 已有的护盾腰带约束
- `BeltAllocator.CollectCandidatePawns` L166-167：gate 候选人到 `ArmorPreference.Heavy`（Brawler）✓
- `WeaponTraitScorer` L41-46：穿护盾腰带时 Veto 远程武器（-9000f）✓
- `GearDefClassifier.IsShieldBelt(Thing)` L35-42：检测护盾腰带（defName 含 SHIELD + Belt 层）✓
- `GearDefClassifier.HasBeltLayerApparel(Pawn)` L58-69：检测是否已穿 belt 层 ✓

### 缺失的约束
- ✗ 防具评分管线**无护盾腰带 Veto**：远程角色评分护盾腰带时不会被拒绝
- ✗ `EvaluateApparel` **不主动卸下错误护盾腰带**：只做升级，不做纠错

### 关键 API 与模式（已验证可复用）
- **Veto 模式**：`breakdown.Veto(-9000f)` → `ScoreBreakdown.Vetoed=true`，管线短路，返回 `VetoScore`（见 `WeaponTraitScorer` L34）
- **卸下防具模式**：`pawn.apparel.Remove(ap)` + `GenDrop.TryDropSpawn(ap, pawn.Position, pawn.Map, ThingPlaceMode.Near, out dropped)`（见 `GlobalAllocator` L295-297）
- **角色→护甲偏好**：`RoleDetector.GetArmorPreference(role)` → `ArmorPreference.Heavy` 仅 `Role.Brawler`（见 `PawnRole.cs` L219-234）
- **EvaluateApparel 守卫**：`CompTick` L74 `if (locked) return;` + L75 Dead/Downed + L98 食尸鬼 + L110 征召 + L132 奴隶——EvaluateApparel 继承所有守卫，无需重复

## 实施方案

### Task 1：新建 ApparelShieldBeltScorer（评分 Veto）

**新文件**：`Source/AutoEverything/AutoEquipment/Scoring/Apparels/ApparelShieldBeltScorer.cs`

**职责**：防具评分第一关——护盾腰带仅允许 Brawler，其他角色硬否决。

```csharp
using RimWorld;
using Verse;
using AutoEverything.RoleEvaluation;
using AutoEverything.AutoEquipment;

namespace AutoEverything.AutoEquipment.Scoring.Apparels
{
    /// <summary>
    /// 护盾腰带硬约束：护盾腰带仅属于重甲前排（Brawler）。
    /// 护盾腰带会阻挡所有远程射击，远程角色（Shooter/Hunter/Leader/Worker/Doctor 等）一律拒绝。
    /// 设计意图：与 BeltAllocator 的 Heavy gate 互为双保险——
    ///   BeltAllocator 在分配阶段 gate，本 Scorer 在评分阶段 Veto，防止任何路径漏网。
    /// </summary>
    public class ApparelShieldBeltScorer : IScorer<Apparel>
    {
        public string Name => "护盾腰带约束";

        public void Score(Pawn pawn, Apparel gear, Role role, GearContext context,
                          GearWeights weights, ScoreBreakdown breakdown)
        {
            if (role == Role.Brawler) return;
            if (GearDefClassifier.IsShieldBelt(gear))
            {
                breakdown.Veto(-9999f);
                breakdown.AddScore(Name, "非格斗者+护盾腰带=拒绝", -9999f);
            }
        }
    }
}
```

**设计要点**：
- `-9999f`：用户指定值，比武器 Veto 的 `-9000f` 更低，调试日志中一眼可辨
- `role == Role.Brawler` 时直接 return（Brawler 允许护盾腰带，不干预后续评分）
- 仅检测护盾腰带（`IsShieldBelt`），不否决消防背包等非护盾 belt 层附件

### Task 2：ScoringPipelineFactory 插入新 Scorer

**文件**：`Source/AutoEverything/AutoEquipment/Scoring/ScoringPipelineFactory.cs`

**修改**：`GetApparelPipeline()` 的 scorers 列表**首位**插入 `new ApparelShieldBeltScorer()`。

```csharp
var scorers = new List<IScorer<Apparel>>
{
    new ApparelShieldBeltScorer(),   // 护盾腰带硬约束（非 Brawler 拒绝）
    new ApparelTaintedScorer(),       // 沾染惩罚
    // ... 其余 11 个不变
};
```

**理由**：放在首位让 Veto 尽早短路，避免后续 11 个 Scorer 无谓计算。
管线从 12 个 Scorer 增至 13 个。

### Task 3：CompGearManager 新增 RemoveWrongShieldBelt（自动卸下）

**文件**：`Source/AutoEverything/AutoEquipment/CompGearManager.cs`

**新增私有方法**：`RemoveWrongShieldBelt(Role role)`

```csharp
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

    List<Apparel> wornCopy = Pawn.apparel.WornApparel;
    for (int i = wornCopy.Count - 1; i >= 0; i--)
    {
        Apparel ap = wornCopy[i];
        if (!GearDefClassifier.IsShieldBelt(ap)) continue;
        if (Pawn.apparel.IsLocked(ap)) continue;

        Pawn.apparel.Remove(ap);
        Thing dropped;
        if (GenDrop.TryDropSpawn(ap, Pawn.Position, Pawn.Map, ThingPlaceMode.Near, out dropped))
        {
            Log.Message($"[AutoEverything] {AEDebug.Label(Pawn)} 卸下错误护盾腰带 '{ap.LabelShort}' (role={role}，护盾阻挡远程射击)");
        }
        return;  // belt 层最多一件，卸下后即返回
    }
}
```

**设计要点**：
- 倒序遍历（`Count - 1 → 0`）：卸下时列表会变，倒序避免索引错位
- `IsLocked(ap)` 检查：尊重玩家锁定的单件装备（与 `EvaluateApparel` L540 一致）
- 复用 `GlobalAllocator` 的 `pawn.apparel.Remove` + `GenDrop.TryDropSpawn` 模式（已验证）
- `return` 提前退出：belt 层最多一件护盾腰带，无需继续遍历

**调用点**：在 `EvaluateApparel` 方法中，**nudity 检查之后、BeltAllocator 调用之前**插入调用：

```csharp
// EvaluateApparel 内，L483（prefersNudity 检查块）之后：
RemoveWrongShieldBelt(role);

// 腰带附件全局分配：纯近战角色（射击无火）优先装备护盾/消防背包
BeltAllocator.AllocateForPawn(Pawn);
```

**理由**：
- 放在 nudity 检查后：偏好裸体的 Pawn 不应被干预
- 放在 BeltAllocator 前：先纠错再分配，避免刚卸下又被重新分配
- 放在寻找升级防具循环前：卸下后 belt 层空缺，EvaluateApparel 的升级循环可自然填补合适的 belt

### Task 4：make check 验证

**命令**：`make check`
**预期**：0 警告 0 错误。

### Task 5：README 同步

**文件**：`README.md`

需更新 3 处：

**5.1 防具评分管线表格**（L140-157）：
- 标题计数 "12 个 Scorer" → "13 个 Scorer"
- 表格首行新增 `ApparelShieldBeltScorer`（顺序 1），其余顺延

**5.2 腰带附件全局分配章节**（L65-84）：
- 在"护盾腰带分配规则"段落后新增"护盾腰带自动卸下"说明

**5.3 护盾腰带约束段落**（L55 与 L82-84）：
- 补充评分 Veto 规则与自动卸下规则

具体文案见实施时填写，核心内容：
- 评分 Veto：`ApparelShieldBeltScorer` 在防具评分首位检查，非 Brawler 角色 + 护盾腰带 → `Veto(-9999f)`
- 自动卸下：`CompGearManager.EvaluateApparel` 周期检测已穿护盾腰带的非 Brawler 角色，卸下并丢到脚下
- 双保险：BeltAllocator（分配 gate）+ ApparelShieldBeltScorer（评分 Veto）+ RemoveWrongShieldBelt（已穿纠错）

### Task 6：make rebuild-check 最终验证

**命令**：`make rebuild-check`
**预期**：0 警告 0 错误，DLL 输出存在。

## 假设与决策

### 决策
1. **卸下判定基于角色，不基于当前武器**：`role != Brawler` 即卸下，而非"有远程武器才卸下"。
   理由：与 BeltAllocator 的 Heavy gate、ApparelShieldBeltScorer 的 Veto 完全一致（均角色驱动）；
   且远程角色即使暂时无武器，护盾腰带也应释放给需要它的 Brawler。
   Brawler 即使持有远程武器（技能型 Brawler）也保留护盾——他们会贴身切近战。
2. **Veto 分值 -9999f**：用户指定值，比武器 Veto 的 -9000f 更低，调试日志可辨。
3. **新 Scorer 放管线首位**：Veto 尽早短路，省后续 12 个 Scorer 计算。
4. **卸下用直接 Remove + GenDrop，不用 Job**：与 GlobalAllocator 一致；apparel 已在身上，无需寻路。
5. **卸下后不主动重新分配**：belt 层空缺后，BeltAllocator 下次周期会为 Brawler 分配；
   非 Brawler 的 belt 层空缺由 EvaluateApparel 升级循环自然填补（消防背包等不会被 Veto）。

### 不做
- 不改 `BeltAllocator`：它已正确 gate 到 Heavy，无需改动。
- 不改 `WeaponTraitScorer`：护盾腰带+远程武器的 Veto 已存在，保留。
- 不新增翻译键：卸下日志用 `Log.Message`（开发者日志，非玩家可见 UI），无需翻译。

## 验证步骤

### 编译验证
1. `make check` 通过（Task 4）
2. `make rebuild-check` 通过（Task 6）

### 逻辑验证（代码审查）
1. 远程角色评分护盾腰带 → `ApparelShieldBeltScorer` 返回 -9999f（Veto 短路）
2. 远程角色已穿护盾腰带 → `RemoveWrongShieldBelt` 卸下并丢到脚下
3. Brawler 评分护盾腰带 → `ApparelShieldBeltScorer` return（不干预，正常评分）
4. Brawler 已穿护盾腰带 → `RemoveWrongShieldBelt` return（保留）
5. 锁定 Pawn → `CompTick` L74 return（不进入 EvaluateApparel，不会卸下）
6. 征召 Pawn → `CompTick` L110 return（不打断战斗）

### 文档同步验证
- README 防具评分管线表格已更新（13 个 Scorer）
- README 腰带附件章节已补充 Veto 与自动卸下规则
- 同步检查清单：改了 Scorer 加分公式 → README 已更新 ✓

## 实施顺序

1. Task 1（新建 ApparelShieldBeltScorer）→ Task 2（管线插入）→ Task 3（CompGearManager 卸下）→ Task 4（make check）
2. Task 5（README 同步）
3. Task 6（make rebuild-check 最终验证）
