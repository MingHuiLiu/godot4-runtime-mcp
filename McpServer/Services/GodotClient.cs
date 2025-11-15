using System.Text.Json;
using Microsoft.Extensions.Logging;
using McpServer.Models;

namespace McpServer.Services;

/// <summary>
/// 与 Godot 游戏通信的强类型 HTTP 客户端 v5.0 - 每个 API 都有明确的方法和参数
/// </summary>
public class GodotClient : IDisposable
{
    private readonly ILogger<GodotClient> _logger;
    private readonly IGodotApi _godotApi;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public GodotClient(ILogger<GodotClient> logger, IGodotApi godotApi)
    {
        _logger = logger;
        _godotApi = godotApi;
    }

    // ========== 场景树操作 ==========

    public async Task<string> GetSceneTreeAsync(bool includeProperties = false)
    {
        return await ExecuteAsync("GetSceneTree", async () => 
            await _godotApi.GetSceneTreeAsync(new SceneTreeRequest { IncludeProperties = includeProperties }));
    }

    public async Task<string> GetNodeInfoAsync(string nodePath)
    {
        return await ExecuteAsync("GetNodeInfo", async () => 
            await _godotApi.GetNodeInfoAsync(new NodePathRequest { NodePath = nodePath }));
    }

    public async Task<string> CreateNodeAsync(string parentPath, string nodeType, string nodeName)
    {
        return await ExecuteAsync("CreateNode", async () => 
            await _godotApi.CreateNodeAsync(new CreateNodeRequest 
            { 
                ParentPath = parentPath, 
                NodeType = nodeType, 
                NodeName = nodeName 
            }));
    }

    public async Task<string> DeleteNodeAsync(string nodePath)
    {
        return await ExecuteAsync("DeleteNode", async () => 
            await _godotApi.DeleteNodeAsync(new NodePathRequest { NodePath = nodePath }));
    }

    public async Task<string> LoadSceneAsync(string scenePath)
    {
        return await ExecuteAsync("LoadScene", async () => 
            await _godotApi.LoadSceneAsync(new ScenePathRequest { ScenePath = scenePath }));
    }

    // ========== 属性操作 ==========

    public async Task<string> GetPropertyAsync(string nodePath, string propertyName)
    {
        return await ExecuteAsync("GetProperty", async () => 
            await _godotApi.GetPropertyAsync(new PropertyRequest 
            { 
                NodePath = nodePath, 
                PropertyName = propertyName 
            }));
    }

    public async Task<string> SetPropertyAsync(string nodePath, string propertyName, object value)
    {
        return await ExecuteAsync("SetProperty", async () => 
            await _godotApi.SetPropertyAsync(new SetPropertyRequest 
            { 
                NodePath = nodePath, 
                PropertyName = propertyName, 
                Value = value 
            }));
    }

    public async Task<string> ListPropertiesAsync(string nodePath)
    {
        return await ExecuteAsync("ListProperties", async () => 
            await _godotApi.ListPropertiesAsync(new NodePathRequest { NodePath = nodePath }));
    }

    // ========== 方法调用 ==========

    public async Task<string> CallMethodAsync(string nodePath, string methodName, List<object>? args = null)
    {
        return await ExecuteAsync("CallMethod", async () => 
            await _godotApi.CallMethodAsync(new CallMethodRequest 
            { 
                NodePath = nodePath, 
                MethodName = methodName, 
                Args = args 
            }));
    }

    public async Task<string> ListMethodsAsync(string nodePath)
    {
        return await ExecuteAsync("ListMethods", async () => 
            await _godotApi.ListMethodsAsync(new NodePathRequest { NodePath = nodePath }));
    }

    // ========== 脚本和资源 ==========

    public async Task<string> ExecuteCSharpAsync(string code)
    {
        return await ExecuteAsync("ExecuteCSharp", async () => 
            await _godotApi.ExecuteCSharpAsync(new CodeRequest { Code = code }));
    }

    public async Task<string> GetGlobalVariablesAsync()
    {
        return await ExecuteAsync("GetGlobalVariables", async () => 
            await _godotApi.GetGlobalVariablesAsync());
    }

    public async Task<string> GetResourceInfoAsync(string resourcePath)
    {
        return await ExecuteAsync("GetResourceInfo", async () => 
            await _godotApi.GetResourceInfoAsync(new ResourcePathRequest { ResourcePath = resourcePath }));
    }

    public async Task<string> ListResourcesAsync(string path = "res://", string? filter = null)
    {
        return await ExecuteAsync("ListResources", async () => 
            await _godotApi.ListResourcesAsync(new ListResourcesRequest 
            { 
                Path = path, 
                Filter = filter 
            }));
    }

    public async Task<string> LoadResourceAsync(string resourcePath)
    {
        return await ExecuteAsync("LoadResource", async () => 
            await _godotApi.LoadResourceAsync(new ResourcePathRequest { ResourcePath = resourcePath }));
    }

    // ========== 调试工具 ==========

    public async Task<string> GetPerformanceStatsAsync()
    {
        return await ExecuteAsync("GetPerformanceStats", async () => 
            await _godotApi.GetPerformanceStatsAsync());
    }

    public async Task<string> GetLogsAsync(int count = 50)
    {
        return await ExecuteAsync("GetLogs", async () => 
            await _godotApi.GetLogsAsync(new LogsRequest { Count = count }));
    }

    public async Task<string> TakeScreenshotAsync(string? savePath = null)
    {
        return await ExecuteAsync("TakeScreenshot", async () => 
            await _godotApi.TakeScreenshotAsync(new ScreenshotRequest { SavePath = savePath }));
    }

    public async Task<string> GetTimeAsync()
    {
        return await ExecuteAsync("GetTime", async () => 
            await _godotApi.GetTimeAsync());
    }

    // ========== 扩展场景树查询方法 ==========

    public async Task<string> GetNodeChildrenAsync(string nodePath)
    {
        return await ExecuteAsync("GetNodeChildren", async () => 
            await _godotApi.GetNodeChildrenAsync(new NodePathRequest { NodePath = nodePath }));
    }

    public async Task<string> GetNodeParentAsync(string nodePath)
    {
        return await ExecuteAsync("GetNodeParent", async () => 
            await _godotApi.GetNodeParentAsync(new NodePathRequest { NodePath = nodePath }));
    }

    public async Task<string> FindNodesByTypeAsync(string nodeType, string rootPath = "/root")
    {
        return await ExecuteAsync("FindNodesByType", async () => 
            await _godotApi.FindNodesByTypeAsync(new FindNodesRequest 
            { 
                NodeType = nodeType, 
                RootPath = rootPath 
            }));
    }

    public async Task<string> FindNodesByNameAsync(string namePattern, string rootPath = "/root", bool caseSensitive = false, bool exactMatch = false)
    {
        return await ExecuteAsync("FindNodesByName", async () => 
            await _godotApi.FindNodesByNameAsync(new FindNodesRequest 
            { 
                NamePattern = namePattern, 
                RootPath = rootPath,
                CaseSensitive = caseSensitive,
                ExactMatch = exactMatch
            }));
    }

    public async Task<string> FindNodesByGroupAsync(string groupName, string rootPath = "/root")
    {
        return await ExecuteAsync("FindNodesByGroup", async () => 
            await _godotApi.FindNodesByGroupAsync(new FindNodesRequest 
            { 
                GroupName = groupName, 
                RootPath = rootPath 
            }));
    }

    public async Task<string> GetNodeAncestorsAsync(string nodePath, int levels = -1, bool includeSiblings = false)
    {
        return await ExecuteAsync("GetNodeAncestors", async () => 
            await _godotApi.GetNodeAncestorsAsync(new AncestorsRequest 
            { 
                NodePath = nodePath, 
                Levels = levels,
                IncludeSiblings = includeSiblings
            }));
    }

    public async Task<string> GetSceneTreeStatsAsync(string rootPath = "/root")
    {
        return await ExecuteAsync("GetSceneTreeStats", async () => 
            await _godotApi.GetSceneTreeStatsAsync(new NodePathRequest { NodePath = rootPath }));
    }

    public async Task<string> NodeExistsAsync(string nodePath)
    {
        return await ExecuteAsync("NodeExists", async () => 
            await _godotApi.NodeExistsAsync(new NodePathRequest { NodePath = nodePath }));
    }

    public async Task<string> GetNodeSubtreeAsync(string nodePath, int maxDepth = 2, bool includeProperties = false)
    {
        return await ExecuteAsync("GetNodeSubtree", async () => 
            await _godotApi.GetNodeSubtreeAsync(new SubtreeRequest 
            { 
                NodePath = nodePath, 
                MaxDepth = maxDepth,
                IncludeProperties = includeProperties
            }));
    }

    public async Task<string> SearchNodesAsync(string? namePattern, string? nodeType, string? groupName, string rootPath = "/root", int maxResults = 50)
    {
        return await ExecuteAsync("SearchNodes", async () => 
            await _godotApi.SearchNodesAsync(new FindNodesRequest 
            { 
                NamePattern = namePattern,
                NodeType = nodeType,
                GroupName = groupName,
                RootPath = rootPath,
                MaxResults = maxResults
            }));
    }

    public async Task<string> GetNodeContextAsync(string nodePath, bool includeParent = true, bool includeSiblings = true, bool includeChildren = true)
    {
        return await ExecuteAsync("GetNodeContext", async () => 
            await _godotApi.GetNodeContextAsync(new NodeContextRequest 
            { 
                NodePath = nodePath,
                IncludeParent = includeParent,
                IncludeSiblings = includeSiblings,
                IncludeChildren = includeChildren
            }));
    }

    // ========== 内部辅助方法 ==========

    /// <summary>
    /// 统一的异步执行和错误处理
    /// </summary>
    private async Task<string> ExecuteAsync(string methodName, Func<Task<GodotResponse>> action)
    {
        try
        {
            _logger.LogInformation("调用 Godot 方法: {Method}", methodName);
            
            var response = await action();
            
            LogResponse(methodName, response);
            return SerializeResponse(response);
        }
        catch (Refit.ApiException ex)
        {
            return HandleApiException(ex);
        }
        catch (HttpRequestException ex)
        {
            return HandleConnectionError(ex);
        }
        catch (Exception ex)
        {
            return HandleGenericError(methodName, ex);
        }
    }

    /// <summary>
    /// 记录响应日志
    /// </summary>
    private void LogResponse(string method, GodotResponse response)
    {
        _logger.LogInformation("调用结果: Success={Success}", response.Success);
        
        if (!response.Success)
        {
            _logger.LogError("调用失败: {Method} - {Error}", method, response.Error);
        }
        else
        {
            _logger.LogInformation("✓ 调用成功: {Method}", method);
        }
    }

    /// <summary>
    /// 序列化响应对象
    /// </summary>
    private string SerializeResponse(GodotResponse response)
    {
        return JsonSerializer.Serialize(response, JsonOptions);
    }

    /// <summary>
    /// 处理 API 异常
    /// </summary>
    private string HandleApiException(Refit.ApiException ex)
    {
        _logger.LogError("HTTP API 错误: {StatusCode} - {Content}", ex.StatusCode, ex.Content);
        return SerializeResponse(new GodotResponse
        {
            Success = false,
            Error = $"HTTP {ex.StatusCode}: {ex.Content}"
        });
    }

    /// <summary>
    /// 处理连接错误
    /// </summary>
    private string HandleConnectionError(HttpRequestException ex)
    {
        _logger.LogWarning("无法连接到 Godot: {Message}", ex.Message);
        _logger.LogWarning("请确保 Godot 游戏正在运行并且 HTTP 服务器已启动");
        return SerializeResponse(new GodotResponse
        {
            Success = false,
            Error = "未连接到 Godot,请确保游戏正在运行"
        });
    }

    /// <summary>
    /// 处理通用错误
    /// </summary>
    private string HandleGenericError(string method, Exception ex)
    {
        _logger.LogError(ex, "发送请求到 Godot 时出错: {Method}", method);
        return SerializeResponse(new GodotResponse
        {
            Success = false,
            Error = ex.Message
        });
    }

    public void Dispose()
    {
        // Refit 客户端由 DI 容器管理,不需要手动释放
        GC.SuppressFinalize(this);
    }
}
