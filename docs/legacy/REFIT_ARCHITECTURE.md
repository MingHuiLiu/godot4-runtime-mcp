# Godot MCP - Refit 强类型架构

## ✨ 新架构特点

### **使用 Refit 实现类型安全的 HTTP 调用**

```csharp
// 定义强类型接口
public interface IGodotApi
{
    [Post("/")]
    Task<GodotResponse> CallAsync([Body] GodotRequest request);
}

// 使用 Refit 客户端
var response = await _godotApi.CallAsync(new GodotRequest
{
    Method = "get_scene_tree",
    Parameters = new()
});
```

## 📦 核心类型定义

### GodotRequest (请求)
```csharp
public class GodotRequest
{
    public string Method { get; set; }
    public Dictionary<string, JsonElement> Parameters { get; set; }
}
```

### GodotResponse (响应)
```csharp
public class GodotResponse
{
    public bool Success { get; set; }
    public object? Data { get; set; }
    public string? Error { get; set; }
}
```

## 🔄 依赖注入配置

### Program.cs
```csharp
// 注册 Refit 客户端
builder.Services
    .AddRefitClient<IGodotApi>()
    .ConfigureHttpClient(c => 
    {
        c.BaseAddress = new Uri("http://127.0.0.1:7777/");
        c.Timeout = TimeSpan.FromSeconds(10);
    });

// 注册 Godot 客户端服务
builder.Services.AddSingleton<GodotClient>();
```

## 🛠️ 工具使用示例

### SceneTools.cs
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

    [McpServerTool, Description("获取指定节点的详细信息")]
    public async Task<string> GetNodeInfo(
        [Description("节点路径")] string nodePath)
    {
        return await _godotClient.SendRequestAsync("get_node_info", 
            new { nodePath });
    }
}
```

## 🎯 优势

### 1. **类型安全**
- ✅ 编译时检查接口定义
- ✅ 强类型请求/响应
- ✅ 自动序列化/反序列化

### 2. **易于维护**
- ✅ 接口定义即文档
- ✅ 清晰的错误处理
- ✅ 统一的日志记录

### 3. **更好的错误处理**
```csharp
try
{
    var response = await _godotApi.CallAsync(request);
}
catch (Refit.ApiException ex)
{
    // HTTP 错误 (404, 500, etc.)
    logger.LogError("HTTP {StatusCode}: {Content}", 
        ex.StatusCode, ex.Content);
}
catch (HttpRequestException ex)
{
    // 连接错误
    logger.LogWarning("无法连接: {Message}", ex.Message);
}
```

### 4. **依赖注入集成**
- ✅ 自动管理 HttpClient 生命周期
- ✅ 避免端口耗尽问题
- ✅ 支持 HttpClientFactory 最佳实践

## 📊 完整调用流程

```
VSCode Copilot
    ↓ (stdio)
MCP Server - SceneTools
    ↓
GodotClient.SendRequestAsync("get_scene_tree")
    ↓
IGodotApi.CallAsync(new GodotRequest { Method = "get_scene_tree" })
    ↓ (Refit 自动序列化)
HTTP POST http://127.0.0.1:7777/
    Body: {"Method":"get_scene_tree","Parameters":{}}
    ↓
Godot HTTP Server (McpClient.cs)
    ↓
RuntimeBridge.GetSceneTree()
    ↓
Godot Scene Tree
    ↓
HTTP Response: {"Success":true,"Data":{...}}
    ↓ (Refit 自动反序列化)
GodotResponse object
    ↓
JSON string to MCP Tool
    ↓
VSCode Copilot
```

## 🧪 测试

### 单元测试示例
```csharp
[Fact]
public async Task GetSceneTree_ShouldReturnSuccess()
{
    // Arrange
    var mockApi = new Mock<IGodotApi>();
    mockApi.Setup(x => x.CallAsync(It.IsAny<GodotRequest>()))
        .ReturnsAsync(new GodotResponse 
        { 
            Success = true, 
            Data = new { } 
        });

    var client = new GodotClient(logger, mockApi.Object);

    // Act
    var result = await client.SendRequestAsync("get_scene_tree");

    // Assert
    var response = JsonSerializer.Deserialize<GodotResponse>(result);
    Assert.True(response.Success);
}
```

## 📝 NuGet 包

```xml
<PackageReference Include="Refit" Version="8.0.0" />
<PackageReference Include="Refit.HttpClientFactory" Version="8.0.0" />
<PackageReference Include="ModelContextProtocol" Version="0.4.0-preview.3" />
<PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.0" />
```

## 🚀 下一步

1. **启动 Godot 游戏** - HTTP 服务器会自动启动
2. **重启 VSCode** - 使用新的 Refit 架构
3. **测试工具** - 在 Copilot 中调用 18 个工具
4. **查看日志** - 验证强类型调用正常工作
