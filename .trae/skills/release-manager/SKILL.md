---
name: release-manager
description: Use when a gate-green AutoEverything change is ready to publish. Bump version, tag, push and package only after the required human approvals.
---

# 发布（release-manager）

1. **前置**：`make test-check` 全绿；ADR 交接已回填；CHANGELOG `[Unreleased]` 已更新；README 同步清单已核对；Defs / 翻译改动已进游戏加载验证。
2. **预演**：`make release` 在本地打包 `dist/AutoEverything-v<版本>.zip`，检查包内为 `About/ Assemblies/ Defs/ Languages/ Patches/ Textures/ README.md CHANGELOG.md`。
3. **版本**：`make bump patch|minor|major`（bump-my-version 同步 `About/About.xml` 与 `.csproj` 并创建 tag）。
4. **人工卡点**：打 tag 前必须获得明确授权；未授权不 tag、不触发发布。
5. **推送**：`make push`（代码 + 所有 tag）；分支无 upstream 时先 `git push -u origin <branch>`。
6. **发布**：tag 触发 [release.yml](../../../.github/workflows/release.yml) 构建并创建 GitHub Release，检查产物版本号与 zip 内容。
7. **验证**：下载 release zip，按 About.xml 版本号与 `supportedVersions` 核对；失败先修根因再重新走授权流程。

## 铁律

- 不 force push、不直推 main/master、不打未授权 tag。
- 发布失败不重打同名 tag 覆盖；先撤销 / 修复，再重新申请授权。
