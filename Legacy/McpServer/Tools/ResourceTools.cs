using System.ComponentModel;
using McpServer.Services;
using ModelContextProtocol.Server;

namespace McpServer.Tools;

/// <summary>
/// Godot 资源管理工具
/// </summary>
[McpServerToolType]
public class ResourceTools
{
    private readonly GodotClient _godotClient;

    public ResourceTools(GodotClient godotClient)
    {
        _godotClient = godotClient;
    }

    [McpServerTool, Description("列出指定目录下的资源")]
    public async Task<string> ListResources(
        [Description("资源目录路径，例如 'res://assets/'")] string path = "res://",
        [Description("资源过滤器 (可选)")] string? filter = null)
    {
        return await _godotClient.ListResourcesAsync(path, filter);
    }

    [McpServerTool, Description("加载资源")]
    public async Task<string> LoadResource(
        [Description("资源路径，例如 'res://icon.png'")] string resourcePath)
    {
        return await _godotClient.LoadResourceAsync(resourcePath);
    }

    [McpServerTool, Description("获取资源信息")]
    public async Task<string> GetResourceInfo(
        [Description("资源路径")] string resourcePath)
    {
        return await _godotClient.GetResourceInfoAsync(resourcePath);
    }
}
