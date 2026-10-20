using System.ComponentModel;
using McpServer.Services;
using ModelContextProtocol.Server;

namespace McpServer.Tools;

/// <summary>
/// Godot 信号系统调试工具
/// </summary>
[McpServerToolType]
public class SignalTools
{
    private readonly GodotClient _godotClient;

    public SignalTools(GodotClient godotClient)
    {
        _godotClient = godotClient;
    }

    [McpServerTool, Description("获取节点的所有信号")]
    public async Task<string> GetNodeSignals(
        [Description("节点路径")] string nodePath)
    {
        return await _godotClient.GetNodeSignalsAsync(nodePath);
    }

    [McpServerTool, Description("获取信号的连接信息")]
    public async Task<string> GetSignalConnections(
        [Description("节点路径")] string nodePath,
        [Description("信号名称")] string signalName)
    {
        return await _godotClient.GetSignalConnectionsAsync(nodePath, signalName);
    }

    [McpServerTool, Description("连接信号到方法")]
    public async Task<string> ConnectSignal(
        [Description("源节点路径")] string sourceNodePath,
        [Description("信号名称")] string signalName,
        [Description("目标节点路径")] string targetNodePath,
        [Description("目标方法名")] string targetMethod)
    {
        return await _godotClient.ConnectSignalAsync(sourceNodePath, signalName, targetNodePath, targetMethod);
    }

    [McpServerTool, Description("断开信号连接")]
    public async Task<string> DisconnectSignal(
        [Description("源节点路径")] string sourceNodePath,
        [Description("信号名称")] string signalName,
        [Description("目标节点路径")] string targetNodePath,
        [Description("目标方法名")] string targetMethod)
    {
        return await _godotClient.DisconnectSignalAsync(sourceNodePath, signalName, targetNodePath, targetMethod);
    }

    [McpServerTool, Description("发射自定义信号 (用于测试)")]
    public async Task<string> EmitSignal(
        [Description("节点路径")] string nodePath,
        [Description("信号名称")] string signalName,
        [Description("信号参数 (可选)")] List<object>? args = null)
    {
        return await _godotClient.EmitSignalAsync(nodePath, signalName, args);
    }

    [McpServerTool, Description("监听信号事件 (设置过滤器)")]
    public async Task<string> StartSignalMonitoring(
        [Description("要监听的信号名称 (可选,为空则监听所有)")] string? signalName = null,
        [Description("最大记录数量 (已自动管理)")] int maxEvents = 5000)
    {
        return await _godotClient.StartSignalMonitoringAsync(null, signalName, maxEvents);
    }

    [McpServerTool, Description("停止监听信号事件 (查看统计)")]
    public async Task<string> StopSignalMonitoring()
    {
        return await _godotClient.StopSignalMonitoringAsync();
    }

    [McpServerTool, Description("获取已记录的信号事件 (支持时间范围查询)")]
    public async Task<string> GetSignalEvents(
        [Description("获取最近 N 条事件")] int count = 50,
        [Description("按节点路径过滤 (可选,支持部分匹配)")] string? nodePath = null,
        [Description("按信号名称过滤 (可选)")] string? signalName = null,
        [Description("开始时间 (Unix时间戳,可选)")] long? startTime = null,
        [Description("结束时间 (Unix时间戳,可选)")] long? endTime = null)
    {
        return await _godotClient.GetSignalEventsAsync(count, nodePath, signalName, startTime, endTime);
    }

    [McpServerTool, Description("清空信号事件记录")]
    public async Task<string> ClearSignalEvents()
    {
        return await _godotClient.ClearSignalEventsAsync();
    }
}
