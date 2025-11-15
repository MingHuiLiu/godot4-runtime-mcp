using Refit;
using McpServer.Models;

namespace McpServer.Services;

/// <summary>
/// Godot HTTP API 接口 - 每个方法独立的 HTTP 端点
/// </summary>
public interface IGodotApi
{
    // ========== 场景树操作 ==========
    
    [Post("/get_scene_tree")]
    Task<GodotResponse> GetSceneTreeAsync([Body] SceneTreeRequest request);
    
    [Post("/get_node_info")]
    Task<GodotResponse> GetNodeInfoAsync([Body] NodePathRequest request);
    
    [Post("/create_node")]
    Task<GodotResponse> CreateNodeAsync([Body] CreateNodeRequest request);
    
    [Post("/delete_node")]
    Task<GodotResponse> DeleteNodeAsync([Body] NodePathRequest request);
    
    [Post("/load_scene")]
    Task<GodotResponse> LoadSceneAsync([Body] ScenePathRequest request);
    
    // ========== 属性操作 ==========
    
    [Post("/get_property")]
    Task<GodotResponse> GetPropertyAsync([Body] PropertyRequest request);
    
    [Post("/set_property")]
    Task<GodotResponse> SetPropertyAsync([Body] SetPropertyRequest request);
    
    [Post("/list_properties")]
    Task<GodotResponse> ListPropertiesAsync([Body] NodePathRequest request);
    
    // ========== 方法调用 ==========
    
    [Post("/call_method")]
    Task<GodotResponse> CallMethodAsync([Body] CallMethodRequest request);
    
    [Post("/list_methods")]
    Task<GodotResponse> ListMethodsAsync([Body] NodePathRequest request);
    
    // ========== 脚本和资源 ==========
    
    [Post("/execute_csharp")]
    Task<GodotResponse> ExecuteCSharpAsync([Body] CodeRequest request);
    
    [Post("/get_global_variables")]
    Task<GodotResponse> GetGlobalVariablesAsync();
    
    [Post("/get_resource_info")]
    Task<GodotResponse> GetResourceInfoAsync([Body] ResourcePathRequest request);
    
    [Post("/list_resources")]
    Task<GodotResponse> ListResourcesAsync([Body] ListResourcesRequest request);
    
    [Post("/load_resource")]
    Task<GodotResponse> LoadResourceAsync([Body] ResourcePathRequest request);
    
    // ========== 调试工具 ==========
    
    [Post("/get_performance_stats")]
    Task<GodotResponse> GetPerformanceStatsAsync();
    
    [Post("/get_logs")]
    Task<GodotResponse> GetLogsAsync([Body] LogsRequest request);
    
    [Post("/take_screenshot")]
    Task<GodotResponse> TakeScreenshotAsync([Body] ScreenshotRequest request);
    
    [Post("/get_time")]
    Task<GodotResponse> GetTimeAsync();
}
