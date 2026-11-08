# Tasks：修复「有格斗者特质且已勾选，却分到"其他"」

- [x] T1 `RoleEvaluation/PawnRole.cs`：把 `RoleGroupRules.IsRoleGroupCandidate` 改为 5 参数纯函数（isColonist / isSlaveOfColony / isSubhumanControlled / hasSkills / hasStory），并更新中文注释说明「不能用 pawn.IsColonist」的原因。
- [x] T2 `RoleEvaluation/PawnRole.cs`：`RoleDetector.DetectRoleGroup` 改用新签名，候选 = `IsColonist || IsSlaveOfColony || IsColonySubhumanPlayerControlled`；保留食尸鬼前置分支。
- [x] T3 `RoleEvaluation/PawnRole.cs`：在 `DetectRoleGroup` 末尾加调试日志（仅 `AEDebug.IsActive` 为真时输出 colonist/slave/subhuman/brawler/group）。
- [x] T4 `Test/AutoEverything.Tests/RoleGroupTests.cs`：新增 `IsRoleGroupCandidate` 真值表用例（含奴隶为 true 的回归用例，及缺 skills/story 为 false）。
- [x] T5 `Test/AutoEverything.Tests/RoleGroupTests.cs`：新增「奴隶 + 格斗者特质 → Front」端到端用例与「未勾选格斗者 → 不进前排」回归用例。
- [x] T6 README 同步：在「殖民者栏按角色分组」补充「参与范围」条目（自由殖民者 / 玩家奴隶 / 玩家控制亚人，含远行队与精神崩溃）。
- [x] T7 运行 `make check`（0 警告 0 错误）与 `make test`（All tests passed，RoleGroupTests 211/211）。
