# Godot 项目集成示例 (v2.0)

## 🎯 新方式: 使用合并后的插件 (推荐)

> v2.0 将 McpServer + GodotPlugin 合并为单个 Godot 插件，无需 .NET SDK。

### 步骤 1: 复制插件文件

将 `addons/godot_mcp/` 文件夹复制到你的 Godot 项目：

```
YourGodotProject/
├── addons/
│   └── godot_mcp/                  ← 整个文件夹
│       ├── plugin.cfg
│       ├── GodotMcpPlugin.cs
│       └── GodotMcpServer.cs       ← 自包含 MCP 服务器
├── scenes/
├── scripts/
└── project.godot
```

### 步骤 2: 启用插件

1. 打开 Godot 编辑器
2. 进入 **项目 → 项目设置 → 插件**
3. 找到 **Godot MCP Server**，点击 **启用**

### 步骤 3: 运行游戏

运行项目后，控制台会显示 MCP 服务器已启动的信息：
```
[GodotMcp] ✓ MCP HTTP+SSE server: http://127.0.0.1:7777/
```

### 步骤 4: 连接 AI Agent

详见 [QUICKSTART.md](QUICKSTART.md) 的配置说明。

---

## 📦 旧方式: 使用独立 GodotPlugin (v1.x, 供参考)

> 以下内容仅供引用。新项目请使用上述 v2.0 方式。

### 步骤 1: 复制插件文件

将整个 `GodotPlugin` 文件夹复制到你的 Godot 项目中：

```
YourGodotProject/
├── addons/
│   └── mcp_client/
│       ├── plugin.cfg
│       ├── McpPlugin.cs
│       ├── McpClient.cs
│       └── RuntimeBridge.cs
├── scenes/
├── scripts/
└── project.godot
```

### 步骤 2: 确保项目使用 C#

在 Godot 编辑器中，确保项目已启用 C# 支持
2. 项目 -> 项目设置 -> 插件
3. 找到 "MCP Client" 并勾选启用

### 步骤 4: 验证安装

1. 启动 MCP 服务器（在终端中）：
```bash
cd /Users/arviter/Projects/Godot-Mcp/McpServer
dotnet run
```

2. 在 Godot 中运行游戏 (F5)

3. 查看 Godot 控制台输出，应该看到：
```
MCP Plugin 已启用
MCP Client 已启动
已连接到 MCP 服务器 127.0.0.1:7777
```

## 创建测试场景

创建一个简单的测试场景来验证 MCP 功能：

### TestScene.tscn

```
Root (Node2D)
├── Player (CharacterBody2D)
│   └── Sprite2D
└── Camera2D
```

### 在 Claude 中测试

启动游戏后，在 Claude 中尝试：

1. **查看场景结构**
```
使用 get_scene_tree 工具获取当前场景树
```

2. **查看玩家信息**
```
使用 get_node_info 工具获取 /root/TestScene/Player 节点的信息
```

3. **修改玩家位置**
```
使用 set_property 工具设置 /root/TestScene/Player 的 position 为 {"x": 500, "y": 300}
```

## 在运行时调试脚本

假设你有一个玩家脚本：

```csharp
public partial class Player : CharacterBody2D
{
    public float Speed { get; set; } = 200.0f;
    public int Health { get; set; } = 100;
    
    public override void _Ready()
    {
        GD.Print("Player ready!");
    }
}
```

你可以通过 Claude：

1. **查看属性**
```
获取玩家的 Speed 和 Health 属性
```

2. **修改属性**
```
将玩家速度设置为 500
```

3. **调用方法**
```
调用玩家节点的 QueueFree 方法（这会删除玩家）
```

## 性能监控

在游戏运行时监控性能：

```
定期获取性能统计信息，帮我分析是否有性能问题
```

Claude 会使用 `get_performance_stats` 工具获取：
- 当前 FPS
- 处理时间
- 物理处理时间
- 内存使用
- 节点数量
- 渲染调用次数

## 动态场景修改

在游戏运行时动态修改场景：

```
1. 在 /root/TestScene 下创建 10 个敌人节点
2. 每个敌人的位置随机分布
```

Claude 会使用 `create_node` 工具动态创建节点。

## 注意事项

### 线程安全

某些操作必须在主线程执行，插件使用 `CallDeferred` 处理这些情况。

### 节点路径

- 使用绝对路径：`/root/Main/Player`
- 确保路径正确，区分大小写

### 性能影响

- MCP 通信有轻微性能开销
- 建议仅在开发/调试时使用
- 发布版本应禁用插件

## 扩展功能

### 添加自定义工具

你可以在 `RuntimeBridge.cs` 中添加自定义功能：

```csharp
public GodotResponse MyCustomAction(Dictionary<string, JsonElement> parameters)
{
    // 你的自定义逻辑
    return new GodotResponse
    {
        Success = true,
        Data = "自定义操作完成"
    };
}
```

然后在 `McpClient.cs` 的请求处理中添加对应的 case。

在 MCP 服务器端创建对应的 Handler：

```csharp
public class MyCustomHandler : ToolHandler
{
    // 实现你的处理器
}
```

## 常见问题

**Q: 游戏启动后 MCP 无法连接？**

A: 检查：
- MCP 服务器是否正在运行
- 端口 7777 是否被占用
- 防火墙设置

**Q: 修改属性后没有效果？**

A: 某些属性可能是只读的，或需要特定的值类型。使用 `get_node_info` 查看可用属性。

**Q: 如何在发布时禁用插件？**

A: 在项目设置中禁用插件，或删除 `addons/mcp_client` 文件夹。
