#!/bin/bash

# Godot MCP v2.0 — 启动脚本
# ============================
# 注意: v2.0 不再需要独立的 .NET MCP 服务器！
# Godot MCP 现在作为 Godot 插件直接运行。
#
# 使用方法:
# 1. 将 addons/godot_mcp/ 复制到 Godot 项目的 addons/ 目录
# 2. 在 Godot 编辑器中启用插件
# 3. 运行 Godot 项目
#
# 如需兼容 Claude Desktop (stdio 模式)，请使用:
#   ./start-mcp-bridge.sh
# ============================

echo "======================================"
echo "  Godot MCP Server v2.0"
echo "======================================"
echo ""
echo "✅ v2.0 不再需要 .NET SDK！"
echo ""
echo "请按照以下步骤操作:"
echo ""
echo "1. 将 addons/godot_mcp/ 文件夹复制到你的 Godot 项目:"
echo "   cp -r addons/godot_mcp/ /path/to/your/godot_project/addons/"
echo ""
echo "2. 在 Godot 编辑器中启用插件:"
echo "   项目设置 → 插件 → 启用 'Godot MCP Server'"
echo ""
echo "3. 运行你的 Godot 项目"
echo ""
echo "4. 测试连接:"
echo "   curl -X POST http://127.0.0.1:7777/get_time \\"
echo "     -H \"Content-Type: application/json\" \\"
echo "     -d '{}'"
echo ""
echo "5. (可选) 如需 Claude Desktop 兼容:"
echo "   ./start-mcp-bridge.sh"
echo "======================================"

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
