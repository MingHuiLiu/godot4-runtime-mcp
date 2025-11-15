# Godot MCP 使用指南

## 快速开始

### 1. 构建 MCP 服务器

```bash
cd McpServer
dotnet restore
dotnet build
```

### 2. 配置 Claude Desktop

编辑 Claude Desktop 配置文件：
- macOS: `~/Library/Application Support/Claude/claude_desktop_config.json`
- Windows: `%APPDATA%\Claude\claude_desktop_config.json`

添加以下配置：

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

### 3. 在 Godot 项目中安装插件

1. 将 `GodotPlugin` 文件夹复制到你的 Godot 项目的 `addons/mcp_client/` 目录下
2. 在 Godot 编辑器中：
   - 打开 项目 -> 项目设置 -> 插件
   - 启用 "MCP Client" 插件

### 4. 运行游戏

启动你的 Godot 游戏（F5），插件会自动连接到 MCP 服务器。

### 5. 使用 Claude 与游戏交互

现在可以在 Claude 中使用以下命令与运行中的游戏交互：

#### 查看场景树
```
获取当前场景树结构
```

#### 查看节点信息
```
获取节点 /root/Main/Player 的详细信息
```

#### 修改节点属性
```
将节点 /root/Main/Player 的 position 属性设置为 {"x": 100, "y": 200}
```

#### 调用节点方法
```
调用节点 /root/Main/Player 的 queue_free 方法
```

#### 创建新节点
```
在 /root/Main 下创建一个名为 "NewSprite" 的 Sprite2D 节点
```

#### 查看性能统计
```
获取游戏的性能统计信息
```

## MCP 工具完整列表

### 场景管理工具

#### get_scene_tree
获取当前场景树的完整结构。

参数：
- `include_properties` (boolean, 可选): 是否包含节点属性

示例：
```json
{
  "include_properties": true
}
```

#### get_node_info
获取指定节点的详细信息。

参数：
- `node_path` (string, 必需): 节点路径，例如 "/root/Main/Player"

#### get_property
获取节点的属性值。

参数：
- `node_path` (string, 必需): 节点路径
- `property_name` (string, 必需): 属性名称

#### set_property
设置节点的属性值。

参数：
- `node_path` (string, 必需): 节点路径
- `property_name` (string, 必需): 属性名称
- `value` (any, 必需): 新的属性值

示例：
```json
{
  "node_path": "/root/Main/Player",
  "property_name": "position",
  "value": {"x": 100, "y": 200}
}
```

#### create_node
创建新节点。

参数：
- `parent_path` (string, 必需): 父节点路径
- `node_type` (string, 必需): 节点类型，如 "Node2D", "Sprite2D"
- `node_name` (string, 必需): 新节点名称

#### delete_node
删除节点。

参数：
- `node_path` (string, 必需): 要删除的节点路径

#### call_method
调用节点的方法。

参数：
- `node_path` (string, 必需): 节点路径
- `method_name` (string, 必需): 方法名称
- `arguments` (array, 可选): 方法参数

### 运行时工具

#### execute_csharp
执行 C# 代码片段（高级功能）。

参数：
- `code` (string, 必需): C# 代码
- `context_node` (string, 可选): 上下文节点路径

#### get_global_variables
获取全局变量和自动加载单例。

#### get_performance_stats
获取性能统计信息，包括 FPS、内存使用等。

#### get_logs
获取运行时日志。

参数：
- `count` (number, 可选): 获取的日志条数，默认 50
- `level` (string, 可选): 日志级别过滤

### 场景和资源工具

#### load_scene
加载新场景。

参数：
- `scene_path` (string, 必需): 场景文件路径，如 "res://scenes/level2.tscn"

#### get_resource_info
获取资源详细信息。

参数：
- `resource_path` (string, 必需): 资源路径

#### list_resources
列出目录中的资源。

参数：
- `directory` (string, 可选): 目录路径，默认 "res://"
- `filter` (string, 可选): 文件扩展名过滤器

#### take_screenshot
截取游戏画面。

参数：
- `save_path` (string, 可选): 保存路径，默认 "user://screenshot.png"

## 使用场景示例

### 调试玩家位置
```
1. 获取节点 /root/Main/Player 的 position 属性
2. 如果位置不对，设置 position 为正确的值
```

### 动态添加敌人
```
在 /root/Main/Enemies 下创建一个 CharacterBody2D 节点命名为 "Enemy1"
```

### 性能优化
```
1. 获取性能统计信息
2. 查看当前 FPS 和内存使用
3. 如果性能差，查看场景树找出节点过多的地方
```

### 场景切换测试
```
加载场景 res://scenes/menu.tscn
```

## 故障排除

### MCP 服务器无法启动
- 确保已安装 .NET 8.0 SDK
- 检查项目路径是否正确
- 查看 Claude Desktop 的日志

### Godot 插件无法连接
- 确保 MCP 服务器正在运行
- 检查端口 7777 是否被占用
- 查看 Godot 控制台的错误信息

### 找不到节点
- 使用 get_scene_tree 确认节点路径
- 节点路径区分大小写
- 确保节点在当前场景中存在

## 高级配置

### 修改通信端口

在 `McpServer/Services/GodotCommunicationService.cs` 中修改：
```csharp
public GodotCommunicationService(int port = 7777)
```

在 `GodotPlugin/McpClient.cs` 中修改：
```csharp
private int _serverPort = 7777;
```

### 自定义工具

1. 在 `McpServer/Handlers/` 中创建新的处理器类
2. 继承 `ToolHandler` 基类
3. 实现必需的属性和方法
4. 在 `Program.cs` 的 `InitializeHandlers()` 中注册

## 安全注意事项

- 仅在开发环境中使用此工具
- 不要在生产环境中启用 MCP 插件
- `execute_csharp` 功能应谨慎使用
- 考虑添加身份验证机制

## 贡献

欢迎提交 Issue 和 Pull Request！

## 许可证

MIT License
