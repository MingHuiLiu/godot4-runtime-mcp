# Godot MCP Server v2.0 — 自包含 Godot 插件

> **合并版本!** `McpServer` (.NET) + `GodotPlugin` 已合并为单个 Godot 插件。
> **无需 .NET SDK!** 所有 MCP 功能直接运行在 Godot 引擎内。

一个用于 Godot 4 + C# 项目的完整 MCP (Model Context Protocol) 服务器，让 AI 助手通过标准的 MCP 协议直接与 Godot 游戏引擎通信。

## 🏗️ 架构变革

```
v1.x (旧):  Agent ←→ [.NET McpServer] ←HTTP→ [GodotPlugin] ←→ Godot
v2.0 (新):  Agent ←──── HTTP+SSE ──────→ [GodotMcpServer] ←→ Godot
                                           ↑ 一个插件搞定全部
```

## ✨ 核心功能

### 🌲 场景树查询 (17 个工具)
- 完整/简化场景树获取
- 模糊搜索 (名称、类型、组)
- 节点上下文 (父级、兄弟、子级)
- 多层祖先追溯
- 子树查询和统计

### ⚡ 信号系统 (9 个工具)
- 查询节点信号列表
- 查看信号连接关系
- 动态连接/断开信号
- 手动触发信号
- **全局自动信号监听** — 自动记录所有信号事件
- 信号事件历史查询 (支持时间范围)

### 📊 日志系统 (6 个工具)
- 环形缓冲区 (最近 1000 条)
- 自动溢出到文件持久化
- 多维度过滤 (级别、关键字、时间)
- 日志统计和导出
- 自定义日志标记

### 🎮 运行时控制
- 节点属性读写
- 创建/删除节点
- 场景加载/卸载
- C# 代码执行
- 方法调用

### 🔍 调试工具
- 性能统计
- 资源信息
- 全局变量
- 截图功能

## 📦 项目结构 (v2.0)

```
Godot-Mcp/
├── addons/godot_mcp/       # ✅ 合并后的 Godot MCP 插件
│   ├── plugin.cfg          # 插件配置
│   ├── GodotMcpPlugin.cs   # 编辑器插件注册
│   ├── GodotMcpServer.cs   # MCP HTTP+SSE 服务器 + 48 个工具
│   └── README.md           # 插件内嵌文档
├── McpServer/              # 📦 (旧) .NET 控制台应用 - 不再需要
├── GodotPlugin/            # 📦 (旧) Godot HTTP 插件 - 已合并
├── start-mcp-bridge.sh     # Stdio 桥接脚本 (for Claude Desktop)
├── test-godot-api.http     # HTTP 测试文件
├── test-new-features.http  # 新功能测试文件
└── 文档/                   # 7 个 Markdown 文档
```

## 🛠️ 安装和使用 (v2.0)

### 🎯 一步安装

1. 将 **`addons/godot_mcp/`** 文件夹复制到你的 Godot 项目的 `addons/` 目录
2. 在 Godot 编辑器启用插件: **项目设置 → 插件 → 启用 "Godot MCP Server"**
3. 运行你的 Godot 项目

> ✅ 无需 .NET SDK，无需独立进程，一个插件搞定全部！

### 🔌 配置 AI Agent

#### VSCode Copilot (支持 MCP)
VSCode 不支持直接 HTTP+SSE MCP，请使用 stdio 桥接:

```json
{
  "github.copilot.chat.mcp.enabled": true,
  "github.copilot.chat.mcp.servers": {
    "godot": {
      "command": "/path/to/start-mcp-bridge.sh",
      "args": []
    }
  }
}
```

#### Claude Desktop
```json
{
  "mcpServers": {
    "godot": {
      "command": "/path/to/start-mcp-bridge.sh",
      "args": []
    }
  }
}
```

#### 支持 HTTP+SSE 的 Agent
直接连接:
- **SSE**: `http://127.0.0.1:7777/sse`
- **消息**: `POST http://127.0.0.1:7777/messages?session_id=X`

### 🧪 测试连接

```bash
curl -X POST http://127.0.0.1:7777/get_time \
  -H "Content-Type: application/json" \
  -d '{}'
```

## 📝 MCP 工具总览 (48 个)

### 场景树工具 (17 个)
- `get_scene_tree` - 获取完整场景树 (含属性、方法、信号)
- `get_scene_tree_simple` - 获取简化场景树 (仅结构和类型)
- `get_node_info` - 获取节点详细信息
- `get_node_children` - 获取直接子节点列表
- `get_node_parent` - 获取父节点信息
- `find_nodes_by_type` - 按类型查找节点
- `find_nodes_by_name` - 按名称模糊搜索
- `find_nodes_by_group` - 按组查找节点
- `search_nodes` - 组合条件搜索 (名称+类型+组)
- `get_node_ancestors` - 获取多层父级链
- `get_node_context` - 获取节点上下文 (父、兄弟、子)
- `get_node_subtree` - 获取子树
- `node_exists` - 检查节点是否存在
- `get_scene_tree_stats` - 获取场景树统计

### 信号工具 (9 个) ✨
- `get_node_signals` - 获取节点的所有信号
- `get_signal_connections` - 查看信号连接情况
- `connect_signal` - 连接信号到方法
- `disconnect_signal` - 断开信号连接
- `emit_signal` - 发射信号 (测试用)
- `start_signal_monitoring` - 开始监听信号事件
- `stop_signal_monitoring` - 停止监听
- `get_signal_events` - 获取信号事件历史
- `clear_signal_events` - 清空信号事件

### 日志工具 (6 个) ✨
- `get_logs` - 获取最近日志
- `get_logs_filtered` - 过滤查询日志 (级别、关键字、时间)
- `get_log_stats` - 获取日志统计
- `export_logs` - 导出日志到文件
- `clear_logs` - 清空日志
- `add_custom_log` - 添加自定义日志标记

### 属性工具 (4 个)
- `get_property` - 获取节点属性值
- `set_property` - 设置节点属性值
- `list_properties` - 列出节点所有属性
- `list_methods` - 列出节点所有方法

### 方法调用工具 (2 个)
- `call_method` - 调用节点方法
- `execute_csharp` - 执行 C# 代码片段

### 资源工具 (5 个)
- `list_resources` - 列出资源目录
- `load_resource` - 加载资源
- `get_resource_info` - 获取资源详细信息
- `get_global_variables` - 获取全局变量
- `create_node` - 创建新节点

### 调试工具 (9 个)
- `delete_node` - 删除节点
- `load_scene` - 加载场景
- `get_performance_stats` - 获取性能统计
- `take_screenshot` - 截图
- `get_time` - 获取游戏时间

## 🎯 典型使用场景

### 1. 调试 UI 不更新问题

```
AI: 检查玩家血量信号是否连接
→ get_node_signals (查看 Player 信号)
→ get_signal_connections (检查 health_changed 连接)
→ start_signal_monitoring (开始监听)
→ [玩家受伤]
→ get_signal_events (查看事件)
→ 分析: 信号触发了,但 UI 更新方法有 bug
```

### 2. 性能分析

```
AI: 游戏卡顿排查
→ get_scene_tree_simple (快速浏览场景结构)
→ add_custom_log ("性能测试开始")
→ get_performance_stats (记录基线)
→ [运行游戏 5 分钟]
→ get_logs_filtered (level=error, 查找错误)
→ export_logs (导出完整报告)
→ 分析: 发现内存泄漏
```

### 3. 查找未知节点

```
AI: 找到敌人节点修改血量
→ search_nodes (namePattern="enemy", caseSensitive=false)
→ 找到 /root/Main/Enemies/Enemy1
→ get_node_context (查看上下文)
→ set_property (修改 health 属性)
→ add_custom_log ("修改敌人血量")
```

## 🔧 技术架构

### v5.0 架构图

```
VSCode Copilot
    ↓ stdio (MCP Protocol)
MCP Server (.NET 9.0)
    ├── 48 个 MCP 工具 (SceneTools, SignalTools, DebugTools...)
    ├── GodotClient (Refit 强类型 HTTP 客户端)
    └── IGodotApi (48 个 HTTP 端点定义)
        ↓ HTTP POST
Godot HTTP Server (127.0.0.1:7777)
    ├── 请求队列 (线程安全)
    ├── 主线程处理 (_Process)
    ├── 环形日志缓冲区 (1000 条)
    ├── 信号事件监听器
    └── 场景树 API (Godot Runtime)
```

### 核心特性

- ✅ **完全强类型** - 工具→Client→API→Godot 全链路类型安全
- ✅ **线程安全** - 请求队列 + 主线程执行场景树操作
- ✅ **Agent 友好** - 模糊搜索、简化响应、上下文查询
- ✅ **内存高效** - 环形缓冲区 + 文件溢出
- ✅ **实时监控** - 信号事件捕获 + 日志追踪

## 📊 技术栈

- **.NET 9.0** - MCP 服务器运行时
- **Godot 4.x + C#** - 游戏引擎
- **ModelContextProtocol SDK 0.4.0** - 官方 Microsoft MCP 实现
- **Refit 8.0.0** - 类型安全 HTTP 客户端
- **HTTP REST** - 通信协议
- **JSON** - 数据序列化

## 📈 项目统计

- **48 个 MCP 工具** (场景树 13 + 信号 9 + 日志 6 + 属性 4 + 方法 2 + 资源 5 + 调试 9)
- **48 个 HTTP 端点** (完全对应)
- **7 个文档文件** (总计 3000+ 行)
- **2000+ 行代码** (C# + Godot)

## 许可证

MIT License

---

## 📋 项目状态

✅ **v5.0 生产就绪** - 完整的调试系统,包含简化查询、信号监听、增强日志

**完成度**: 100% - 所有功能已实现并编译成功

查看 [项目完成总结](PROJECT_SUMMARY.md) 了解详细的实现清单。
