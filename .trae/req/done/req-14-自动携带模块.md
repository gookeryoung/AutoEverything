# req-14 自动携带模块

## 需求清单

- [x] 新增【自动携带】模块（AutoCarry），勾选后殖民者自动携带食物 x3、活力水 x1、清醒丸 x1、思滞血清 x1
- [x] 判定时机：周期 6000 tick（约 100 秒）派发 HaulJob，战斗中暂停触发（与 AutoWork 一致）
- [x] 适用人群：仅自由殖民者（不含奴隶、不含食尸鬼、不含机械族机器人）
- [x] 食物优先级：包装食物 → 干粮 → 奢侈餐 → 精致餐 → 一般餐
- [x] 殖民者判定：不需要睡眠不带清醒丸；禁止活力水不带活力水
- [x] 排除：食尸鬼、机器人（机械族已通过 PawnSuitabilityChecker.CanManageGear 排除）
- [x] 战斗过滤：复用 AutoExecutor.AnyCombatActive，战斗中暂停派发 HaulJob
- [x] 医疗守卫：复用 PawnJobGuard.ShouldSkipForMedical，正在手术/休养的殖民者跳过
- [x] ITab 底部第 4 个勾选框（评级/工作/星标/携带），勾选时立即执行
- [x] 中英文翻译键完整
- [x] make check 零警告零错误通过
- [x] 单元测试覆盖食物选择优先级、殖民者适用性判定
