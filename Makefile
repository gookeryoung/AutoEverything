# AutoEquipment MOD 构建脚本
# 使用 GNU Make 或 make for Windows 调用

# 项目路径
PROJECT := Source/AutoEquipment/AutoEquipment.csproj
CONFIG := Release
OUTPUT := Assemblies/AutoEquipment.dll

# .NET 命令
DOTNET := dotnet

.PHONY: all build clean restore rebuild help

# 默认目标：构建
all: build

# 构建项目
build:
	$(DOTNET) build -c $(CONFIG) $(PROJECT)

# 仅还原依赖（无 NuGet 包时几乎无操作，留作扩展）
restore:
	$(DOTNET) restore $(PROJECT)

# 清理构建产物
clean:
	$(DOTNET) clean -c $(CONFIG) $(PROJECT)
	@if exist "$(OUTPUT)" del /Q "$(OUTPUT)"

# 重新构建：先清理再构建
rebuild: clean build

# 查看可用目标
help:
	@echo AutoEquipment Makefile 目标:
	@echo   make build     构建项目 (默认)
	@echo   make clean     清理构建产物
	@echo   make rebuild   清理后重新构建
	@echo   make restore   还原 NuGet 依赖
	@echo   make help      显示此帮助信息
