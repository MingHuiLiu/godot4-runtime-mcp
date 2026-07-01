# 🎮 Godot MCP 快速开始指南 (v2.0)

欢迎使用 **Godot MCP v2.0**！合并后的插件版本，**无需 .NET SDK**，一个 Godot 插件自包含 MCP 服务。

## ⚡ 前置要求

在开始之前，请确保已安装：

- ✅ [Godot 4.x (C# 版本)](https://godotengine.org/download)
- ✅ [Claude Desktop](https://claude.ai/desktop)（或其他 MCP 客户端）

> 💡 **不再需要 .NET SDK！** 合并后的 Godot 插件直接运行在 Godot 引擎内。

---

## 🚀 新架构（推荐）

### 📦 步骤 1: 安装 Godot 插件

1. 将 `addons/godot_mcp/` 文件夹复制到你的 Godot 项目的 `addons/` 目录
2. 在 Godot 编辑器中: **项目 → 项目设置 → 插件 → 启用 "Godot MCP Server"**
3. 运行你的 Godot 项目

启动后控制台会显示：
```
============================================================
[GodotMcp] Godot MCP v2.0 — Unified HTTP+SSE MCP Server
[GodotMcp] Merged McpServer + GodotPlugin into one addon
============================================================
[GodotMcp] 48 MCP tools loaded
[GodotMcp] ✓ MCP HTTP+SSE server: http://127.0.0.1:7777/
[GodotMcp]   SSE endpoint:  GET  /sse
[GodotMcp]   MCP endpoint:  POST /messages?session_id=...
```

### 🔧 步骤 2: 配置 Claude Desktop

#### 方式 A: 使用 Stdio Bridge（推荐 for Claude Desktop）

```bash
# 给桥接脚本添加执行权限
chmod +x start-mcp-bridge.sh

# 测试连接（确保 Godot 正在运行）
python3 start-mcp-bridge.sh
```

编辑 `claude_desktop_config.json`:
```json
{
  "mcpServers": {
    "godot": {
      "command": "/ABSOLUTE/PATH/TO/start-mcp-bridge.sh",
      "args": []
    }
  }
}
```

#### 方式 B: 直接 HTTP+SSE（for 支持 HTTP 的 Agent）

Agent 直接连接到:
- **SSE 端点**: `http://127.0.0.1:7777/sse`
- **消息端点**: `POST http://127.0.0.1:7777/messages?session_id=X`

---

## 📦 旧架构（v1.x，已废弃）

旧架构需要 .NET SDK 和独立的 `McpServer` 进程。仅供现有项目参考。

### 前置要求
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download)
- Godot 4.x (C# 版本)

### 配置 Claude Desktop（旧方式）
```json
{
  "mcpServers": {
    "godot": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "/path/to/McpServer/McpServer.csproj"
      ]
    }
  }
}
```

---

## 🧪 测试连接

无论哪种方式，都可以用 curl 测试 Godot API：

```bash
curl -X POST http://127.0.0.1:7777/get_time \
  -H "Content-Type: application/json" \
  -d '{}'

curl -X POST http://127.0.0.1:7777/get_scene_tree \
  -H "Content-Type: application/json" \
  -d '{"includeProperties": false}'
```

## 📋 可用工具

插件提供 48 个 MCP 工具，覆盖：
- **场景树操作** (17个): 获取/创建/删除节点、搜索、统计
- **属性操作** (3个): 读写节点属性
- **方法调用** (2个): 调用节点方法
- **资源管理** (3个): 列出/加载/查询资源
- **脚本执行** (2个): C# 代码执行、全局变量
- **信号系统** (9个): 监听/查询/连接信号
- **调试工具** (9个): 日志、性能、截图、时间
- **信号事件** (3个): 全局历史信号查询

## 🔍 更多信息

- [架构说明](ARCHITECTURE_V5.1.md) - 详细了解架构设计
- [HTTP API 指南](HTTP_API_GUIDE.md) - HTTP API 参考
- [场景查询工具](SCENE_QUERY_TOOLS.md) - 场景树查询详情
```

**Windows:**
```cmd
explorer %APPDATA%\Claude
```

### 编辑 claude_desktop_config.json

创建或编辑 `claude_desktop_config.json`，添加以下内容：

**macOS/Linux:**
```json
{
  "mcpServers": {
    "godot": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "/Users/arviter/Projects/Godot-Mcp/McpServer/McpServer.csproj"
      ]
    }
  }
}
```

**Windows:**
```json
{
  "mcpServers": {
    "godot": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "C:\\Path\\To\\Godot-Mcp\\McpServer\\McpServer.csproj"
      ]
    }
  }
}
```

⚠️ **重要**: 将路径替换为你的实际项目路径！

## 🎯 步骤 3: 设置 Godot 项目

### 选项 A: 新建 Godot 项目

1. 打开 Godot 编辑器
2. 创建新项目，选择 **C#** 模板
3. 保存项目

### 选项 B: 使用现有项目

确保项目已启用 C# 支持。

### 安装插件

```bash
# 复制插件到你的 Godot 项目
cp -r /Users/arviter/Projects/Godot-Mcp/GodotPlugin \
     /path/to/your/godot/project/addons/mcp_client
```

或者手动复制 `GodotPlugin` 文件夹到项目的 `addons/mcp_client/` 目录。

### 启用插件

1. 在 Godot 编辑器中打开你的项目
2. 点击 **项目 → 项目设置 → 插件**
3. 找到 **MCP Client** 并勾选启用

## 🚀 步骤 4: 启动和测试

### 1. 重启 Claude Desktop

完全退出并重新启动 Claude Desktop 以加载新配置。

### 2. 运行 Godot 游戏

在 Godot 编辑器中按 **F5** 运行游戏。

### 3. 检查连接

在 Godot 控制台中应该看到：

```
MCP Plugin 已启用
MCP Client 已启动
已连接到 MCP 服务器 127.0.0.1:7777
```

### 4. 在 Claude 中测试

打开 Claude Desktop，尝试以下命令：

**测试 1: 查看可用工具**
```
列出所有可用的 Godot MCP 工具
```

**测试 2: 获取场景树**
```
获取当前 Godot 游戏的场景树结构
```

**测试 3: 获取性能信息**
```
获取游戏的性能统计信息
```

## ✅ 验证成功

如果一切正常，你应该：

1. ✅ Claude 能够列出 Godot MCP 工具
2. ✅ Claude 能够获取场景树信息
3. ✅ Claude 能够读取和修改节点属性
4. ✅ Godot 控制台显示 MCP 客户端已连接

## 🎨 创建测试场景

创建一个简单的测试场景来体验 MCP 的强大功能：

### 在 Godot 中创建场景

```
Main (Node2D)
├── Player (CharacterBody2D)
│   └── Sprite2D
└── Camera2D
```

### 运行游戏后，在 Claude 中尝试：

**查看玩家信息**
```
获取节点 /root/Main/Player 的详细信息
```

**修改玩家位置**
```
将 /root/Main/Player 的 position 设置为 {"x": 100, "y": 200}
```

**创建新节点**
```
在 /root/Main 下创建一个名为 "Enemy" 的 Node2D 节点
```

**截图**
```
截取当前游戏画面
```

## 🐛 故障排除

### 问题: Claude 没有显示 Godot 工具

**解决方案:**
1. 确认已重启 Claude Desktop
2. 检查配置文件路径是否正确
3. 查看 Claude Desktop 日志（帮助 → 查看日志）

### 问题: Godot 显示无法连接到 MCP 服务器

**解决方案:**
1. 检查 MCP 服务器是否在运行
2. 确认端口 7777 未被占用：
   ```bash
   # macOS/Linux
   lsof -i :7777
   
   # Windows
   netstat -ano | findstr :7777
   ```
3. 查看 Godot 控制台的错误信息

### 问题: 找不到节点

**解决方案:**
1. 先使用 `get_scene_tree` 查看完整的场景树
2. 确保使用正确的节点路径（区分大小写）
3. 节点路径必须以 `/root` 开头

### 问题: MCP 服务器构建失败

**解决方案:**
```bash
# 清理并重新构建
cd McpServer
dotnet clean
dotnet restore
dotnet build
```

## 📚 下一步

恭喜！你已经成功设置了 Godot MCP。现在可以：

1. 📖 阅读 [完整使用指南](USAGE.md)
2. 🔍 查看 [所有可用工具](USAGE.md#mcp-工具完整列表)
3. 🛠️ 学习 [如何扩展功能](DEVELOPMENT.md#扩展开发)
4. 💡 探索 [示例项目](ExampleProject/README.md)

## 🆘 获取帮助

遇到问题？

- 查看 [完整文档](README.md)
- 阅读 [故障排除指南](USAGE.md#故障排除)
- 查看 [开发者文档](DEVELOPMENT.md)

## 🎉 开始探索

现在你可以使用 AI 助手来：

- 🔍 **实时调试** - 在游戏运行时检查和修改状态
- ⚡ **快速迭代** - 无需重启即可测试更改
- 🎮 **智能开发** - 让 AI 帮助你理解和优化游戏
- 🐛 **高效排错** - 快速定位和修复问题

玩得开心！🚀
