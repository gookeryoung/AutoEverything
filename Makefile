# AutoEverything MOD 构建脚本
# 使用 GNU Make 或 make for Windows 调用

# 项目路径
PROJECT := Source/AutoEverything/AutoEverything.csproj
TEST_PROJECT := Test/AutoEverything.Tests/AutoEverything.Tests.csproj
CONFIG := Release
OUTPUT := Assemblies/AutoEverything.dll

# .NET 命令
# -clp:Force 强制刷新控制台输出，避免在 Trae IDE 终端中缓冲卡死
DOTNET := dotnet
DOTNET_FLAGS := -clp:Force

# 发布产物目录
DIST := dist
# 发布 zip 包含的 MOD 文件/目录（排除开发文件）
RELEASE_FILES := About Assemblies Defs Languages Patches Textures README.md CHANGELOG.md

# 当前版本号（从 About.xml 读取，供 release target 使用）
VERSION := $(shell powershell -NoProfile -Command "(Select-Xml -Path About/About.xml -XPath '//modVersion').Node.InnerText" 2>nul)

.PHONY: all build check clean restore rebuild rebuild-check test test-check help bump patch minor major release push

# 默认目标：构建
all: build

# 构建项目
build:
	$(DOTNET) build -c $(CONFIG) $(DOTNET_FLAGS) $(PROJECT)

# 检查：零警告零错误，用于改动后强制验证
# 规则依据 .trae/rules/rimworld-mod-dev.md "通用工作流" 条款
# -warnaserror:将警告升级为错误，任何警告都会导致非零退出码
# -nologo:抑制版权信息，便于日志阅读
# -clp:Force:强制控制台输出，避免 Trae 终端缓冲卡死
check:
	echo "[check] Check errors..."
	$(DOTNET) build -c $(CONFIG) -warnaserror -nologo $(DOTNET_FLAGS) $(PROJECT)
	echo "[check] PASS: No errors"

# 仅还原依赖（无 NuGet 包时几乎无操作，留作扩展）
restore:
	$(DOTNET) restore $(PROJECT)

# 清理构建产物
clean:
	$(DOTNET) clean -c $(CONFIG) $(DOTNET_FLAGS) $(PROJECT)
	@if exist "$(OUTPUT)" del /Q "$(OUTPUT)"

# 重新构建：先清理再构建
rebuild: clean build

# 重新构建后检查
rebuild-check: clean check

# 构建并运行单元测试（零依赖控制台运行器，不使用 xunit/NUnit）
# 测试项目通过 ProjectReference 引用主项目，自动处理构建顺序
test:
	$(DOTNET) build -c $(CONFIG) $(DOTNET_FLAGS) $(TEST_PROJECT)
	$(DOTNET) run -c $(CONFIG) --no-build --project $(TEST_PROJECT)

# 检查 + 测试：完整门禁（规则强制 check 通过 + 单元测试通过）
test-check: check test

# ════════════════════════════════════════════════════════════
# 版本管理（bump-my-version）
# ════════════════════════════════════════════════════════════

# 版本号 bump（默认 patch，用法: make bump [minor|major]）
# 自动更新 About.xml modVersion + csproj Version + commit + tag
# bump 前请在 CHANGELOG.md [Unreleased] 下补充变更说明
BUMP_PART := $(filter-out bump,$(MAKECMDGOALS))

bump:
	@echo "[bump] 当前版本: $(VERSION)"
	@uvx bump-my-version bump $(if $(BUMP_PART),$(firstword $(BUMP_PART)),patch) --tag --allow-dirty
	@echo "[bump] 完成，新 tag 已创建"

patch minor major:
	@:

# ════════════════════════════════════════════════════════════
# 发布
# ════════════════════════════════════════════════════════════

# 打包发布 zip（先跑 check 确保质量，再打包 MOD 文件）
release: check
	@echo "[release] 准备打包版本 $(VERSION)..."
	@if exist $(DIST) rmdir /s /q $(DIST)
	@mkdir $(DIST)
	@powershell -NoProfile -Command "$$files = '@(About,Assemblies,Defs,Languages,Patches,Textures,README.md,CHANGELOG.md)' -split ','; $$exist = @(); foreach ($$f in $$files) { if (Test-Path $$f) { $$exist += $$f } }; Compress-Archive -Path $$exist -DestinationPath $(DIST)/AutoEverything-v$(VERSION).zip -Force"
	@echo "[release] 完成: $(DIST)/AutoEverything-v$(VERSION).zip"

# 推送代码与所有 tag 到 origin
push:
	@git push origin
	@git push origin --tags
	@echo "[push] 代码与 tag 已推送"

# ════════════════════════════════════════════════════════════
# 帮助
# ════════════════════════════════════════════════════════════

help:
	echo AutoEverything Makefile 目标:
	echo   make build         构建项目 (默认)
	echo   make check         验证零警告零错误 (规则强制)
	echo   make clean         清理构建产物
	echo   make rebuild       清理后重新构建
	echo   make rebuild-check 清理后重新构建并验证
	echo   make restore       还原 NuGet 依赖
	echo   make test          构建并运行单元测试
	echo   make test-check    check + test 完整门禁
	echo   make bump          版本号 patch bump (默认)
	echo   make bump minor    版本号 minor bump
	echo   make bump major    版本号 major bump
	echo   make release       打包发布 zip 到 dist/
	echo   make push          推送代码与 tag 到 origin
	echo   make help          显示此帮助信息
