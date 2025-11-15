# 开发笔记

## 架构说明

### 整体架构

```
┌─────────────┐         JSON-RPC 2.0         ┌──────────────┐
│             │◄─────────(stdio)──────────────┤              │
│   Claude    │                               │  MCP Server  │
│   Desktop   │                               │  (.NET 8)    │
│             │──────────────────────────────►│              │
└─────────────┘                               └──────┬───────┘
                                                     │
                                                     │ TCP Socket
                                                     │ (Port 7777)
                                                     │
                                              ┌──────▼───────┐
                                              │              │
                                              │ Godot Plugin │
                                              │ (MCP Client) │
                                              │              │
                                              └──────┬───────┘
                                                     │
                                                     │ 直接访问
                                                     │
                                              ┌──────▼───────┐
                                              │              │
                                              │ Godot Engine │
                                              │  Runtime     │
                                              │              │
                                              └──────────────┘
```

### 通信流程

1. **Claude -> MCP Server**
   - 协议: JSON-RPC 2.0
   - 传输: stdio (标准输入/输出)
   - 格式: 每行一个 JSON 对象

2. **MCP Server <-> Godot Plugin**
   - 协议: 自定义 JSON 协议
   - 传输: TCP Socket (端口 7777)
   - 格式: 换行符分隔的 JSON 消息

3. **Godot Plugin -> Godot Runtime**
   - 直接方法调用和反射
   - 使用 Godot 的 C# API

## 关键技术决策

### 为什么使用 TCP 而不是 HTTP?

- **保持连接**: TCP 长连接避免每次请求建立连接的开销
- **双向通信**: 支持服务器推送（虽然当前未使用）
- **简单性**: 不需要 HTTP 框架的额外复杂性
- **性能**: 直接的二进制流，延迟更低

### 为什么分离 MCP Server 和 Godot Plugin?

- **独立进程**: Godot 游戏崩溃不影响 MCP 服务器
- **安全性**: 限制 AI 直接访问游戏进程
- **灵活性**: 可以连接多个 Godot 实例
- **调试**: 可以独立调试两个组件

### 异步处理

- MCP Server 使用 async/await 处理并发请求
- Godot Plugin 使用 CallDeferred 确保主线程安全
- 请求-响应使用 TaskCompletionSource 实现

## 主要组件

### MCP Server

#### Program.cs
- 主入口点
- 处理 stdio 通信
- 分发 MCP 请求到相应的处理器

#### GodotCommunicationService.cs
- 管理与 Godot 的 TCP 连接
- 维护待处理请求队列
- 处理超时和重连

#### ToolHandler.cs
- 所有工具处理器的基类
- 提供统一的执行接口

#### SceneHandlers.cs & RuntimeHandlers.cs
- 实现具体的 MCP 工具
- 将 MCP 请求转换为 Godot 操作

### Godot Plugin

#### McpPlugin.cs
- Godot 编辑器插件入口
- 在游戏运行时创建 McpClient

#### McpClient.cs
- 连接到 MCP 服务器
- 处理来自服务器的请求
- 委托给 RuntimeBridge 执行

#### RuntimeBridge.cs
- 核心运行时访问层
- 使用反射和 Godot API 访问游戏状态
- 实现所有运行时操作

## 扩展开发

### 添加新工具

1. **在 MCP Server 端**:

创建新的 Handler:
```csharp
public class MyNewToolHandler : ToolHandler
{
    public override string Name => "my_new_tool";
    public override string Description => "工具描述";
    
    public override InputSchema InputSchema => new()
    {
        // 定义参数
    };
    
    public override async Task<ToolResult> ExecuteAsync(
        Dictionary<string, object?> parameters)
    {
        // 调用 GodotService
        var response = await GodotService.SendRequestAsync(
            "my_action", parameters);
        // 返回结果
    }
}
```

在 Program.cs 中注册:
```csharp
new MyNewToolHandler(_godotService),
```

2. **在 Godot Plugin 端**:

在 RuntimeBridge.cs 中实现:
```csharp
public GodotResponse MyAction(Dictionary<string, JsonElement> parameters)
{
    // 实现逻辑
    return new GodotResponse
    {
        Success = true,
        Data = result
    };
}
```

在 McpClient.cs 中添加路由:
```csharp
"my_action" => _bridge!.MyAction(request.Parameters),
```

### 调试技巧

#### MCP Server 调试

启用详细日志:
```csharp
LogToStderr($"详细信息: {JsonConvert.SerializeObject(data)}");
```

#### Godot Plugin 调试

在 Godot 控制台查看输出:
```csharp
GD.Print($"调试信息: {info}");
GD.PrintErr($"错误: {error}");
```

#### 网络调试

使用 Wireshark 或 tcpdump 监控 TCP 通信:
```bash
tcpdump -i lo0 -A port 7777
```

## 已知限制

### 1. C# 代码执行
- `execute_csharp` 当前是占位实现
- 需要集成 Roslyn 编译器才能真正执行
- 安全性考虑：需要沙箱环境

### 2. 日志拦截
- Godot 的日志系统较难完全拦截
- 当前实现不包含完整的日志捕获

### 3. 属性类型转换
- 复杂类型（如 Color, Transform）的序列化可能不完整
- 需要为每种类型添加专门的转换器

### 4. 并发限制
- 当前只支持一个 Godot 客户端连接
- 多客户端需要额外的会话管理

## 性能考虑

### 瓶颈分析

1. **网络延迟**: 每次操作需要两次网络往返
2. **序列化开销**: JSON 序列化/反序列化
3. **反射开销**: 动态属性访问使用反射

### 优化建议

1. **批量操作**: 支持一次请求执行多个操作
2. **缓存**: 缓存常用的节点信息
3. **增量更新**: 场景树变化时只发送增量
4. **二进制协议**: 使用 MessagePack 替代 JSON

## 安全性

### 当前的安全措施

- 仅监听 localhost，不暴露到网络
- 需要同时运行 MCP 服务器和 Godot 游戏

### 建议的安全增强

1. **身份验证**: 添加 token 认证
2. **权限控制**: 限制某些危险操作
3. **审计日志**: 记录所有操作
4. **沙箱**: 限制代码执行能力

## 未来改进

### 短期目标

- [ ] 完善日志捕获
- [ ] 支持更多属性类型转换
- [ ] 添加单元测试
- [ ] 完善错误处理

### 中期目标

- [ ] 实现真正的 C# 代码执行
- [ ] 支持断点和单步调试
- [ ] 可视化工具（Web UI）
- [ ] 性能分析器集成

### 长期目标

- [ ] 支持多人协作调试
- [ ] AI 驱动的自动化测试
- [ ] 集成机器学习训练
- [ ] 跨引擎支持（Unity, Unreal）

## 版本历史

### v1.0.0 (2025-11-15)
- 初始版本
- 基本的场景和节点操作
- 属性读写
- 性能监控
- 资源管理

## 贡献指南

### 代码风格

- C#: 遵循 Microsoft C# 编码规范
- 使用有意义的变量名
- 添加 XML 文档注释
- 每个公共方法都应有注释

### 提交 PR

1. Fork 项目
2. 创建功能分支
3. 编写测试（如适用）
4. 更新文档
5. 提交 PR 并描述变更

## 许可证

MIT License - 详见 LICENSE 文件
