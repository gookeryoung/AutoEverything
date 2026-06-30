# 禁止类装备 + 研究型实验服加分

## Context

玩家观察到三个问题：

1. **非奴隶穿奴隶项圈**：殖民者（非奴隶）身上出现奴隶项圈（`Apparel_Collar`）。奴隶项圈带 `<slaveApparel>true</slaveApparel>` 标志，是 Ideology DLC 为奴隶设计的装备，非奴隶穿着无意义且影响美观。当前评分管线无任何约束，导致过渡防具兜底或全局重配时可能误穿。
2. **自动穿危险装备**：毒气背包（死气背包 `Apparel_DeadlifePack`）、手榴弹（`Weapon_GrenadeFrag` 等）、末日火箭（`Gun_DoomsdayRocket`、`Gun_TripleRocket`）等单次使用/范围伤害的装备被自动装备。这些装备作为主武器或日常防具不合适：手榴弹/火箭是单次消耗品，死气背包会毒伤友军。
3. **研究型殖民者不穿实验服**：近战远程均无火、但医疗/研究等级高的殖民者（非战斗型），应当优先穿实验服（`Apparel_LabCoat`，提供 `ResearchSpeed +0.05`、`EntityStudyRate +0.1`）提升研究/医疗效率，但当前评分无此偏好。

**目标**：复用已有的"三重保险"模式（评分 Veto + 已穿纠错 + 分配 gate），让评分管线拒绝禁止类装备，并让研究型殖民者主动偏好实验服。

## 实现方案

### 第 1 步：扩展 GearDefClassifier 集中识别

文件：`Source/AutoEverything/AutoEquipment/GearDefClassifier.cs`

新增 4 个识别方法（参考现有 `IsShieldBelt`/`IsFirefoamPack` 的 `IndexOf(OrdinalIgnoreCase)` 模式）：

```csharp
// 奴隶项圈：用 RimWorld 原生 apparel.slaveApparel 标志位，覆盖所有 MOD 扩展的奴隶项圈
public static bool IsSlaveCollar(Thing thing)
{
    return thing?.def?.apparel?.slaveApparel == true;
}

// 危险防具：死气背包等释放毒云的背包（defName 含 DEADLIFE）
public static bool IsDeadlifePack(Thing thing)
{
    return thing?.def != null
        && thing.def.defName.IndexOf("DEADLIFE", StringComparison.OrdinalIgnoreCase) >= 0;
}

// 危险武器：手榴弹（defName/label 含 GRENADE）+ 火箭发射器（label 含 rocket launcher）
// 注意：EMP 手雷作为库存携带特例，由 SidearmAllocator 处理，不经过武器评分管线
public static bool IsDangerousWeapon(Thing thing)
{
    if (thing?.def == null) return false;
    if (thing.def.defName.IndexOf("GRENADE", StringComparison.OrdinalIgnoreCase) >= 0
        || thing.def.label.IndexOf("grenade", StringComparison.OrdinalIgnoreCase) >= 0)
        return true;
    return thing.def.label.IndexOf("rocket launcher", StringComparison.OrdinalIgnoreCase) >= 0;
}

// 实验服一类：defName 含 LABCOAT（覆盖原生 Apparel_LabCoat 与 MOD 扩展）
public static bool IsLabCoat(Thing thing)
{
    return thing?.def != null
        && thing.def.defName.IndexOf("LABCOAT", StringComparison.OrdinalIgnoreCase) >= 0;
}
```

### 第 2 步：防具评分管线新增 Veto Scorer

新建文件：`Source/AutoEverything/AutoEquipment/Scoring/Apparels/ApparelForbiddenScorer.cs`

参考 `ApparelShieldBeltScorer.cs` 模板，统一 Veto 奴隶项圈 + 死气背包：

```csharp
public class ApparelForbiddenScorer : IScorer<Apparel>
{
    public string Name => "禁止类防具约束";

    public void Score(Pawn pawn, Apparel gear, Role role, GearContext context,
                      GearWeights weights, ScoreBreakdown breakdown)
    {
        // 奴隶项圈：非奴隶穿着无意义
        if (GearDefClassifier.IsSlaveCollar(gear) && !DLCCompat.IsSlave(pawn))
        {
            breakdown.Veto(-9999f);
            breakdown.AddScore(Name, "非奴隶+奴隶项圈=拒绝", -9999f);
            return;
        }
        // 死气背包：释放毒云伤友军，禁止自动穿
        if (GearDefClassifier.IsDeadlifePack(gear))
        {
            breakdown.Veto(-9999f);
            breakdown.AddScore(Name, "死气背包=拒绝(毒伤友军)", -9999f);
        }
    }
}
```

### 第 3 步：武器评分管线新增 Veto Scorer

新建文件：`Source/AutoEverything/AutoEquipment/Scoring/Weapons/WeaponForbiddenScorer.cs`

参考 `WeaponBiocodedScorer`/`ApparelShieldBeltScorer` 模板，Veto 手榴弹 + 火箭发射器：

```csharp
public class WeaponForbiddenScorer : IScorer<Thing>
{
    public string Name => "禁止类武器约束";

    public void Score(Pawn pawn, Thing gear, Role role, GearContext context,
                      GearWeights weights, ScoreBreakdown breakdown)
    {
        if (GearDefClassifier.IsDangerousWeapon(gear))
        {
            breakdown.Veto(-9000f);
            breakdown.AddScore(Name, "手榴弹/火箭发射器=拒绝(单次消耗品)", -9000f);
        }
    }
}
```

> 注：武器 Veto 用 -9000f 与现有武器 Veto 一致；防具 Veto 用 -9999f 与护盾腰带一致。调试时按分值辨识来源。

### 第 4 步：管线注册

文件：`Source/AutoEverything/AutoEquipment/Scoring/ScoringPipelineFactory.cs`

- 武器管线：在 `WeaponBiocodedScorer` 之后插入 `WeaponForbiddenScorer`（生物编码检查后立即拒绝禁止类，短路后续评分）
- 防具管线：在 `ApparelShieldBeltScorer` 之后插入 `ApparelForbiddenScorer`（护盾腰带约束后立即拒绝禁止类）

武器管线 9 → 10 个 Scorer；防具管线 13 → 14 个。

### 第 5 步：已穿奴隶项圈纠错

文件：`Source/AutoEverything/AutoEquipment/CompGearManager.cs`

参考 `RemoveWrongShieldBelt`（L686-709）实现 `RemoveSlaveCollar`，在 `EvaluateApparel` 中 `RemoveWrongShieldBelt(role)` 旁并列调用：

```csharp
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
            Log.Message($"[AutoEverything] {AEDebug.Label(Pawn)} 卸下奴隶项圈 '{ap.LabelShort}' (非奴隶不应穿戴)");
        }
        return;
    }
}
```

> 注：危险武器/危险防具（手榴弹/火箭/死气背包）**不主动卸下**——玩家可能手动给，仅评分阶段不主动拾取。奴隶项圈是明确错误，主动卸下。

### 第 6 步：研究型殖民者偏好实验服

新建文件：`Source/AutoEverything/AutoEquipment/Scoring/Apparels/ApparelLabCoatScorer.cs`

**"研究型"判定**（非 Brawler + 近战远程均无火 + 医疗或研究 ≥ 8）：

```csharp
public class ApparelLabCoatScorer : IScorer<Apparel>
{
    public string Name => "实验服偏好";

    public void Score(Pawn pawn, Apparel gear, Role role, GearContext context,
                      GearWeights weights, ScoreBreakdown breakdown)
    {
        // 仅研究型殖民者生效：非 Brawler + 近战远程无火 + 医疗/研究高
        if (!IsResearchOriented(pawn)) return;
        if (!GearDefClassifier.IsLabCoat(gear)) return;

        breakdown.AddScore(Name, "研究型+实验服=偏好", 50f);
    }

    private static bool IsResearchOriented(Pawn pawn)
    {
        // 角色非 Brawler（轻甲工人/自由后排/医生等）
        if (role == Role.Brawler) return false;  // 注：role 由管线传入，此处需调整签名或外部判定

        // 近战和射击均无火（None）
        SkillRecord shooting = pawn.skills?.GetSkill(SkillDefOf.Shooting);
        SkillRecord melee = pawn.skills?.GetSkill(SkillDefOf.Melee);
        if (shooting == null || melee == null) return false;
        if (shooting.passion != Passion.None || melee.passion != Passion.None) return false;

        // 医疗 ≥ 8 或 研究 ≥ 8
        SkillRecord medical = pawn.skills?.GetSkill(SkillDefOf.Medicine);
        SkillRecord research = pawn.skills?.GetSkill(SkillDefOf.Intellectual);
        int medLevel = medical?.Level ?? 0;
        int resLevel = research?.Level ?? 0;
        return medLevel >= 8 || resLevel >= 8;
    }
}
```

> 注：`IsResearchOriented` 需要 `role` 参数，但 IScorer.Score 已传入 role，直接用即可（上面伪代码示意）。实际实现把 role 作为方法参数。

**管线注册**：在 `ApparelWorkScorer` 之后插入 `ApparelLabCoatScorer`（工作加分后追加实验服偏好）。

加分幅度 +50：参考 `WeaponSkillScorer` 双修角色远程武器 +50 偏好分，让实验服在研究型殖民者评分中显著领先同类防具。

### 第 7 步：README 同步

文件：`README.md`

更新以下章节（项目规则强制同步）：

1. **防具评分管线表格**：13 → 14 个 Scorer，新增 `ApparelShieldBeltScorer` 后的 `ApparelForbiddenScorer` 与 `ApparelWorkScorer` 后的 `ApparelLabCoatScorer`
2. **武器评分管线表格**：9 → 10 个 Scorer，新增 `WeaponForbiddenScorer`
3. **主武器选择规则**章节后新增"禁止类装备"小节，说明：奴隶项圈（非奴隶 Veto + 卸下）、死气背包（Veto）、手榴弹/火箭发射器（Veto）
4. **护甲偏好**章节后新增"研究型殖民者偏好"小节，说明：非 Brawler + 近战远程无火 + 医疗/研究 ≥ 8 → 实验服 +50 加分

### 第 8 步：编译验证

```bash
make rebuild-check
```

要求 0 警告 0 错误，DLL 生成。

## 关键文件清单

| 用途 | 文件路径 |
|------|---------|
| 装备识别（扩展） | `Source/AutoEverything/AutoEquipment/GearDefClassifier.cs` |
| 防具 Veto Scorer（新建） | `Source/AutoEverything/AutoEquipment/Scoring/Apparels/ApparelForbiddenScorer.cs` |
| 武器 Veto Scorer（新建） | `Source/AutoEverything/AutoEquipment/Scoring/Weapons/WeaponForbiddenScorer.cs` |
| 实验服 Scorer（新建） | `Source/AutoEverything/AutoEquipment/Scoring/Apparels/ApparelLabCoatScorer.cs` |
| 管线注册 | `Source/AutoEverything/AutoEquipment/Scoring/ScoringPipelineFactory.cs` |
| 已穿纠错 | `Source/AutoEverything/AutoEquipment/CompGearManager.cs`（参考 `RemoveWrongShieldBelt` L686） |
| 文档同步 | `README.md` |

## 复用的现有模式

- `ApparelShieldBeltScorer`（Veto Scorer 模板）
- `RemoveWrongShieldBelt`（已穿纠错模板：`apparel.Remove` + `GenDrop.TryDropSpawn`）
- `DLCCompat.IsSlave(Pawn)`（奴隶判定）
- `GearDefClassifier` 的 `IndexOf(OrdinalIgnoreCase)` 识别模式
- `WeaponTraitScorer` 的 `Veto(-9000f)` 武器 Veto 分值

## 验证方式

1. `make rebuild-check` 通过（0 警告 0 错误）
2. 游戏内验证（可选）：
   - 非奴隶殖民者身上有奴隶项圈 → 下次 EvaluateApparel 周期被卸下
   - 地图上有手榴弹/火箭/死气背包 → 评分阶段被 Veto，不被自动拾取
   - 近战远程无火 + 医疗 ≥ 8 的殖民者 → 评分阶段优先穿实验服
