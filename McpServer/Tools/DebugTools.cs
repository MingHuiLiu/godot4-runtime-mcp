using System.ComponentModel;
using McpServer.Services;
using ModelContextProtocol.Server;

namespace McpServer.Tools;

/// <summary>
/// Godot 调试和性能监控工具
/// </summary>
[McpServerToolType]
public class DebugTools
{
    private readonly GodotClient _godotClient;

    public DebugTools(GodotClient godotClient)
    {
        _godotClient = godotClient;
    }

    [McpServerTool, Description("获取游戏日志输出")]
    public async Task<string> GetLogs(
        [Description("日志数量")] int count = 50)
    {
        return await _godotClient.GetLogsAsync(count);
    }

    [McpServerTool, Description("获取性能统计信息")]
    public async Task<string> GetPerformanceStats()
    {
        return await _godotClient.GetPerformanceStatsAsync();
    }

    [McpServerTool, Description("截取游戏画面")]
    public async Task<string> TakeScreenshot(
        [Description("保存路径 (可选)，例如 'user://screenshot.png'")] string? savePath = null)
    {
        return await _godotClient.TakeScreenshotAsync(savePath);
    }

    [McpServerTool, Description("获取当前时间")]
    public async Task<string> GetTime()
    {
        return await _godotClient.GetTimeAsync();
    }
}
