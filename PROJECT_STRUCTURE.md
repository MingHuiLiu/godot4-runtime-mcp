# Godot MCP 项目文件结构

## 完整项目结构

```
Godot-Mcp/
│
├── README.md                          # 项目介绍和快速开始
├── LICENSE                            # MIT 许可证
├── USAGE.md                          # 详细使用指南
├── INTEGRATION.md                    # Godot 项目集成指南
├── DEVELOPMENT.md                    # 开发者文档
├── .gitignore                        # Git 忽略文件
├── package.json                      # 项目元数据
│
├── start-server.sh                   # Linux/macOS 启动脚本
├── start-server.bat                  # Windows 启动脚本
├── claude_desktop_config.example.json # Claude Desktop 配置示例
│
├── McpServer/                        # MCP 服务器 (.NET 项目)
│   ├── McpServer.csproj              # 项目文件
│   ├── Program.cs                    # 主程序入口
│   │
│   ├── Models/                       # 数据模型
│   │   ├── McpModels.cs              # MCP 协议模型
│   │   └── GodotModels.cs            # Godot 数据模型
│   │
│   ├── Services/                     # 服务层
│   │   └── GodotCommunicationService.cs # Godot 通信服务
│   │
│   └── Handlers/                     # MCP 工具处理器
│       ├── ToolHandler.cs            # 处理器基类
│       ├── SceneHandlers.cs          # 场景管理工具
│       └── RuntimeHandlers.cs        # 运行时工具
│
├── GodotPlugin/                      # Godot 插件
│   ├── plugin.cfg                    # 插件配置
│   ├── McpPlugin.cs                  # 插件入口
│   ├── McpClient.cs                  # MCP 客户端
│   └── RuntimeBridge.cs              # 运行时桥接
│
└── ExampleProject/                   # 示例 Godot 项目
    └── README.md                     # 示例项目说明
```

## 文件说明

### 根目录文件

- **README.md**: 项目主文档，包含功能特性、安装步骤和工具列表
- **USAGE.md**: 详细的使用指南和 MCP 工具参考
- **INTEGRATION.md**: 如何将插件集成到现有 Godot 项目
- **DEVELOPMENT.md**: 架构说明、扩展开发和贡献指南
- **LICENSE**: MIT 开源许可证
- **package.json**: 项目元信息和脚本定义
- **.gitignore**: Git 版本控制忽略规则

### 启动脚本

- **start-server.sh**: Unix 系统（macOS/Linux）启动脚本
- **start-server.bat**: Windows 系统启动脚本
- **claude_desktop_config.example.json**: Claude Desktop 配置模板

### MCP 服务器 (McpServer/)

#### 核心文件
- **Program.cs**: 
  - MCP 协议处理
  - stdio 通信
  - 请求路由
  - 工具注册

#### 模型 (Models/)
- **McpModels.cs**:
  - MCP 协议消息结构
  - 工具定义
  - 请求/响应模型

- **GodotModels.cs**:
  - Godot 特定数据结构
  - 节点信息
  - 性能统计
  - 日志条目

#### 服务 (Services/)
- **GodotCommunicationService.cs**:
  - TCP Socket 服务器
  - 消息队列管理
  - 异步请求处理
  - 连接管理

#### 处理器 (Handlers/)
- **ToolHandler.cs**: 所有工具的抽象基类

- **SceneHandlers.cs**: 场景相关工具
  - GetSceneTreeHandler
  - GetNodeInfoHandler
  - SetPropertyHandler
  - GetPropertyHandler
  - CreateNodeHandler
  - DeleteNodeHandler
  - CallMethodHandler

- **RuntimeHandlers.cs**: 运行时工具
  - ExecuteCSharpHandler
  - GetGlobalVariablesHandler
  - GetPerformanceStatsHandler
  - GetLogsHandler
  - LoadSceneHandler
  - GetResourceInfoHandler
  - ListResourcesHandler
  - TakeScreenshotHandler

### Godot 插件 (GodotPlugin/)

- **plugin.cfg**: Godot 插件元数据配置

- **McpPlugin.cs**: 
  - EditorPlugin 实现
  - 插件生命周期管理
  - 运行时 McpClient 创建

- **McpClient.cs**:
  - TCP 客户端连接
  - 消息接收和处理
  - 请求路由到 RuntimeBridge
  - 响应发送

- **RuntimeBridge.cs**:
  - 核心运行时访问实现
  - 场景树操作
  - 节点属性读写
  - 方法调用
  - 资源管理
  - 性能监控

### 示例项目 (ExampleProject/)

- **README.md**: 示例项目的使用说明和测试用例

## 依赖关系

### MCP 服务器依赖

```xml
<PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
<PackageReference Include="System.Net.WebSockets" Version="4.3.0" />
<PackageReference Include="System.Net.WebSockets.Client" Version="4.3.2" />
```

### Godot 插件依赖

- Godot 4.x with C# (Mono)
- .NET 8.0 或更高版本

## 数据流

### MCP 请求流程

```
1. Claude Desktop
   ↓ (JSON-RPC via stdio)
2. Program.cs
   ↓ (Method routing)
3. ToolHandler
   ↓ (GodotService.SendRequestAsync)
4. GodotCommunicationService
   ↓ (TCP Socket, JSON)
5. McpClient (Godot)
   ↓ (Request routing)
6. RuntimeBridge
   ↓ (Godot API calls)
7. Godot Engine
   ← (Response back through same path)
```

### 消息格式

#### MCP 协议 (stdio)
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "get_scene_tree",
    "arguments": {}
  }
}
```

#### Godot 协议 (TCP)
```json
{
  "type": "request",
  "id": "uuid-here",
  "action": "get_scene_tree",
  "parameters": {}
}
```

## 构建和部署

### 开发环境

1. 安装 .NET 8.0 SDK
2. 安装 Godot 4.x (C# 版本)
3. 克隆仓库
4. 构建 MCP 服务器：
   ```bash
   cd McpServer
   dotnet build
   ```

### 生产部署

1. 发布 MCP 服务器：
   ```bash
   dotnet publish -c Release -o publish
   ```

2. 复制插件到 Godot 项目
3. 配置 Claude Desktop
4. 启动服务器和游戏

## 版本兼容性

| 组件 | 版本要求 |
|------|----------|
| .NET SDK | 8.0+ |
| Godot | 4.0+ (C#) |
| Claude Desktop | 最新版本 |
| macOS | 10.15+ |
| Windows | 10+ |
| Linux | 现代发行版 |

## 网络端口

- **7777**: Godot ↔ MCP Server 通信
  - 协议: TCP
  - 仅 localhost
  - 自定义 JSON 消息格式

## 性能指标

### 典型操作延迟

- 获取节点信息: ~10-50ms
- 设置属性: ~10-50ms
- 获取场景树: ~50-200ms (取决于场景大小)
- 方法调用: ~10-50ms

### 资源使用

- MCP 服务器内存: ~50-100MB
- Godot 插件开销: ~5-10MB
- CPU 使用: 最小 (<1%)

## 支持的 Godot 节点类型

插件支持所有 Godot 内置节点类型，包括：

- Node, Node2D, Node3D
- Control 及其子类
- CanvasItem 及其子类
- CharacterBody2D/3D
- Area2D/3D
- 自定义 C# 节点

## 限制和注意事项

1. **单客户端**: 目前只支持一个 Godot 实例连接
2. **仅运行时**: 无法在编辑器模式下访问
3. **C# 限制**: 仅支持 C# 脚本，不支持 GDScript
4. **性能**: 频繁调用会有网络开销
5. **安全性**: 应仅在开发环境使用

## 扩展点

可以扩展的地方：

1. **新工具**: 添加自定义 MCP 工具
2. **新操作**: 在 RuntimeBridge 中添加新功能
3. **协议增强**: 支持更复杂的数据类型
4. **UI 工具**: 创建 Web 界面或 Godot 编辑器插件
5. **多实例**: 支持连接多个 Godot 游戏

## 相关资源

- [MCP 规范](https://spec.modelcontextprotocol.io/)
- [Godot 文档](https://docs.godotengine.org/)
- [.NET 文档](https://docs.microsoft.com/dotnet/)
- [Claude Desktop](https://claude.ai/desktop)
