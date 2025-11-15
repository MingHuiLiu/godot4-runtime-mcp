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

    [McpServerTool, Description("获取过滤后的日志")]
    public async Task<string> GetLogsFiltered(
        [Description("日志级别过滤: 'error', 'warning', 'info', 'debug' (可选)")] string? level = null,
        [Description("消息内容过滤 (可选,支持部分匹配)")] string? messagePattern = null,
        [Description("时间范围-开始 (可选,Unix时间戳)")] long? startTime = null,
        [Description("时间范围-结束 (可选,Unix时间戳)")] long? endTime = null,
        [Description("最大返回数量")] int maxCount = 100)
    {
        return await _godotClient.GetLogsFilteredAsync(level, messagePattern, startTime, endTime, maxCount);
    }

    [McpServerTool, Description("获取日志统计信息")]
    public async Task<string> GetLogStats()
    {
        return await _godotClient.GetLogStatsAsync();
    }

    [McpServerTool, Description("导出所有日志到文件")]
    public async Task<string> ExportLogs(
        [Description("导出文件路径 (可选,默认 user://logs_export.txt)")] string? filePath = null)
    {
        return await _godotClient.ExportLogsAsync(filePath);
    }

    [McpServerTool, Description("清空日志缓冲区")]
    public async Task<string> ClearLogs()
    {
        return await _godotClient.ClearLogsAsync();
    }

    [McpServerTool, Description("添加自定义日志条目 (用于标记调试点)")]
    public async Task<string> AddCustomLog(
        [Description("日志消息")] string message,
        [Description("日志级别: 'info', 'warning', 'error', 'debug'")] string level = "info")
    {
        return await _godotClient.AddCustomLogAsync(message, level);
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
