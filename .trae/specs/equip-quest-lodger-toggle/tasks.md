# Tasks：自动装备排除任务临时成员（难民等）

- [x] T1 `Core/AESettings.Equip.cs`：新增 `equipAllowQuestLodgers`（默认 `false`）与存档键 `ae_equipAllowQuestLodgers`，接入 `ExposeEquipData`。
- [x] T2 `AutoEquip/EquipPolicy.cs`：新增纯函数 `ShouldSkipQuestLodger(bool isQuestLodger, bool allowQuestLodgers)`。
- [x] T3 `AutoEquip/EquipAllocator.cs`：`ShouldSkipPawn` 在殖民者判定后调用 `QuestUtility.IsQuestLodger` + `EquipPolicy.ShouldSkipQuestLodger`，对周期/脏标/立即装备统一生效。
- [x] T4 `Test/AutoEverything.Tests/EquipPolicyTests.cs`：新增真值表用例（默认 false→跳过、允许 true→放行、非临时成员→不跳过）。
- [x] T5 `UI/MainTabWindow_AutoEverything.cs`：自动装备页总开关下方新增勾选框「允许为难民等临时成员装备」。
- [x] T6 `Languages/{ChineseSimplified,English}/Keyed/AE_Keyed.xml`：新增 `AE_CFG_EquipAllowLodgers` 与 `AE_TT_EquipAllowLodgers`。
- [x] T7 `README.md`「自动装备 / 执行与过滤」与设置清单、`CHANGELOG.md` `[Unreleased]` 同步新默认行为与开关。
- [x] T8 跑 `make test-check`（0 警告 0 错误 + 全部单测通过）。
