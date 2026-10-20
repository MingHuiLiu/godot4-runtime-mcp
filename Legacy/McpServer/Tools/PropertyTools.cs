using System.ComponentModel;
using McpServer.Services;
using ModelContextProtocol.Server;

namespace McpServer.Tools;

/// <summary>
/// Godot 节点属性操作工具
/// </summary>
[McpServerToolType]
public class PropertyTools
{
    private readonly GodotClient _godotClient;

    public PropertyTools(GodotClient godotClient)
    {
        _godotClient = godotClient;
    }

    [McpServerTool, Description("获取节点属性值")]
    public async Task<string> GetProperty(
        [Description("节点路径")] string nodePath,
        [Description("属性名称，例如 'position', 'visible'")] string propertyName)
    {
        return await _godotClient.GetPropertyAsync(nodePath, propertyName);
    }

    [McpServerTool, Description("设置节点属性值")]
    public async Task<string> SetProperty(
        [Description("节点路径")] string nodePath,
        [Description("属性名称")] string propertyName,
        [Description("属性值 (JSON格式)")] object value)
    {
        return await _godotClient.SetPropertyAsync(nodePath, propertyName, value);
    }

    [McpServerTool, Description("列出节点所有属性")]
    public async Task<string> ListProperties(
        [Description("节点路径")] string nodePath)
    {
        return await _godotClient.ListPropertiesAsync(nodePath);
    }
}
