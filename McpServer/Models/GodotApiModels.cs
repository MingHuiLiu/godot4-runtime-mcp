using System.Text.Json;
using System.Text.Json.Serialization;

namespace McpServer.Models;

// ========== 基础请求类型 ==========

/// <summary>
/// 场景树请求
/// </summary>
public class SceneTreeRequest
{
    [JsonPropertyName("includeProperties")]
    public bool IncludeProperties { get; set; } = false;
}

/// <summary>
/// 节点路径请求
/// </summary>
public class NodePathRequest
{
    [JsonPropertyName("nodePath")]
    public string NodePath { get; set; } = string.Empty;
}

/// <summary>
/// 创建节点请求
/// </summary>
public class CreateNodeRequest
{
    [JsonPropertyName("parentPath")]
    public string ParentPath { get; set; } = string.Empty;
    
    [JsonPropertyName("nodeType")]
    public string NodeType { get; set; } = string.Empty;
    
    [JsonPropertyName("nodeName")]
    public string NodeName { get; set; } = string.Empty;
}

/// <summary>
/// 场景路径请求
/// </summary>
public class ScenePathRequest
{
    [JsonPropertyName("scenePath")]
    public string ScenePath { get; set; } = string.Empty;
}

/// <summary>
/// 属性请求
/// </summary>
public class PropertyRequest
{
    [JsonPropertyName("nodePath")]
    public string NodePath { get; set; } = string.Empty;
    
    [JsonPropertyName("propertyName")]
    public string PropertyName { get; set; } = string.Empty;
}

/// <summary>
/// 设置属性请求
/// </summary>
public class SetPropertyRequest
{
    [JsonPropertyName("nodePath")]
    public string NodePath { get; set; } = string.Empty;
    
    [JsonPropertyName("propertyName")]
    public string PropertyName { get; set; } = string.Empty;
    
    [JsonPropertyName("value")]
    public object? Value { get; set; }
}

/// <summary>
/// 调用方法请求
/// </summary>
public class CallMethodRequest
{
    [JsonPropertyName("nodePath")]
    public string NodePath { get; set; } = string.Empty;
    
    [JsonPropertyName("methodName")]
    public string MethodName { get; set; } = string.Empty;
    
    [JsonPropertyName("args")]
    public List<object>? Args { get; set; }
}

/// <summary>
/// 代码执行请求
/// </summary>
public class CodeRequest
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;
}

/// <summary>
/// 资源路径请求
/// </summary>
public class ResourcePathRequest
{
    [JsonPropertyName("resourcePath")]
    public string ResourcePath { get; set; } = string.Empty;
}

/// <summary>
/// 列出资源请求
/// </summary>
public class ListResourcesRequest
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "res://";
    
    [JsonPropertyName("filter")]
    public string? Filter { get; set; }
}

/// <summary>
/// 日志请求
/// </summary>
public class LogsRequest
{
    [JsonPropertyName("count")]
    public int Count { get; set; } = 50;
}

/// <summary>
/// 截图请求
/// </summary>
public class ScreenshotRequest
{
    [JsonPropertyName("savePath")]
    public string? SavePath { get; set; }
}

/// <summary>
/// 查找节点请求
/// </summary>
public class FindNodesRequest
{
    [JsonPropertyName("nodeType")]
    public string? NodeType { get; set; }
    
    [JsonPropertyName("namePattern")]
    public string? NamePattern { get; set; }
    
    [JsonPropertyName("rootPath")]
    public string RootPath { get; set; } = "/root";
    
    [JsonPropertyName("caseSensitive")]
    public bool CaseSensitive { get; set; } = false;
    
    [JsonPropertyName("exactMatch")]
    public bool ExactMatch { get; set; } = false;
    
    [JsonPropertyName("groupName")]
    public string? GroupName { get; set; }
    
    [JsonPropertyName("maxResults")]
    public int MaxResults { get; set; } = 50;
}

/// <summary>
/// 子树请求
/// </summary>
public class SubtreeRequest
{
    [JsonPropertyName("nodePath")]
    public string NodePath { get; set; } = string.Empty;
    
    [JsonPropertyName("maxDepth")]
    public int MaxDepth { get; set; } = 2;
    
    [JsonPropertyName("includeProperties")]
    public bool IncludeProperties { get; set; } = false;
}

/// <summary>
/// 祖先节点请求
/// </summary>
public class AncestorsRequest
{
    [JsonPropertyName("nodePath")]
    public string NodePath { get; set; } = string.Empty;
    
    [JsonPropertyName("levels")]
    public int Levels { get; set; } = -1;
    
    [JsonPropertyName("includeSiblings")]
    public bool IncludeSiblings { get; set; } = false;
}

/// <summary>
/// 节点上下文请求
/// </summary>
public class NodeContextRequest
{
    [JsonPropertyName("nodePath")]
    public string NodePath { get; set; } = string.Empty;
    
    [JsonPropertyName("includeParent")]
    public bool IncludeParent { get; set; } = true;
    
    [JsonPropertyName("includeSiblings")]
    public bool IncludeSiblings { get; set; } = true;
    
    [JsonPropertyName("includeChildren")]
    public bool IncludeChildren { get; set; } = true;
}

/// <summary>
/// Godot API 响应 (强类型)
/// </summary>
public class GodotResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    
    [JsonPropertyName("data")]
    public object? Data { get; set; }
    
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>
/// 场景树节点 (强类型)
/// </summary>
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

/// <summary>
/// 节点详细信息 (强类型)
/// </summary>
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

/// <summary>
/// 节点属性信息 (强类型)
/// </summary>
public class PropertyInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
    
    [JsonPropertyName("value")]
    public object? Value { get; set; }
}

/// <summary>
/// 方法信息 (强类型)
/// </summary>
public class MethodInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("parameters")]
    public List<ParameterInfo> Parameters { get; set; } = new();
    
    [JsonPropertyName("returnType")]
    public string ReturnType { get; set; } = string.Empty;
}

/// <summary>
/// 参数信息 (强类型)
/// </summary>
public class ParameterInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
}

/// <summary>
/// 资源信息 (强类型)
/// </summary>
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

/// <summary>
/// 日志条目 (强类型)
/// </summary>
public class LogEntry
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }
    
    [JsonPropertyName("level")]
    public string Level { get; set; } = string.Empty;
    
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// 性能统计 (强类型)
/// </summary>
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

/// <summary>
/// 全局变量 (强类型)
/// </summary>
public class GlobalVariables
{
    [JsonPropertyName("variables")]
    public Dictionary<string, object> Variables { get; set; } = new();
}

/// <summary>
/// 脚本执行结果 (强类型)
/// </summary>
public class ScriptExecutionResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    
    [JsonPropertyName("output")]
    public string? Output { get; set; }
    
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
