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

    [McpServerTool, Description("获取节点的直接子节点列表 (不递归,轻量级)")]
    public async Task<string> GetNodeChildren(
        [Description("节点路径")] string nodePath)
    {
        return await _godotClient.GetNodeChildrenAsync(nodePath);
    }

    [McpServerTool, Description("获取节点的父节点路径")]
    public async Task<string> GetNodeParent(
        [Description("节点路径")] string nodePath)
    {
        return await _godotClient.GetNodeParentAsync(nodePath);
    }

    [McpServerTool, Description("查找指定类型的所有节点")]
    public async Task<string> FindNodesByType(
        [Description("节点类型，例如 'Sprite2D', 'Camera2D'")] string nodeType,
        [Description("搜索根节点路径，默认 '/root'")] string rootPath = "/root")
    {
        return await _godotClient.FindNodesByTypeAsync(nodeType, rootPath);
    }

    [McpServerTool, Description("按名称搜索节点 (支持模糊匹配)")]
    public async Task<string> FindNodesByName(
        [Description("节点名称或名称片段")] string namePattern,
        [Description("搜索根节点路径，默认 '/root'")] string rootPath = "/root")
    {
        return await _godotClient.FindNodesByNameAsync(namePattern, rootPath);
    }

    [McpServerTool, Description("获取场景树统计信息 (节点数量、类型分布等)")]
    public async Task<string> GetSceneTreeStats(
        [Description("统计根节点路径，默认 '/root'")] string rootPath = "/root")
    {
        return await _godotClient.GetSceneTreeStatsAsync(rootPath);
    }

    [McpServerTool, Description("检查节点是否存在")]
    public async Task<string> NodeExists(
        [Description("节点路径")] string nodePath)
    {
        return await _godotClient.NodeExistsAsync(nodePath);
    }

    [McpServerTool, Description("获取节点的子树 (指定深度,避免完整树太大)")]
    public async Task<string> GetNodeSubtree(
        [Description("节点路径")] string nodePath,
        [Description("递归深度 (0=仅当前节点, 1=包含直接子节点, -1=无限深度)")] int maxDepth = 2)
    {
        return await _godotClient.GetNodeSubtreeAsync(nodePath, maxDepth);
    }
}
