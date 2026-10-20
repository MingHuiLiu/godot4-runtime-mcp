# 类型定义速查表

## 快速参考

### 核心类型

```csharp
// 请求
GodotRequest {
    string Method
    Dictionary<string, object> Parameters
}

// 响应
GodotResponse {
    bool Success
    object? Data
    string? Error
}
```

### 场景类型

```csharp
// 场景节点
SceneNode {
    string Name
    string Type
    string Path
    List<SceneNode> Children
}

// 节点信息
NodeInfo {
    string Name
    string Type
    string Path
    Dictionary<string, object> Properties
}

// 属性信息
PropertyInfo {
    string Name
    string Type
    object? Value
}
```

### 方法类型

```csharp
// 方法信息
MethodInfo {
    string Name
    List<ParameterInfo> Parameters
    string ReturnType
}

// 参数信息
ParameterInfo {
    string Name
    string Type
}
```

### 资源类型

```csharp
// 资源信息
ResourceInfo {
    string Path
    string Type
    long Size
    DateTime Modified
}
```

### 调试类型

```csharp
// 日志条目
LogEntry {
    DateTime Timestamp
    string Level
    string Message
}

// 性能统计
PerformanceStats {
    double Fps
    long Memory
    int Objects
    int Resources
}

// 全局变量
GlobalVariables {
    Dictionary<string, object> Variables
}

// 脚本执行结果
ScriptExecutionResult {
    bool Success
    string? Output
    string? Error
}
```

## JSON 映射

所有类型都使用 camelCase JSON 属性名:

```json
{
  "method": "get_scene_tree",
  "parameters": {
    "nodePath": "/root/Main"
  }
}
```

C# 属性使用 PascalCase:

```csharp
request.Method = "get_scene_tree";
request.Parameters["nodePath"] = "/root/Main";
```

## Refit 接口

```csharp
public interface IGodotApi
{
    [Post("/")]
    Task<GodotResponse> CallAsync([Body] GodotRequest request);
    
    [Post("/")]
    Task<GodotResponse> GetSceneTreeAsync([Body] GodotRequest request);
    
    [Post("/")]
    Task<GodotResponse> GetNodeInfoAsync([Body] GodotRequest request);
    
    [Post("/")]
    Task<GodotResponse> SetPropertyAsync([Body] GodotRequest request);
    
    [Post("/")]
    Task<GodotResponse> CallMethodAsync([Body] GodotRequest request);
}
```

## 使用示例

### 创建请求

```csharp
var request = new GodotRequest
{
    Method = "get_node_info",
    Parameters = new Dictionary<string, object>
    {
        ["nodePath"] = "/root/Main/Player"
    }
};
```

### 发送请求

```csharp
var response = await _godotApi.CallAsync(request);
if (response.Success)
{
    // 处理 response.Data
}
else
{
    // 处理 response.Error
}
```

### 解析响应

```csharp
var json = JsonSerializer.Serialize(response);
var result = JsonSerializer.Deserialize<NodeInfo>(json);
```
