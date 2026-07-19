# 高价值标记范围扩大

## 变动日期

2026-07-19

## 变动背景

用户要求扩展 AutoMarkPawn 模块功能：
1. 标记范围从"非殖民者人类"扩大至"所有人类like 单位"（含殖民者、奴隶、囚犯、敌对、中立/盟友、野生）
2. 星标按类别区分颜色（殖民者=金、奴隶=橙、囚犯=黄、敌对=红、中立/盟友=青、野生=白）
3. 勾选框重命名为"高价值自动标记"，切换勾选时立即全局重扫描并弹消息列出所有当前高价值单位
4. 勾选状态下当地图人员变动时自动扫描新增单位，发现新高价值目标时弹消息提示

规则文件中仍残留对旧版"非殖民者"的引用，与代码实际行为不一致，需同步更新。

## 变动内容

### `.trae/rules/autoeverything-project.md` L43

- 旧：`- **AutoMarkPawn**：高价值非殖民者标记（S+ 档次头顶红色星标实时绘制，不修改 Pawn 数据）`
- 新：`- **AutoMarkPawn**：高价值自动标记（S+ 档次所有人类单位头顶彩色星标实时绘制，按类别区分颜色，事件驱动扫描，不修改 Pawn 数据）`
- 原因：范围扩大至所有人类like 单位，星标按类别区分颜色，新增人员变动事件驱动扫描。

### `.trae/rules/autoeverything-project.md` L138-139

- 旧：
  ```
  13. **高价值标记**（`PawnMarker.cs` / `AutoMarkPawn` 模块）
      - 同步章节：`### 高价值非殖民者标记（AutoMarkPawn）`
  ```
- 新：
  ```
  13. **高价值自动标记**（`PawnMarker.cs` / `AutoMarkPawn` 模块）
      - 同步章节：`### 高价值自动标记（AutoMarkPawn）`
  ```
- 原因：触发器名与 README 章节标题均同步重命名，"非殖民者"限定词已不准确。

### `.trae/rules/autoeverything-project.md` L181

- 旧：`  - 高价值标记（`autoMarkPawn`）`
- 新：`  - 高价值自动标记（`autoMarkPawn`）`
- 原因：ITab 勾选框标签同步重命名，与翻译键 `AE_AutoMarkPawn` 的实际显示文本一致。

## 影响范围

- 仅规则文件（文档），不影响代码编译与运行
- `make check` 通过验证

## 同步更新

- `project_memory.md` 追加本次规则文件变动记录（Rule File Update 章节）
- `README.md` 对应章节与表格已在前序改动中同步更新
