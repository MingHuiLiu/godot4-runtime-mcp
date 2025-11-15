using System.ComponentModel;
using System.Text.Json;
using McpServer.Services;
using ModelContextProtocol.Server;

namespace McpServer.Tools;

/// <summary>
/// Godot 场景管理工具
/// </summary>
[McpServerToolType]
public class SceneTools
{
    private readonly GodotClient _godotClient;

    public SceneTools(GodotClient godotClient)
    {
        _godotClient = godotClient;
    }

    [McpServerTool, Description("获取当前场景树结构")]
    public async Task<string> GetSceneTree(
        [Description("是否包含节点属性")] bool includeProperties = false)
    {
        return await _godotClient.GetSceneTreeAsync(includeProperties);
    }

    [McpServerTool, Description("获取指定节点的详细信息")]
    public async Task<string> GetNodeInfo(
        [Description("节点路径，例如 '/root/Main/Player'")] string nodePath)
    {
        return await _godotClient.GetNodeInfoAsync(nodePath);
    }

    [McpServerTool, Description("创建新节点")]
    public async Task<string> CreateNode(
        [Description("父节点路径")] string parentPath,
        [Description("节点类型，例如 'Node2D', 'Sprite2D'")] string nodeType,
        [Description("新节点名称")] string nodeName)
    {
        return await _godotClient.CreateNodeAsync(parentPath, nodeType, nodeName);
    }

    [McpServerTool, Description("删除节点")]
    public async Task<string> DeleteNode(
        [Description("要删除的节点路径")] string nodePath)
    {
        return await _godotClient.DeleteNodeAsync(nodePath);
    }

    [McpServerTool, Description("加载场景")]
    public async Task<string> LoadScene(
        [Description("场景文件路径，例如 'res://scenes/level1.tscn'")] string scenePath)
    {
        return await _godotClient.LoadSceneAsync(scenePath);
    }
}
