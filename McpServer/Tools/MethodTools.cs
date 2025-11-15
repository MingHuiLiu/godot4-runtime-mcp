using System.ComponentModel;
using McpServer.Services;
using ModelContextProtocol.Server;

namespace McpServer.Tools;

/// <summary>
/// Godot 节点方法调用工具
/// </summary>
[McpServerToolType]
public class MethodTools
{
    private readonly GodotClient _godotClient;

    public MethodTools(GodotClient godotClient)
    {
        _godotClient = godotClient;
    }

    [McpServerTool, Description("调用节点方法")]
    public async Task<string> CallMethod(
        [Description("节点路径")] string nodePath,
        [Description("方法名称")] string methodName,
        [Description("方法参数列表")] List<object>? args = null)
    {
        return await _godotClient.CallMethodAsync(nodePath, methodName, args);
    }

    [McpServerTool, Description("列出节点所有方法")]
    public async Task<string> ListMethods(
        [Description("节点路径")] string nodePath)
    {
        return await _godotClient.ListMethodsAsync(nodePath);
    }
}
