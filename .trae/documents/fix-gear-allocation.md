# 修复自动装备分配三个问题

## 用户反馈问题

1. **更换前需要清理已有装备，而不是无限拾取装备**：周期自动重配走 `ForceEvaluate` 路径，只做"升级检查"（newScore > wornScore × (1+threshold)），从不主动卸下已有装备；只有手动 `GlobalAllocator.ReallocateAll` 才会"放下所有装备 → 全局重配"。
2. **重甲前排穿轻甲，自由后排穿重甲**：`CompGearManager.EvaluateApparel`（Tick + ForceEvaluate 路径）完全不调用 `GetArmorPreference`，候选循环只看 `GearScorer.ScoreApparel`；只有 `GlobalAllocator.ReallocateApparel` 有偏好调整，且仅是 -1000 软惩罚（非 Veto）。
3. **低评分角色穿好装备，SS 角色穿差装备，无优先级**：`AutoExecutor.ExecuteGear` 按 `map.mapPawns.FreeColonistsSpawned` 原始顺序遍历，先到先得，无 CombatTier 排序；Tick 路径同样无全局排序。

## 当前状态分析

### 三条装备分配路径

| 路径 | 入口 | 是否清理已穿戴 | 是否护甲偏好 | 是否按评级排序 |
|------|------|----------------|--------------|----------------|
| 手动全局重配 | `GlobalAllocator.ReallocateAll` | ✅ 放下所有 | ✅ -1000 软惩罚 | ✅ 按 CombatValue 降序 |
| 周期自动 | `AutoExecutor.ExecuteGear` → `ForceEvaluate` | ❌ 仅升级 | ❌ 无 | ❌ 原始顺序 |
| Tick 路径 | `CompGearManager.CompTick` → `EvaluateApparel` | ❌ 仅升级 | ❌ 无 | ❌ 单 Pawn 独立 |

### 关键代码位置

- `AutoExecutor.cs` L201-244：`ExecuteGear` 方法，遍历 FreeColonistsSpawned 调用 `ForceEvaluate`
- `GlobalAllocator.cs` L55-120：`ReallocateAll`，无 silent 参数，含 3 处 `Log.Message`
- `GlobalAllocator.cs` L253-420：`ReallocateApparel`，L383-389 护甲偏好 -1000 惩罚
- `CompGearManager.cs` L504-639：`EvaluateApparel`，L551-616 候选循环，无护甲偏好检查
- `CompGearManager.cs` L689-712：`RemoveWrongShieldBelt`（作为新方法的模式参考）
- `CompGearManager.cs` L647-681：`TryFallbackApparel`，L668 `if (bd.Vetoed) continue` 处加入护甲偏好 Veto
- `PawnRole.cs` L219-235：`GetArmorPreference(role)` 返回 Heavy/Flexible/Light
- `AESettings.cs` L67-70：`heavyArmorSharpThreshold=0.4f`、`heavyArmorPenaltyForLight=-1000f`、`lightArmorPenaltyForHeavy=-1000f`、`heavyArmorMatchBonus=500f`

## 提议改动

### 改动 1：ExecuteGear 改用 GlobalAllocator.ReallocateAll

**文件**: `Source/AutoEverything/Core/AutoExecutor.cs`
**位置**: L201-244 `ExecuteGear` 方法
**改动**: 删除整个 foreach 遍历 ForceEvaluate 的逻辑，直接调用 `GlobalAllocator.ReallocateAll(silent: true)`
**原因**: 
- 周期自动重配必须与手动重配语义一致：先放下所有装备，按战斗价值降序全局重新分配
- `ReallocateAll` 内部已包含全部过滤逻辑（食尸鬼/不适用/Dead/Downed/奴隶/征召/锁定，见 L71-82），无需在 ExecuteGear 重复
- 奴隶过滤：ReallocateAll L75 已过滤奴隶，与 ExecuteGear 原 L216 一致
- 未成年：ReallocateAll 不过滤未成年，但 `ForceEvaluate` 的 `ReloadTarget.All` 对未成年会调用 `EvaluateWeapon`，原 ExecuteGear L222 对未成年只评估防具。**保留未成年特殊处理**：在调用 ReallocateAll 后，单独遍历未成年调用 `ForceEvaluate(Apparel)`。实际上更简单——ReallocateAll 内部对每个 Pawn 调用 `ForceEvaluate(Sidearm)` + `ForceEvaluate(Inventory)`（L115-116），这些对未成年是安全的（CompTick 已守卫）。武器与防具由 ReallocateWeapons/ReallocateApparel 统一处理，未成年会被分配武器——这违反"未成年无武器"规则。

**修正方案**：在 ReallocateAll 收集候选 Pawn 时过滤未成年（与奴隶、食尸鬼同级别过滤）。未成年不参与全局重配，由 Tick 路径的 `EvaluateApparel` 处理（CompTick L222 已限制未成年仅防具）。

```csharp
// ReallocateAll 候选收集循环新增：
if (DLCCompat.IsChild(pawn)) continue;  // 未成年不参与全局重配，由 Tick 路径仅评估防具
```

**ExecuteGear 简化后**:
```csharp
private static void ExecuteGear(int tick, bool showMessage)
{
    lastGearTick = tick;
    if (!AESettings.autoGearReallocate) return;
    try
    {
        int n = GlobalAllocator.ReallocateAll(silent: true);
        AEDebug.Log(() => $"[AutoExecutor] 自动装备重配: {n} 个殖民者 (tick={tick})");
        if (showMessage)
        {
            Messages.Message(
                "AE_AutoGearReallocateResult".Translate(n),
                MessageTypeDefOf.TaskCompletion);
        }
    }
    catch (Exception ex)
    {
        Log.ErrorOnce("[AutoEverything] 自动装备重配失败: " + ex.Message, GearErrorSalt);
    }
}
```

### 改动 2：ReallocateAll 增加 silent 参数

**文件**: `Source/AutoEverything/Allocation/GlobalAllocator.cs`
**位置**: L55 `ReallocateAll` 方法签名
**改动**: 
1. 签名改为 `public static int ReallocateAll(bool silent = false)`
2. 方法内 5 处 `Log.Message` 改为条件输出：`silent ? AEDebug.Log(() => msg) : Log.Message(msg)`
   - L153 `放下武器`
   - L156 `共 X 把武器已释放`
   - L160 `已禁用放下当前武器`
   - L230 `全局重配 #i 武器分配`
   - L234 `无可用武器`
   - L303 `共 X 件护甲已释放`
   - L415 `全局重配护甲 #i`
   - L419 `全局重配护甲完成`

**原因**: 周期自动重配每 3000 tick 执行一次，大量 `Log.Message` 会刷屏控制台；手动触发保留 `Log.Message` 供玩家调试。

### 改动 3：护甲偏好改为硬否决（Veto）

**文件**: `Source/AutoEverything/Allocation/GlobalAllocator.cs`
**位置**: L383-389 `ReallocateApparel` 护甲偏好调整
**改动**: 
```csharp
// 改动前：-1000 软惩罚
if (pref == ArmorPreference.Heavy && !isHeavy)
    score += AESettings.heavyArmorPenaltyForLight;       // -1000
else if (pref == ArmorPreference.Light && isHeavy)
    score += AESettings.lightArmorPenaltyForHeavy;       // -1000
else if ((pref == ArmorPreference.Heavy && isHeavy)
         || (pref == ArmorPreference.Light && !isHeavy))
    score += AESettings.heavyArmorMatchBonus;            // +500

// 改动后：硬否决 continue
if (pref == ArmorPreference.Heavy && !isHeavy) continue;  // Heavy 偏好拒绝轻甲
if (pref == ArmorPreference.Light && isHeavy) continue;   // Light 偏好拒绝重甲
if ((pref == ArmorPreference.Heavy && isHeavy)
    || (pref == ArmorPreference.Light && !isHeavy))
    score += AESettings.heavyArmorMatchBonus;            // 匹配奖励保留
```

**原因**: -1000 软惩罚在极端情况下（如轻甲评分天然 +2000）仍可能让 Heavy 偏好角色穿上轻甲；硬否决彻底杜绝。Flexible 偏好不调整（自由选择）。

### 改动 4：Tick 路径加入护甲偏好 Veto

**文件**: `Source/AutoEverything/AutoEquipment/CompGearManager.cs`
**位置**: L551-616 `EvaluateApparel` 候选循环
**改动**: 在 `ApparelUtility.HasPartsToWear` 检查后（L558 之后）加入护甲偏好 Veto：
```csharp
// 在 HasPartsToWear 检查后加入：
// 护甲偏好硬否决：Heavy 偏好拒绝轻甲，Light 偏好拒绝重甲
ArmorPreference pref = RoleDetector.GetArmorPreference(role);
float armorSharp = apparel.GetStatValue(StatDefOf.ArmorRating_Sharp);
bool isHeavyApparel = armorSharp >= AESettings.heavyArmorSharpThreshold;
if (pref == ArmorPreference.Heavy && !isHeavyApparel) continue;
if (pref == ArmorPreference.Light && isHeavyApparel) continue;
```

**原因**: Tick 路径（含 ForceEvaluate）是单 Pawn 评估，原本完全不检查护甲偏好，导致 Heavy 偏好角色在 Tick 路径拾取轻甲。注意：变量 `pref` 需在循环外计算一次避免重复调用。

**优化**: 将 `pref` 计算移到 foreach 循环之前（与 `role` 同级），避免每个候选护甲重复调用 `GetArmorPreference`。

### 改动 5：新增 RemoveWrongArmorType 纠错

**文件**: `Source/AutoEverything/AutoEquipment/CompGearManager.cs`
**位置**: 在 `RemoveWrongShieldBelt` 方法之后（L712 之后）
**新增方法**:
```csharp
/// <summary>
/// 检测并卸下护甲类型不匹配的已穿戴护甲。
/// Heavy 偏好角色（Brawler）穿戴轻甲时卸下——前排战士需重甲承担伤害。
/// Light 偏好角色（Worker/Doctor 等）穿戴重甲时卸下——工人需轻甲提高工作效率。
/// Flexible 偏好角色（Shooter/Hunter/Leader）不卸下——自由选择。
/// 复用 RemoveWrongShieldBelt 的卸下模式：apparel.Remove + GenDrop.TryDropSpawn。
/// 设计意图：Tick 路径下已穿戴的不匹配护甲需主动卸下，否则 EvaluateApparel 的
/// 升级阈值检查会因 newScore 不显著高于 wornScore 而保留不匹配护甲。
/// </summary>
private void RemoveWrongArmorType(Role role)
{
    ArmorPreference pref = RoleDetector.GetArmorPreference(role);
    if (pref == ArmorPreference.Flexible) return;  // 自由选择，不纠错
    if (Pawn.apparel?.WornApparel == null) return;

    List<Apparel> worn = Pawn.apparel.WornApparel;
    for (int i = worn.Count - 1; i >= 0; i--)
    {
        Apparel ap = worn[i];
        // 跳过腰带层（护盾腰带由 RemoveWrongShieldBelt 处理）
        if (ap.def.apparel?.bodyPartGroups == null) continue;
        bool isBeltLayer = false;
        for (int j = 0; j < ap.def.apparel.bodyPartGroups.Count; j++)
        {
            if (ap.def.apparel.bodyPartGroups[j] == BodyPartGroupDefOf.Torso)
            {
                isBeltLayer = false;
                break;
            }
        }
        // 简化：只检查躯干覆盖的护甲（衣物/防弹衣/重型护甲），跳过腰带/帽子等
        if (!ap.def.apparel.bodyPartGroups.Contains(BodyPartGroupDefOf.Torso)) continue;

        if (Pawn.apparel.IsLocked(ap)) continue;

        float armorSharp = ap.GetStatValue(StatDefOf.ArmorRating_Sharp);
        bool isHeavy = armorSharp >= AESettings.heavyArmorSharpThreshold;

        bool wrong = (pref == ArmorPreference.Heavy && !isHeavy)
                  || (pref == ArmorPreference.Light && isHeavy);
        if (!wrong) continue;

        Pawn.apparel.Remove(ap);
        Thing dropped;
        if (GenDrop.TryDropSpawn(ap, Pawn.Position, Pawn.Map, ThingPlaceMode.Near, out dropped))
        {
            Log.Message($"[AutoEverything] {AEDebug.Label(Pawn)} 卸下不匹配护甲 '{ap.LabelShort}' (role={role}, pref={pref}, isHeavy={isHeavy})");
        }
    }
}
```

**调用位置**: `EvaluateApparel` 中 `RemoveWrongShieldBelt(role)` 之后（L537 之后）：
```csharp
RemoveWrongShieldBelt(role);
RemoveWrongArmorType(role);  // 新增：卸下护甲类型不匹配的已穿戴护甲
```

**简化**: 上述方法体中 `isBeltLayer` 逻辑冗余（最终用 `Contains(Torso)` 判断），删除 `isBeltLayer` 部分，仅保留 `Contains(Torso)` 检查。

### 改动 6：TryFallbackApparel 加入护甲偏好 Veto

**文件**: `Source/AutoEverything/AutoEquipment/CompGearManager.cs`
**位置**: L668 `TryFallbackApparel` 的 `if (bd.Vetoed) continue;` 之后
**改动**:
```csharp
if (bd.Vetoed) continue;

// 护甲偏好硬否决：过渡防具也排除偏好不匹配的护甲
ArmorPreference pref = RoleDetector.GetArmorPreference(role);
float armorSharp = apparel.GetStatValue(StatDefOf.ArmorRating_Sharp);
bool isHeavyApparel = armorSharp >= AESettings.heavyArmorSharpThreshold;
if (pref == ArmorPreference.Heavy && !isHeavyApparel) continue;
if (pref == ArmorPreference.Light && isHeavyApparel) continue;
```

**优化**: 将 `pref` 计算移到 foreach 循环之前。

**原因**: 过渡防具兜底原本只排除 Veto（如护盾腰带），不排除护甲类型不匹配——导致 Heavy 偏好角色赤身时可能穿上轻甲过渡，下次评估因升级阈值无法替换。

## 假设与决策

### 假设
1. `BodyPartGroupDefOf.Torso` 是 RimWorld 原生 DefOf，无需 null 检查（与 `StatDefOf.ArmorRating_Sharp` 同级别）。
2. `ReallocateAll` 当前不过滤未成年，但 `ForceEvaluate(Sidearm)` + `ForceEvaluate(Inventory)` 对未成年安全（CompTick 守卫）。**决策**：在 ReallocateAll 候选收集时过滤未成年，避免 ReallocateWeapons 给未成年分配武器。
3. 奴隶已被 ReallocateAll L75 和 CompTick L132 过滤，不参与自动装备分配，问题 3 中"奴隶穿好装备"指玩家手动装备或历史残留，MOD 不干预。

### 决策
1. **护甲偏好用 Veto 而非软惩罚**：-1000 在极端情况下仍可能被超越，Veto 彻底杜绝"重甲前排穿轻甲"。
2. **ExecuteGear 直接复用 ReallocateAll**：避免维护两套分配逻辑，KISS 原则。
3. **保留 heavyArmorPenaltyForLight / lightArmorPenaltyForHeavy 设置字段**：改为 Veto 后这两个字段不再使用，但删除会破坏存档兼容（Scribe Key）。保留字段但标记为废弃，后续版本可清理。
4. **RemoveWrongArmorType 仅检查躯干护甲**：帽子/腰带等不影响护甲偏好判定，且护盾腰带已有 RemoveWrongShieldBelt 处理。

## 验证步骤

1. **编译验证**: `make check` 通过（0 警告 0 错误）
2. **README 同步**: 更新 README.md 装备分配章节：
   - 周期自动重配改为"放下所有装备全局重配"语义
   - 护甲偏好从"软惩罚"改为"硬否决"
   - 新增 `RemoveWrongArmorType` 纠错说明
3. **游戏内验证**:
   - 周期自动重配后，检查低评分殖民者是否让出好装备给高评分殖民者
   - 重甲前排（Brawler）是否穿重甲，自由后排（Shooter）是否不被强制穿重甲
   - 未成年不被分配武器
   - 奴隶不被自动装备/卸下
