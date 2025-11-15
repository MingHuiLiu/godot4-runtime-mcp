# Godot MCP Server

一个用于 Godot 4 + C# 项目的 MCP (Model Context Protocol) 服务器，允许 AI 助手通过 MCP 协议获取和修改游戏运行时的信息，用于 AI 辅助开发和调试 Godot 游戏项目。

## 🚀 快速开始

**新手？** 查看 [5 分钟快速开始指南](QUICKSTART.md)

**文档导航：**
- � [完整文档索引](DOCUMENTATION_INDEX.md) - 所有文档的导航中心
- �📖 [完整使用指南](USAGE.md) - 详细的工具参考和使用示例
- 🔧 [项目集成指南](INTEGRATION.md) - 如何集成到现有项目
- 💻 [开发者文档](DEVELOPMENT.md) - 架构和扩展开发
- 📁 [项目结构说明](PROJECT_STRUCTURE.md) - 完整的文件结构参考
- 📋 [项目完成总结](PROJECT_SUMMARY.md) - 实现清单和功能列表
- 📝 [更新日志](CHANGELOG.md) - 版本历史

## 功能特性

### 运行时信息获取
- 获取场景树结构
- 查看节点属性和状态
- 获取资源信息
- 查看全局变量和单例
- 获取运行时性能指标

### 运行时修改
- 修改节点属性
- 创建和删除节点
- 加载和卸载场景
- 执行 C# 代码片段
- 调用节点方法

### 调试功能
- 查看日志输出
- 设置断点
- 变量监视
- 性能分析

## 项目结构

```
Godot-Mcp/
├── McpServer/              # MCP 服务器 (.NET 项目)
│   ├── Program.cs          # 服务器主程序
│   ├── McpServer.csproj    # 项目配置
│   ├── Models/             # 数据模型
│   ├── Handlers/           # MCP 工具处理器
│   └── Services/           # 服务层
├── GodotPlugin/            # Godot 插件 (C#)
│   ├── McpClient.cs        # MCP 客户端
│   ├── RuntimeBridge.cs    # 运行时桥接
│   └── plugin.cfg          # 插件配置
└── README.md
```

## 安装和使用

### 1. 构建 MCP 服务器

```bash
cd McpServer
dotnet build
```

### 2. 配置 Claude Desktop

在 `claude_desktop_config.json` 中添加：

```json
{
  "mcpServers": {
    "godot": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/Godot-Mcp/McpServer"]
    }
  }
}
```

### 3. 在 Godot 项目中安装插件

1. 将 `GodotPlugin` 文件夹复制到你的 Godot 项目的 `addons/` 目录下
2. 在 Godot 编辑器中启用插件：项目 -> 项目设置 -> 插件 -> 启用 "MCP Client"

### 4. 运行游戏

启动游戏后，插件会自动连接到 MCP 服务器，AI 助手即可通过 MCP 协议与游戏交互。

## MCP 工具列表

### 场景管理
- `get_scene_tree` - 获取当前场景树结构
- `get_node_info` - 获取指定节点的详细信息
- `create_node` - 创建新节点
- `delete_node` - 删除节点
- `load_scene` - 加载场景

### 属性操作
- `get_property` - 获取节点属性值
- `set_property` - 设置节点属性值
- `list_properties` - 列出节点所有属性

### 方法调用
- `call_method` - 调用节点方法
- `list_methods` - 列出节点所有方法

### 脚本执行
- `execute_csharp` - 执行 C# 代码片段
- `get_global_variables` - 获取全局变量

### 资源管理
- `list_resources` - 列出资源
- `load_resource` - 加载资源
- `get_resource_info` - 获取资源信息

### 调试工具
- `get_logs` - 获取日志输出
- `get_performance_stats` - 获取性能统计
- `take_screenshot` - 截图

## 技术栈

- .NET 8.0
- Godot 4.x with C#
- WebSocket (通信协议)
- JSON-RPC 2.0 (MCP 协议)

## 许可证

MIT License

---

## 📋 项目状态

✅ **生产就绪** - 所有核心功能已完整实现并测试

查看 [项目完成总结](PROJECT_SUMMARY.md) 了解详细的实现清单和功能列表。
