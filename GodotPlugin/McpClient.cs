using Godot;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Linq;

/// <summary>
/// MCP HTTP API 服务器 v4.0 - 每个方法独立的 HTTP 端点
/// </summary>
public partial class McpClient : Node
{
    private HttpListener? _httpListener;
    private bool _isRunning = false;
    private const string ApiUrl = "http://127.0.0.1:7777/";
    private readonly List<LogEntry> _logs = new();
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public override void _Ready()
    {
        GD.Print("=".PadRight(60, '='));
        GD.Print("[MCP] Godot MCP v4.0 - 独立HTTP端点");
        GD.Print("=".PadRight(60, '='));
        StartServer();
    }

    private async void StartServer()
    {
        try
        {
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add(ApiUrl);
            _httpListener.Start();
            _isRunning = true;
            GD.Print($"[MCP] ✓ 监听: {ApiUrl}");
            GD.Print("[MCP] ✓ 19个独立HTTP端点已就绪");
            _ = Task.Run(HandleRequestsAsync);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MCP] ✗ 启动失败: {ex.Message}");
        }
    }

    private async Task HandleRequestsAsync()
    {
        while (_isRunning && _httpListener != null)
        {
            try
            {
                var context = await _httpListener.GetContextAsync();
                _ = Task.Run(() => ProcessRequestAsync(context));
            }
            catch { }
        }
    }

    private async Task ProcessRequestAsync(HttpListenerContext context)
    {
        try
        {
            if (context.Request.HttpMethod != "POST")
            {
                context.Response.StatusCode = 405;
                context.Response.Close();
                return;
            }

            var path = context.Request.Url?.AbsolutePath ?? "/";
            GD.Print($"[MCP] {path}");

            using var reader = new System.IO.StreamReader(context.Request.InputStream);
            var body = await reader.ReadToEndAsync();

            var response = RouteRequest(path, body);
            
            var json = JsonSerializer.Serialize(response, JsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);

            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            context.Response.Close();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MCP] Error: {ex.Message}");
            context.Response.StatusCode = 500;
            context.Response.Close();
        }
    }

    /// <summary>
    /// 根据 HTTP 路径路由到对应方法
    /// </summary>
    private ApiResponse RouteRequest(string path, string body)
    {
        try
        {
            return path switch
            {
                "/get_scene_tree" => GetSceneTree(Deserialize<SceneTreeRequest>(body)),
                "/get_node_info" => GetNodeInfo(Deserialize<NodePathRequest>(body)),
                "/create_node" => CreateNode(Deserialize<CreateNodeRequest>(body)),
                "/delete_node" => DeleteNode(Deserialize<NodePathRequest>(body)),
                "/load_scene" => LoadScene(Deserialize<ScenePathRequest>(body)),
                "/get_property" => GetProperty(Deserialize<PropertyRequest>(body)),
                "/set_property" => SetProperty(Deserialize<SetPropertyRequest>(body)),
                "/list_properties" => ListProperties(Deserialize<NodePathRequest>(body)),
                "/call_method" => CallMethod(Deserialize<CallMethodRequest>(body)),
                "/list_methods" => ListMethods(Deserialize<NodePathRequest>(body)),
                "/execute_csharp" => ExecuteCSharp(Deserialize<CodeRequest>(body)),
                "/get_global_variables" => GetGlobalVariables(),
                "/get_resource_info" => GetResourceInfo(Deserialize<ResourcePathRequest>(body)),
                "/list_resources" => ListResources(Deserialize<ListResourcesRequest>(body)),
                "/load_resource" => LoadResource(Deserialize<ResourcePathRequest>(body)),
                "/get_performance_stats" => GetPerformanceStats(),
                "/get_logs" => GetLogs(Deserialize<LogsRequest>(body)),
                "/take_screenshot" => TakeScreenshot(Deserialize<ScreenshotRequest>(body)),
                "/get_time" => GetTime(),
                _ => ErrorResponse($"未知端点: {path}")
            };
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MCP] 路由错误: {ex.Message}");
            return ErrorResponse(ex.Message);
        }
    }

    private T Deserialize<T>(string json) where T : new()
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? new T();
        }
        catch
        {
            return new T();
        }
    }

    // ========== 场景树方法 (获取游戏运行时信息) ==========

    private ApiResponse GetSceneTree(SceneTreeRequest req)
    {
        var root = GetTree().Root;  // 获取当前运行中游戏的场景树根节点
        var tree = BuildTree(root, req.IncludeProperties);
        return Ok(tree);
    }

    private ApiResponse GetNodeInfo(NodePathRequest req)
    {
        var node = GetNodeOrNull(req.NodePath);
        if (node == null) return Err($"找不到节点: {req.NodePath}");

        return Ok(new NodeInfo
        {
            Name = node.Name,
            Type = node.GetType().Name,
            Path = node.GetPath().ToString(),
            Children = node.GetChildren().Select(c => c.Name.ToString()).ToList(),
            Properties = GetProps(node),
            Methods = GetMethods(node),
            Signals = GetSignals(node)
        });
    }

    private ApiResponse CreateNode(CreateNodeRequest req)
    {
        var parent = GetNodeOrNull(req.ParentPath);
        if (parent == null) return Err($"父节点不存在: {req.ParentPath}");

        var node = CreateByType(req.NodeType);
        if (node == null) return Err($"无法创建类型: {req.NodeType}");

        node.Name = req.NodeName;
        parent.AddChild(node);
        return Ok(new { path = node.GetPath().ToString() });
    }

    private ApiResponse DeleteNode(NodePathRequest req)
    {
        var node = GetNodeOrNull(req.NodePath);
        if (node == null) return Err($"节点不存在: {req.NodePath}");

        node.QueueFree();
        return Ok("已删除");
    }

    private ApiResponse LoadScene(ScenePathRequest req)
    {
        GetTree().CallDeferred("change_scene_to_file", req.ScenePath);
        return Ok($"加载中: {req.ScenePath}");
    }

    // ========== 属性方法 (游戏运行时属性) ==========

    private ApiResponse GetProperty(PropertyRequest req)
    {
        var node = GetNodeOrNull(req.NodePath);
        if (node == null) return Err($"节点不存在: {req.NodePath}");

        var value = node.Get(req.PropertyName);  // 获取运行时属性值
        return Ok(new { value = Conv(value) });
    }

    private ApiResponse SetProperty(SetPropertyRequest req)
    {
        var node = GetNodeOrNull(req.NodePath);
        if (node == null) return Err($"节点不存在: {req.NodePath}");

        node.Set(req.PropertyName, ToVar(req.Value));  // 设置运行时属性
        return Ok($"已设置: {req.PropertyName}");
    }

    private ApiResponse ListProperties(NodePathRequest req)
    {
        var node = GetNodeOrNull(req.NodePath);
        if (node == null) return Err($"节点不存在: {req.NodePath}");

        return Ok(GetProps(node));
    }

    // ========== 方法调用 (运行时方法) ==========

    private ApiResponse CallMethod(CallMethodRequest req)
    {
        var node = GetNodeOrNull(req.NodePath);
        if (node == null) return Err($"节点不存在: {req.NodePath}");

        var args = new Godot.Collections.Array();
        req.Args?.ForEach(a => args.Add(ToVar(a)));
        
        var result = node.Call(req.MethodName, args);  // 运行时调用
        return Ok(Conv(result));
    }

    private ApiResponse ListMethods(NodePathRequest req)
    {
        var node = GetNodeOrNull(req.NodePath);
        if (node == null) return Err($"节点不存在: {req.NodePath}");

        return Ok(GetMethods(node));
    }

    // ========== 脚本和资源 ==========

    private ApiResponse ExecuteCSharp(CodeRequest req) => Ok("需要 Roslyn");

    private ApiResponse GetGlobalVariables()
    {
        return Ok(GetTree().Root.GetChildren().ToDictionary(
            c => c.Name.ToString(),
            c => (object)new { type = c.GetType().Name, path = c.GetPath().ToString() }
        ));
    }

    private ApiResponse GetResourceInfo(ResourcePathRequest req)
    {
        var r = GD.Load(req.ResourcePath);
        return r == null ? Err("资源不存在") : Ok(new { type = r.GetType().Name, path = r.ResourcePath });
    }

    private ApiResponse ListResources(ListResourcesRequest req)
    {
        var list = new List<string>();
        var dir = DirAccess.Open(req.Path);
        if (dir != null)
        {
            dir.ListDirBegin();
            for (var f = dir.GetNext(); f != ""; f = dir.GetNext())
                if (!dir.CurrentIsDir()) list.Add(f);
            dir.ListDirEnd();
        }
        return Ok(list);
    }

    private ApiResponse LoadResource(ResourcePathRequest req)
    {
        var r = GD.Load(req.ResourcePath);
        return r == null ? Err("加载失败") : Ok(new { type = r.GetType().Name });
    }

    // ========== 调试方法 (运行时统计) ==========

    private ApiResponse GetPerformanceStats()
    {
        return Ok(new
        {
            fps = Engine.GetFramesPerSecond(),  // 实时 FPS
            processTime = Performance.GetMonitor(Performance.Monitor.TimeProcess),
            physicsTime = Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess),
            memoryUsage = Performance.GetMonitor(Performance.Monitor.MemoryStatic),  // 实时内存
            nodeCount = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount),  // 实时节点数
            drawCalls = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)
        });
    }

    private ApiResponse GetLogs(LogsRequest req) => Ok(_logs.TakeLast(req.Count));

    private ApiResponse TakeScreenshot(ScreenshotRequest req)
    {
        var path = req.SavePath ?? "user://screenshot.png";
        var img = GetViewport().GetTexture().GetImage();  // 当前画面截图
        img.SavePng(path);
        return Ok(new { path });
    }

    private ApiResponse GetTime() => Ok(new
    {
        unix = Time.GetUnixTimeFromSystem(),
        datetime = DateTime.Now.ToString("s"),
        ticks = Time.GetTicksMsec()
    });

    // ========== 辅助方法 ==========

    private Dictionary<string, object> BuildTree(Node n, bool props)
    {
        var d = new Dictionary<string, object>
        {
            ["name"] = n.Name,
            ["type"] = n.GetType().Name,
            ["path"] = n.GetPath().ToString(),
            ["children"] = n.GetChildren().Select(c => BuildTree(c, props)).ToList()
        };
        if (props) d["properties"] = GetProps(n);
        return d;
    }

    private Dictionary<string, object?> GetProps(Node n) =>
        n.GetPropertyList()
            .Select(p => (Godot.Collections.Dictionary)p)
            .Where(p => !p["name"].AsString().StartsWith("_"))
            .ToDictionary(p => p["name"].AsString(), p =>
            {
                try { return Conv(n.Get(p["name"].AsString())); }
                catch { return null; }
            });

    private List<string> GetMethods(Node n) =>
        n.GetMethodList()
            .Select(m => ((Godot.Collections.Dictionary)m)["name"].AsString())
            .Where(m => !m.StartsWith("_"))
            .ToList();

    private List<string> GetSignals(Node n) =>
        n.GetSignalList()
            .Select(s => ((Godot.Collections.Dictionary)s)["name"].AsString())
            .ToList();

    private Node? CreateByType(string t)
    {
        var type = Type.GetType($"Godot.{t}") ?? Type.GetType(t);
        return type != null && typeof(Node).IsAssignableFrom(type)
            ? (Node?)Activator.CreateInstance(type)
            : null;
    }

    private object? Conv(Variant v) => v.VariantType switch
    {
        Variant.Type.Nil => null,
        Variant.Type.Bool => v.AsBool(),
        Variant.Type.Int => v.AsInt64(),
        Variant.Type.Float => v.AsDouble(),
        Variant.Type.String => v.AsString(),
        Variant.Type.Vector2 => new { x = v.AsVector2().X, y = v.AsVector2().Y },
        Variant.Type.Vector3 => new { x = v.AsVector3().X, y = v.AsVector3().Y, z = v.AsVector3().Z },
        _ => v.ToString()
    };

    private Variant ToVar(object? o) => o switch
    {
        null => default,
        bool b => b,
        int i => i,
        long l => l,
        float f => f,
        double d => d,
        string s => s,
        JsonElement je => je.ValueKind switch
        {
            JsonValueKind.Null => default,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => je.GetDouble(),
            JsonValueKind.String => je.GetString() ?? "",
            _ => je.ToString()
        },
        _ => o.ToString() ?? ""
    };

    private ApiResponse Ok(object? d) => new() { Success = true, Data = d };
    private ApiResponse Err(string msg) => new() { Success = false, Error = msg };
    private ApiResponse ErrorResponse(string msg) => new() { Success = false, Error = msg };

    public override void _ExitTree()
    {
        _isRunning = false;
        _httpListener?.Stop();
        _httpListener?.Close();
        GD.Print("[MCP] 已停止");
    }
}

// ========== 强类型请求模型 ==========

public class SceneTreeRequest
{
    [JsonPropertyName("includeProperties")]
    public bool IncludeProperties { get; set; }
}

public class NodePathRequest
{
    [JsonPropertyName("nodePath")]
    public string NodePath { get; set; } = "";
}

public class CreateNodeRequest
{
    [JsonPropertyName("parentPath")]
    public string ParentPath { get; set; } = "";
    
    [JsonPropertyName("nodeType")]
    public string NodeType { get; set; } = "";
    
    [JsonPropertyName("nodeName")]
    public string NodeName { get; set; } = "";
}

public class ScenePathRequest
{
    [JsonPropertyName("scenePath")]
    public string ScenePath { get; set; } = "";
}

public class PropertyRequest
{
    [JsonPropertyName("nodePath")]
    public string NodePath { get; set; } = "";
    
    [JsonPropertyName("propertyName")]
    public string PropertyName { get; set; } = "";
}

public class SetPropertyRequest
{
    [JsonPropertyName("nodePath")]
    public string NodePath { get; set; } = "";
    
    [JsonPropertyName("propertyName")]
    public string PropertyName { get; set; } = "";
    
    [JsonPropertyName("value")]
    public object? Value { get; set; }
}

public class CallMethodRequest
{
    [JsonPropertyName("nodePath")]
    public string NodePath { get; set; } = "";
    
    [JsonPropertyName("methodName")]
    public string MethodName { get; set; } = "";
    
    [JsonPropertyName("args")]
    public List<object>? Args { get; set; }
}

public class CodeRequest
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "";
}

public class ResourcePathRequest
{
    [JsonPropertyName("resourcePath")]
    public string ResourcePath { get; set; } = "";
}

public class ListResourcesRequest
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "res://";
    
    [JsonPropertyName("filter")]
    public string? Filter { get; set; }
}

public class LogsRequest
{
    [JsonPropertyName("count")]
    public int Count { get; set; } = 50;
}

public class ScreenshotRequest
{
    [JsonPropertyName("savePath")]
    public string? SavePath { get; set; }
}

// ========== 响应模型 ==========

public class ApiResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    
    [JsonPropertyName("data")]
    public object? Data { get; set; }
    
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public class NodeInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";
    
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";
    
    [JsonPropertyName("children")]
    public List<string> Children { get; set; } = new();
    
    [JsonPropertyName("properties")]
    public Dictionary<string, object?> Properties { get; set; } = new();
    
    [JsonPropertyName("methods")]
    public List<string> Methods { get; set; } = new();
    
    [JsonPropertyName("signals")]
    public List<string> Signals { get; set; } = new();
}

public class LogEntry
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }
    
    [JsonPropertyName("level")]
    public string Level { get; set; } = "";
    
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}
