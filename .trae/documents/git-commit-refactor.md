# Git 提交计划：项目模块化重构

## 摘要

将上一会话完成的「自动装备→自动万物」改名与项目模块化重构成果提交为单个 commit。

## 当前状态分析

### Git 状态（已核实）

- **分支**：`main`，领先 `origin/main` 4 个 commit（已包含翻译改名 commit `e3bd296`）
- **已暂存**（staged）：22 个新 Scoring 文件 + 4 个 Allocation 文件 + `.trae/rules/autoeverything-project.md` + `Patches/AE_Patches.xml`
- **未暂存**（unstaged）：
  - 修改：`README.md` + 17 个 Core/RoleEvaluation/AutoEquipment/UI 文件
  - 删除：19 个旧位置 Scoring 文件（`Source/AutoEverything/Scoring/...`）
- **未追踪**（untracked）：11 个新位置 Scoring 文件（GearPolicyEngine、GearPreset、GearWeights、IScorer、ScoreBreakdown、ScoringPipeline、ScoringPipelineFactory、WeaponRangeHelper、WeaponRangeScorer、WeaponSkillScorer、WeaponTraitScorer）

### 工作内容回顾

本次提交涵盖上一会话完成的全部重构工作：

1. **显示名改名**：玩家可见字符串「自动装备」→「自动万物」（翻译键、About.xml、README、规则文件、代码注释）
2. **项目模块化**：53 个 .cs 文件按功能拆分到 8 个文件夹
   - `Core/`（10 文件）：MOD 入口、设置、调试、DLC 兼容、适配性检查
   - `RoleEvaluation/`（4 文件）：角色与情境评价、战斗价值计算
   - `AutoEquipment/`（3 文件 + Scoring 子树）：装备评分系统
   - `Allocation/`（4 文件）：全局分配策略
   - `UI/`（3 文件）：玩家界面
3. **文件拆分**：
   - `SidearmAllocator.cs` → `CombatEvaluator.cs`（RoleEvaluation）+ `SidearmAllocator.cs`（Allocation）+ `CombatTier.cs`（Core）
   - `SGSettings.cs` → `AESettings.cs` + `ColonistBarSortMode.cs` + `AutoEverythingMod.cs`（Core）+ `PresetDetailsWindow.cs`（UI）
   - `DebugHelper.cs` → `AEDebug.cs`（重命名）
4. **命名空间更新**：所有文件 namespace 与文件夹结构匹配（IDE0130）
5. **跨命名空间 using 修复**：手动添加缺失的 using 语句
6. **Patches/AE_Patches.xml**：ITab 类型名带新命名空间前缀
7. **文档同步**：README.md 目录结构图、规则文件命名空间映射表

### 验证状态

- 上一会话 `make rebuild-check` 通过：0 警告 0 错误，输出 `AutoEverything.dll`（104,448 字节）
- 本次提交前需重新运行 `make check` 确认当前工作区状态

## 提交方案

### 步骤 1：运行 make check 验证编译

```bash
make check
```

**预期**：零警告零错误，输出 `Assemblies/AutoEverything.dll`。
**失败处理**：如有错误，停止提交流程，先修复问题。

### 步骤 2：暂存所有变更

由于变更分布在 staged/unstaged/untracked 三种状态，需要统一暂存：

```bash
git add -A
```

**说明**：`-A` 会暂存所有修改、删除、新增。本次重构涉及大量文件移动（旧位置删除 + 新位置新增），`-A` 能正确处理 rename detection。

**安全检查**：暂存后用 `git status` 确认无意外文件（如 `.env`、编译产物 `obj/`、`bin/`）被包含。当前 `.gitignore` 应已排除编译产物。

### 步骤 3：创建 commit

**提交信息**（遵循项目规则：中文 + 类型前缀 + 简洁）：

```
refactor: 项目结构模块化拆分，显示名改为自动万物
```

**理由**：
- 类型 `refactor`：本次变更不涉及功能逻辑改动，仅是结构调整与命名变更
- 描述「项目结构模块化拆分」：概括 Core/RoleEvaluation/AutoEquipment/Allocation/UI 的拆分
- 「显示名改为自动万物」：概括玩家可见字符串的改名
- 长度 22 字符（描述部分），符合 ≤50 字符规则

**命令**：

```bash
git commit -m "$(cat <<'EOF'
refactor: 项目结构模块化拆分，显示名改为自动万物
EOF
)"
```

### 步骤 4：验证提交成功

```bash
git status
git log --oneline -5
```

**预期**：
- `git status`：`nothing to commit, working tree clean`
- `git log`：最新 commit 为本次重构，message 正确

### 步骤 5：不推送（用户仅要求提交）

用户选择「Git 提交本次重构」，未要求推送。提交完成后报告状态，由用户决定是否推送。

## 假设与决策

### 假设

1. **上一会话的工作完整且正确**：基于总结描述与本次 Glob/Grep 验证，53 个 .cs 文件已就位，显示名已更新
2. **`make check` 仍会通过**：上一会话已验证，本次仅是重新确认
3. **`.gitignore` 已正确配置**：排除 `obj/`、`bin/`、`Assemblies/*.dll`（除已追踪的）

### 决策

1. **使用 `git add -A`**：因变更分散在三种状态，统一暂存最简洁；已通过 `.gitignore` 排除编译产物
2. **不使用 pyflowx 工具**：项目规则提到 `uvx --from pyflowx gitt a` / `pymake p`，但：
   - `gitt a` 等价于 `git add -A`，直接用 git 更可控
   - `pymake p` 可能包含 push 操作，用户仅要求提交，不推送
   - 标准 git 命令更透明，便于审查
3. **单 commit**：符合上一会话计划的「一次性完成（单 commit）」约定
4. **commit message 不含 HEREDOC 多行**：本次提交信息简短，单行即可

## 验证步骤

- [ ] `make check` 通过（零警告零错误）
- [ ] `git add -A` 后 `git status` 显示所有变更已暂存，无意外文件
- [ ] `git commit` 成功，commit message 正确
- [ ] `git status` 显示 `working tree clean`
- [ ] `git log --oneline -5` 显示新 commit 在顶部

## 风险与回滚

### 风险

- **低风险**：本次仅是 git 提交操作，不修改任何代码
- **编译失败风险**：如 `make check` 失败，需先修复；但上一会话已验证通过

### 回滚

如提交后发现问题：

```bash
git reset HEAD~1
```

会撤销 commit 但保留所有变更在工作区（mixed 模式，默认）。
