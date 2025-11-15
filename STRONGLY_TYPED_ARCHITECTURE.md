# Godot MCP - 完全强类型架构

## 🎯 强类型设计原则

### **所有类型都有明确定义**
- ✅ 所有 DTO 使用 `[JsonPropertyName]` 属性
- ✅ Refit 使用强类型 HTTP 客户端
- ✅ 参数使用 `Dictionary<string, object>` 而非 `JsonElement`
- ✅ 完整的编译时类型检查

## 📦 完整类型定义

### 1. **核心请求/响应**

#### GodotRequest
```csharp
public class GodotRequest
{
    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;
    
    [JsonPropertyName("parameters")]
    public Dictionary<string, object> Parameters { get; set; } = new();
}
```

#### GodotResponse
```csharp
public class GodotResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    
    [JsonPropertyName("data")]
    public object? Data { get; set; }
    
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
```

### 2. **场景树类型**

#### SceneNode
```csharp
public class SceneNode
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
    
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;
    
    [JsonPropertyName("children")]
    public List<SceneNode> Children { get; set; } = new();
}
```

#### NodeInfo
```csharp
public class NodeInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
    
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;
    
    [JsonPropertyName("properties")]
    public Dictionary<string, object> Properties { get; set; } = new();
}
```

### 3. **属性和方法类型**

#### PropertyInfo
```csharp
public class PropertyInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
    
    [JsonPropertyName("value")]
    public object? Value { get; set; }
}
```

#### MethodInfo
```csharp
public class MethodInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("parameters")]
    public List<ParameterInfo> Parameters { get; set; } = new();
    
    [JsonPropertyName("returnType")]
    public string ReturnType { get; set; } = string.Empty;
}
```

#### ParameterInfo
```csharp
public class ParameterInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
}
```

### 4. **资源和调试类型**

#### ResourceInfo
```csharp
public class ResourceInfo
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;
    
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
    
    [JsonPropertyName("size")]
    public long Size { get; set; }
    
    [JsonPropertyName("modified")]
    public DateTime Modified { get; set; }
}
```

#### LogEntry
```csharp
public class LogEntry
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }
    
    [JsonPropertyName("level")]
    public string Level { get; set; } = string.Empty;
    
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}
```

#### PerformanceStats
```csharp
public class PerformanceStats
{
    [JsonPropertyName("fps")]
    public double Fps { get; set; }
    
    [JsonPropertyName("memory")]
    public long Memory { get; set; }
    
    [JsonPropertyName("objects")]
    public int Objects { get; set; }
    
    [JsonPropertyName("resources")]
    public int Resources { get; set; }
}
```

#### ScriptExecutionResult
```csharp
public class ScriptExecutionResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    
    [JsonPropertyName("output")]
    public string? Output { get; set; }
    
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
```

#### GlobalVariables
```csharp
public class GlobalVariables
{
    [JsonPropertyName("variables")]
    public Dictionary<string, object> Variables { get; set; } = new();
}
```

## 🔧 强类型 Refit 接口

```csharp
public interface IGodotApi
{
    /// <summary>
    /// 调用 Godot 方法 (通用)
    /// </summary>
    [Post("/")]
    Task<GodotResponse> CallAsync([Body] GodotRequest request);
    
    /// <summary>
    /// 获取场景树 (专用)
    /// </summary>
    [Post("/")]
    Task<GodotResponse> GetSceneTreeAsync([Body] GodotRequest request);
    
    /// <summary>
    /// 获取节点信息 (专用)
    /// </summary>
    [Post("/")]
    Task<GodotResponse> GetNodeInfoAsync([Body] GodotRequest request);
    
    /// <summary>
    /// 设置节点属性 (专用)
    /// </summary>
    [Post("/")]
    Task<GodotResponse> SetPropertyAsync([Body] GodotRequest request);
    
    /// <summary>
    /// 调用节点方法 (专用)
    /// </summary>
    [Post("/")]
    Task<GodotResponse> CallMethodAsync([Body] GodotRequest request);
}
```

## ⚙️ JSON 序列化配置

### Refit 配置
```csharp
var refitSettings = new RefitSettings
{
    ContentSerializer = new SystemTextJsonContentSerializer(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,  // 自动转 camelCase
        WriteIndented = false,                               // 压缩 JSON
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,  // 忽略 null
        PropertyNameCaseInsensitive = true                   // 忽略大小写
    })
};
```

### DI 注册
```csharp
builder.Services
    .AddRefitClient<IGodotApi>(refitSettings)  // 使用自定义设置
    .ConfigureHttpClient(c => 
    {
        c.BaseAddress = new Uri("http://127.0.0.1:7777/");
        c.Timeout = TimeSpan.FromSeconds(10);
    });
```

## 🛠️ GodotClient 强类型实现

```csharp
public class GodotClient : IDisposable
{
    private readonly ILogger<GodotClient> _logger;
    private readonly IGodotApi _godotApi;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// 发送强类型请求到 Godot
    /// </summary>
    public async Task<string> SendRequestAsync(string method, object? parameters = null)
    {
        var request = CreateRequest(method, parameters);      // ✅ 强类型
        var response = await _godotApi.CallAsync(request);    // ✅ 强类型
        return SerializeResponse(response);                   // ✅ 强类型
    }

    /// <summary>
    /// 创建强类型请求对象
    /// </summary>
    private GodotRequest CreateRequest(string method, object? parameters)
    {
        return new GodotRequest
        {
            Method = method,
            Parameters = ConvertToParameterDictionary(parameters)
        };
    }

    /// <summary>
    /// 转换参数为强类型字典
    /// </summary>
    private Dictionary<string, object> ConvertToParameterDictionary(object? parameters)
    {
        if (parameters == null)
            return new Dictionary<string, object>();

        if (parameters is Dictionary<string, object> dict)
            return dict;

        var json = JsonSerializer.Serialize(parameters, JsonOptions);
        var result = JsonSerializer.Deserialize<Dictionary<string, object>>(json, JsonOptions);
        return result ?? new Dictionary<string, object>();
    }
}
```

## 📊 完整调用流程 (强类型)

```
VSCode Copilot
    ↓
[McpServerTool] GetSceneTree()  // ✅ 强类型方法
    ↓
GodotClient.SendRequestAsync("get_scene_tree")
    ↓
CreateRequest() → GodotRequest { Method, Parameters }  // ✅ 强类型对象
    ↓
IGodotApi.CallAsync(request)  // ✅ Refit 强类型接口
    ↓ (Refit 自动序列化)
HTTP POST http://127.0.0.1:7777/
    Content-Type: application/json
    Body: {
        "method": "get_scene_tree",      // ✅ camelCase
        "parameters": {}
    }
    ↓
Godot HTTP Server (McpClient.cs)
    ↓
RuntimeBridge.GetSceneTree()
    ↓
HTTP Response:
    {
        "success": true,                  // ✅ camelCase
        "data": { ... }
    }
    ↓ (Refit 自动反序列化)
GodotResponse object  // ✅ 强类型对象
    ↓
SerializeResponse() → JSON string
    ↓
VSCode Copilot
```

## 🎯 强类型优势

### 1. **编译时类型检查**
```csharp
// ❌ 错误会在编译时发现
var request = new GodotRequest
{
    Method = 123,  // ❌ 编译错误: 不能将 int 转为 string
    Parameters = "invalid"  // ❌ 编译错误: 类型不匹配
};

// ✅ 正确的强类型使用
var request = new GodotRequest
{
    Method = "get_scene_tree",
    Parameters = new Dictionary<string, object>
    {
        ["nodePath"] = "/root/Main"
    }
};
```

### 2. **IntelliSense 支持**
- ✅ 自动完成属性名
- ✅ 显示类型信息
- ✅ 参数提示

### 3. **重构安全**
- ✅ 重命名属性会更新所有引用
- ✅ 删除属性会显示编译错误
- ✅ 修改类型会触发类型检查

### 4. **JSON 序列化保证**
- ✅ `[JsonPropertyName]` 明确定义 JSON 键名
- ✅ camelCase 自动转换
- ✅ null 值自动忽略

## 🧪 测试示例

### 单元测试 (强类型)
```csharp
[Fact]
public async Task GetSceneTree_Returns_StrongTypedResponse()
{
    // Arrange
    var expectedResponse = new GodotResponse
    {
        Success = true,
        Data = new SceneNode
        {
            Name = "Root",
            Type = "Node",
            Path = "/root",
            Children = new()
        }
    };

    var mockApi = new Mock<IGodotApi>();
    mockApi
        .Setup(x => x.CallAsync(It.Is<GodotRequest>(r => r.Method == "get_scene_tree")))
        .ReturnsAsync(expectedResponse);

    var client = new GodotClient(logger, mockApi.Object);

    // Act
    var result = await client.SendRequestAsync("get_scene_tree");

    // Assert
    var response = JsonSerializer.Deserialize<GodotResponse>(result);
    Assert.NotNull(response);
    Assert.True(response.Success);
    Assert.IsType<SceneNode>(response.Data);
}
```

### 集成测试
```bash
# 使用 curl 测试 (JSON 必须是 camelCase)
curl -X POST http://127.0.0.1:7777/ \
  -H "Content-Type: application/json" \
  -d '{
    "method": "get_scene_tree",
    "parameters": {}
  }'

# 预期响应 (camelCase)
{
  "success": true,
  "data": {
    "name": "Root",
    "type": "Node",
    "path": "/root",
    "children": []
  }
}
```

## 📝 类型定义总结

| 类型 | 用途 | 关键属性 |
|------|------|---------|
| `GodotRequest` | HTTP 请求 | method, parameters |
| `GodotResponse` | HTTP 响应 | success, data, error |
| `SceneNode` | 场景树节点 | name, type, path, children |
| `NodeInfo` | 节点详情 | name, type, path, properties |
| `PropertyInfo` | 属性信息 | name, type, value |
| `MethodInfo` | 方法信息 | name, parameters, returnType |
| `ParameterInfo` | 参数信息 | name, type |
| `ResourceInfo` | 资源信息 | path, type, size, modified |
| `LogEntry` | 日志条目 | timestamp, level, message |
| `PerformanceStats` | 性能统计 | fps, memory, objects, resources |
| `GlobalVariables` | 全局变量 | variables |
| `ScriptExecutionResult` | 脚本执行 | success, output, error |

## 🚀 下一步

1. **启动 Godot** - 确保 HTTP 服务器正在运行
2. **重启 VSCode** - 使用新的强类型架构
3. **测试工具** - 验证所有 18 个工具正常工作
4. **检查日志** - 确认 JSON 序列化正确 (camelCase)
