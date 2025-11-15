#!/bin/bash

# Godot MCP 服务器启动脚本

echo "======================================"
echo "  Godot MCP Server"
echo "======================================"
echo ""

# 检查 .NET SDK
if ! command -v dotnet &> /dev/null
then
    echo "错误: 未找到 .NET SDK"
    echo "请访问 https://dotnet.microsoft.com/download 安装 .NET 8.0 SDK"
    exit 1
fi

echo "✓ .NET SDK 版本: $(dotnet --version)"
echo ""

# 切换到 McpServer 目录
cd "$(dirname "$0")/McpServer"

# 检查是否需要还原依赖
if [ ! -d "obj" ]; then
    echo "正在还原 NuGet 包..."
    dotnet restore
    echo ""
fi

# 构建项目
echo "正在构建项目..."
dotnet build --configuration Release
if [ $? -ne 0 ]; then
    echo ""
    echo "错误: 构建失败"
    exit 1
fi

echo ""
echo "======================================"
echo "  MCP 服务器正在运行"
echo "  端口: 7777"
echo "  按 Ctrl+C 停止服务器"
echo "======================================"
echo ""

# 运行服务器
dotnet run --configuration Release
