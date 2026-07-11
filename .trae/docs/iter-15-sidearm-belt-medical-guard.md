# 迭代 15：SidearmAllocator/BeltAllocator 补充医疗守卫

## 本轮目标

修复 SidearmAllocator 和 BeltAllocator 的 CollectCandidatePawns 缺少
PawnJobGuard.ShouldSkipForMedical 检查的不一致问题。

## 根因分析

Tick 路径打断行为分析发现：SidearmAllocator.AssignSidearm 和 BeltAllocator.AssignBelt
都会调用 TryTakeOrderedJob 分配装备，但两者的 CollectCandidatePawns 入口未过滤
正在执行医疗 Job/休养的 Pawn。

对比其他全局重配入口（CompGearManager.CompTick/ForceEvaluate/EvaluateInventory、
GlobalAllocator.ReallocateAll、WorkAllocator.ReallocateAll）均有 PawnJobGuard 守卫，
SidearmAllocator/BeltAllocator 是唯一遗漏的两个入口。

实际风险较低（两者分别有 2000/3000 tick 全局周期门控，且分配目标需 Pawn 走过去拾取），
但为保持医疗守卫一致性，补充过滤。

## 改动文件清单

- `Source/AutoEverything/Allocation/SidearmAllocator.cs`：
  CollectCandidatePawns 在 `comp.locked` 检查后添加
  `if (PawnJobGuard.ShouldSkipForMedical(pawn)) continue;`
- `Source/AutoEverything/Allocation/BeltAllocator.cs`：
  CollectCandidatePawns 同上
- `README.md`：医疗守卫入口列表追加 `SidearmAllocator`/`BeltAllocator`
- `project_memory.md`：硬约束条目同步更新

## 关键决策与依据

### 守卫位置：CollectCandidatePawns 而非 AssignSidearm/AssignBelt

AllocateAllColonists 是同步执行的（同一 Tick 内完成收集+分配），在 CollectCandidatePawns
过滤即可确保候选池不含医疗中的 Pawn，与 GlobalAllocator/WorkAllocator 的守卫位置一致。

## 验证结果

- `make check` 通过（0 警告 0 错误）
