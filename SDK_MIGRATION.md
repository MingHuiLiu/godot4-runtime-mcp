# Godot MCP 服务器 - 使用官方 SDK

## ✅ 重构完成

本项目已成功从自定义 JSON-RPC 实现迁移到 **Microsoft 官方 ModelContextProtocol SDK**。

### 主要改进

1. **使用官方 SDK** - 不再手动处理协议细节
2. **自动工具注册** - 通过特性自动扫描和注册工具
3. **依赖注入** - 标准的 .NET Hosting 模式
4. **更好的兼容性** - 完全符合 MCP 规范

## 🛠️ 已实现的 18 个工具

### 场景管理 (SceneTools)
- `GetSceneTree` - 获取当前场景树结构
- `GetNodeInfo` - 获取指定节点的详细信息
- `CreateNode` - 创建新节点
- `DeleteNode` - 删除节点
- `LoadScene` - 加载场景

### 属性操作 (PropertyTools)
- `GetProperty` - 获取节点属性值
- `SetProperty` - 设置节点属性值
- `ListProperties` - 列出节点所有属性

### 方法调用 (MethodTools)
- `CallMethod` - 调用节点方法
- `ListMethods` - 列出节点所有方法

### 脚本执行 (ScriptTools)
- `ExecuteCSharp` - 执行 C# 代码片段
- `GetGlobalVariables` - 获取全局变量

### 资源管理 (ResourceTools)
- `ListResources` - 列出资源
- `LoadResource` - 加载资源
- `GetResourceInfo` - 获取资源信息

### 调试工具 (DebugTools)
- `GetLogs` - 获取日志输出
- `GetPerformanceStats` - 获取性能统计
- `TakeScreenshot` - 截图

## 📁 项目结构

```
McpServer/
├── Program.cs                   # 主程序 - 使用官方 SDK
├── Services/
│   └── GodotClient.cs          # Godot TCP 通信客户端
└── Tools/
    ├── SceneTools.cs           # 5个场景管理工具
    ├── PropertyTools.cs        # 3个属性操作工具
    ├── MethodTools.cs          # 2个方法调用工具
    ├── ScriptTools.cs          # 2个脚本执行工具
    ├── ResourceTools.cs        # 3个资源管理工具
    └── DebugTools.cs           # 3个调试工具
```

## 🚀 使用方法

### 1. 构建服务器
```bash
cd McpServer
dotnet build
```

### 2. 配置 VSCode

在 `.vscode/mcp.json` 中已配置:
```json
{
  "servers": {
    "godot-mcp": {
      "type": "stdio",
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

### 3. 在 Godot 中启动游戏

确保 Godot 插件已启用并运行游戏。插件会自动连接到 MCP 服务器的 TCP 端口 7777。

### 4. 在 VSCode Copilot 中使用

1. 打开 GitHub Copilot
2. 切换到 Agent 模式
3. 点击"选择工具"查看所有 18 个工具
4. 开始与 Godot 游戏交互!

## 📝 示例用法

```
你: "获取当前场景树结构"
→ 调用 GetSceneTree

你: "获取 /root/Main/Player 节点的位置属性"
→ 调用 GetProperty(nodePath="/root/Main/Player", propertyName="position")

你: "创建一个新的 Sprite2D 节点"
→ 调用 CreateNode(parentPath="/root/Main", nodeType="Sprite2D", nodeName="NewSprite")

你: "执行代码: GD.Print('Hello from MCP!')"
→ 调用 ExecuteCSharp(code="GD.Print('Hello from MCP!')")
```

## 🔧 技术栈

- **.NET 9.0** - 最新 .NET 运行时
- **ModelContextProtocol SDK 0.4.0-preview.3** - 官方 MCP SDK
- **Microsoft.Extensions.Hosting 10.0.0** - 依赖注入和托管
- **Godot 4.x + C#** - 游戏引擎

## 📦 依赖包

```xml
<PackageReference Include="ModelContextProtocol" Version="0.4.0-preview.3" />
<PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.0" />
```

## 🎯 核心代码

### Program.cs (仅 23 行!)
```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using McpServer.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(consoleLogOptions =>
{
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.AddSingleton<GodotClient>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
```

### 工具定义示例
```csharp
[McpServerToolType]
public class SceneTools
{
    private readonly GodotClient _godotClient;

    public SceneTools(GodotClient godotClient)
    {
        _godotClient = godotClient;
    }

    [McpServerTool, Description("获取当前场景树结构")]
    public async Task<string> GetSceneTree()
    {
        return await _godotClient.SendRequestAsync("get_scene_tree");
    }
}
```

## ✨ 优势

1. **极简代码** - 主程序仅 23 行
2. **自动发现** - 工具通过特性自动注册
3. **类型安全** - 强类型参数和返回值
4. **易于扩展** - 添加新工具只需添加新方法
5. **官方支持** - 使用 Microsoft 官方 SDK

## 📄 许可证

MIT License
