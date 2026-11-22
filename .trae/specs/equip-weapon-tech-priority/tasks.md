# Tasks：自动装备武器选择修复

- [x] T1 纯逻辑：`WeaponScoreInput` 增加 `TechRank`，`WeaponScorer` 写入 `ThingDef.techLevel`
- [x] T2 纯逻辑：`EquipPolicy.CompareWeapon` 改为「一般武器：科技 > 战斗评分 > 品质 > defName」
- [x] T3 纯逻辑：新增 `ShouldSwapWeapon`，替换 `ComparableScore` 换装判断
- [x] T4 运行时：`ShouldSkipPawn` 跳过正在装备/穿戴/取物的殖民者
- [x] T5 运行时：`IsCandidateAvailable` 增加可达性检查
- [x] T6 单测：更新 `EquipPolicyTests`，覆盖科技优先、同科技阈值、特化互斥、无武器场景
- [x] T7 文档：README 选择优先级 / CHANGELOG `[Unreleased]` / 中英文提示
- [x] T8 门禁：`make test-check` 全绿（0 警告 0 错误，全部单测通过）
