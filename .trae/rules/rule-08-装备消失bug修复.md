# 装备消失 Bug 修复

## 变动日期

2026-07-20

## 变动背景

用户反馈："勾选自动装备时，原有身上的装备消失了"。

通过 PowerShell 反射查询 RimWorld 1.6 `RimWorld.Pawn_ApparelTracker` 的实际 API 签名，确认根因：

- `Remove(Apparel)` 返回 `Void`：仅从 `WornApparel` 列表移除，**不 spawn apparel 到地面**。apparel 变成 unspawned 状态（相当于消失）。
- `TryDrop(Apparel)` 返回 `Boolean`：移除并 spawn apparel 到 pawn 位置，返回是否成功。
- `Wear(Apparel, Boolean dropReplacedApparel = True, Boolean locked = False)`：装备 apparel，`dropReplacedApparel=true` 时同层冲突会自动 drop 旧装备。

原代码 `TrySafeRemove` 误用 `Remove(Apparel)`，注释错误地声称"自动 spawn 到 pawn 位置"。实际流程：

1. `TrySafeRemove(pawn, currentWorn)` 调用 `Remove` → currentWorn 从 WornApparel 移除但**不 spawn**（消失）
2. `TrySafeRemove(wearer, best)` 调用 `Remove` → best 从 wearer 移除但**不 spawn**（消失）
3. `TrySafeEquip(pawn, best)` 检查 `if (!apparel.Spawned) return false` → 因 best 是 unspawned，**装备失败**
4. 尝试 `TrySafeEquip(pawn, currentWorn)` 装回 → currentWorn 也是 unspawned，**装回失败**
5. 结果：原穿戴者失去 currentWorn，新穿戴者也没穿上 best，两件装备都消失

## 变动内容

### `Source/AutoEverything/AutoEquipment/GearAllocator.cs`

#### `TrySafeRemove` 方法

- 旧：`pawn.apparel.Remove(apparel); return true;`
- 新：`return pawn.apparel.TryDrop(apparel);`
- 原因：`Remove` 不 spawn，`TryDrop` 才会 spawn apparel 到 pawn 位置。`TryDrop` 返回 `false` 时 apparel 仍在 WornApparel 里，原穿戴者未失去装备，调用方 `continue` 跳过该 apparel，无副作用。

#### `AllocateForPawn` 扒装失败恢复

- 旧：`if (!TrySafeRemove(wearer, best)) continue;`（currentWorn 已卸下，pawn 失去装备）
- 新：`if (!TrySafeRemove(wearer, best)) { if (currentWorn != null) TrySafeEquip(pawn, currentWorn); continue; }`
- 原因：扒装 best 失败时，把已卸下的 currentWorn 装回 pawn，避免 pawn 在本轮失去装备。与 `TrySafeEquip(pawn, best)` 失败时的恢复逻辑一致。

#### 代码注释

- 旧：`Pawn_ApparelTracker.Remove(Apparel) 直接卸下（不返回掉落物，自动 spawn 到 pawn 位置）`
- 新：`Pawn_ApparelTracker.TryDrop(Apparel) 卸下并 spawn apparel 到 pawn 位置（返回是否成功）` + `⚠️ 不能用 Remove(Apparel)：Remove 仅从 WornApparel 列表移除，不 spawn，apparel 会变成 unspawned 状态（消失）`

### `.trae/rules/autoeverything-project.md` L101

- 旧：`- **扒装流程**：先 \`TrySafeRemove\`（落地 spawn）→ \`MarkAllocated\` → 再 \`TrySafeEquip\`，单件失败 try-catch 隔离不阻塞整体`
- 新：补充 `TryDrop` API 说明、扒装失败恢复逻辑、`Remove` 不可用的警告
- 原因：规则文件需准确描述当前实现，避免后续开发再次误用 `Remove`。

### `README.md`

- 同步"扒装流程"章节，与规则文件一致

## 影响范围

- 代码：`GearAllocator.cs` 两处修改（`TrySafeRemove` + `AllocateForPawn` 扒装失败恢复）
- 文档：README.md + autoeverything-project.md 同步
- `make test-check` 完整门禁通过（编译零警告零错误 + 所有测试通过）

## 验证方法

- 编译验证：`make test-check` 通过
- 游戏内验证（待用户执行）：
  1. 给殖民者穿上一件护甲
  2. 勾选 ITab "自动装备"
  3. 检查原护甲是否仍在地上或被重新分配（不应消失）
  4. 检查新装备是否正确穿戴

## 同步更新

- `project_memory.md` 追加本次规则文件变动记录（Rule File Update 章节）+ Lessons Learned 章节
