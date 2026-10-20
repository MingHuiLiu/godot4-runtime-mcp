# Godot MCP v3.0 - 完全强类型架构

## ✨ 核心改进

### **1. 移除 RuntimeBridge**
- ❌ 不再使用中间层 `RuntimeBridge.cs`
- ✅ `McpClient` 直接实现所有 API 方法
- ✅ 每个方法都有明确的签名和类型定义

### **2. 完全强类型实现**
- ✅ 所有 19 个 API 方法都明确定义
- ✅ 强类型参数解析 (`RequestParams`)
- ✅ 强类型响应模型 (`ApiResponse`, `NodeInfo`)
- ✅ JSON 属性映射 (`[JsonPropertyName]`)

### **3. 简洁高效**
- 📦 单文件实现 (约 400 行)
- 🚀 无反射,无动态调用
- 🎯 直接方法调用,性能最优

## 📋 完整 API 列表

### 场景树操作 (5个)
```csharp
private ApiResponse GetSceneTree(RequestParams p)
private ApiResponse GetNodeInfo(RequestParams p)  
private ApiResponse CreateNode(RequestParams p)
private ApiResponse DeleteNode(RequestParams p)
private ApiResponse LoadScene(RequestParams p)
```

### 属性操作 (3个)
```csharp
private ApiResponse GetProperty(RequestParams p)
private ApiResponse SetProperty(RequestParams p)
private ApiResponse ListProperties(RequestParams p)
```

### 方法调用 (2个)
```csharp
private ApiResponse CallMethod(RequestParams p)
private ApiResponse ListMethods(RequestParams p)
```

### 脚本和资源 (4个)
```csharp
private ApiResponse Exec(RequestParams p)           // C# 执行
private ApiResponse GetGlobals(RequestParams p)     // 全局变量
private ApiResponse GetResourceInfo(RequestParams p)
private ApiResponse ListResources(RequestParams p)
private ApiResponse LoadResource(RequestParams p)
```

### 调试工具 (4个)
```csharp
private ApiResponse GetPerf(RequestParams p)        // 性能统计
private ApiResponse GetLogs(RequestParams p)        // 日志
private ApiResponse Screenshot(RequestParams p)     // 截图
private ApiResponse GetTime(RequestParams p)        // 时间
```

## 🎯 强类型模型

### ApiRequest (请求)
```csharp
public class ApiRequest
{
    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;
    
    [JsonPropertyName("parameters")]
    public Dictionary<string, object> Parameters { get; set; } = new();
}
```

### ApiResponse (响应)
```csharp
public class ApiResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    
    [JsonPropertyName("data")]
    public object? Data { get; set; }
    
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
```

### NodeInfo (节点信息)
```csharp
public class NodeInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; }
    
    [JsonPropertyName("type")]
    public string Type { get; set; }
    
    [JsonPropertyName("path")]
    public string Path { get; set; }
    
    [JsonPropertyName("children")]
    public List<string> Children { get; set; }
    
    [JsonPropertyName("properties")]
    public Dictionary<string, object?> Properties { get; set; }
    
    [JsonPropertyName("methods")]
    public List<string> Methods { get; set; }
    
    [JsonPropertyName("signals")]
    public List<string> Signals { get; set; }
}
```

### RequestParams (参数辅助)
```csharp
public class RequestParams
{
    public string? GetString(string key, string? defaultValue = null)
    public int GetInt(string key, int defaultValue = 0)
    public bool GetBool(string key, bool defaultValue = false)
    public object? Get(string key)
    public List<object>? GetArray(string key)
}
```

## 🔄 请求路由

### 强类型 Switch 路由
```csharp
private ApiResponse HandleRequest(ApiRequest req)
{
    var p = new RequestParams(req.Parameters);
    try {
        return req.Method switch {
            "get_scene_tree" => GetSceneTree(p),
            "get_node_info" => GetNodeInfo(p),
            "create_node" => CreateNode(p),
            "delete_node" => DeleteNode(p),
            "load_scene" => LoadScene(p),
            "get_property" => GetProperty(p),
            "set_property" => SetProperty(p),
            "list_properties" => ListProperties(p),
            "call_method" => CallMethod(p),
            "list_methods" => ListMethods(p),
            "execute_csharp" => Exec(p),
            "get_global_variables" => GetGlobals(p),
            "get_resource_info" => GetResourceInfo(p),
            "list_resources" => ListResources(p),
            "load_resource" => LoadResource(p),
            "get_performance_stats" => GetPerf(p),
            "get_logs" => GetLogs(p),
            "take_screenshot" => Screenshot(p),
            "get_time" => GetTime(p),
            _ => Err($"未知: {req.Method}")
        };
    } catch (Exception ex) { return Err(ex.Message); }
}
```

## 📝 使用示例

### 获取场景树
```json
POST http://127.0.0.1:7777/
{
  "method": "get_scene_tree",
  "parameters": {
    "includeProperties": false
  }
}
```

### 获取节点信息
```json
{
  "method": "get_node_info",
  "parameters": {
    "nodePath": "/root/Main/Player"
  }
}
```

### 设置属性
```json
{
  "method": "set_property",
  "parameters": {
    "nodePath": "/root/Main/Player",
    "propertyName": "position",
    "value": { "x": 100, "y": 200 }
  }
}
```

### 调用方法
```json
{
  "method": "call_method",
  "parameters": {
    "nodePath": "/root/Main/Player",
    "methodName": "TakeDamage",
    "args": [10, "fire"]
  }
}
```

## 🚀 性能优势

| 特性 | v2.0 (RuntimeBridge) | v3.0 (强类型) |
|------|---------------------|--------------|
| 文件数量 | 2个 | 1个 |
| 代码行数 | ~800行 | ~400行 |
| 中间层 | ✗ RuntimeBridge | ✓ 直接调用 |
| 类型安全 | 部分 | 完全 |
| 性能 | 一般 | 最优 |
| 可维护性 | 中等 | 高 |

## 📦 文件结构

```
GodotPlugin/
├── McpClient.cs        ✅ 单文件实现 (所有功能)
├── McpPlugin.cs        (插件元数据)
└── plugin.cfg          (Godot 插件配置)
```

## ✅ 优势总结

1. **简洁明了**: 单文件,所有方法一目了然
2. **强类型**: 编译时检查,运行时无反射
3. **高性能**: 直接方法调用,无中间层开销
4. **易维护**: 明确的方法签名,清晰的参数类型
5. **易调试**: 强类型错误信息,精确的堆栈跟踪

## 🎯 下一步

1. 启动 Godot 游戏
2. 查看控制台输出: `=== Godot MCP v3.0 (强类型) ===`
3. 重启 VSCode (使用新的强类型服务器)
4. 测试 MCP 工具,验证所有 19 个方法正常工作
