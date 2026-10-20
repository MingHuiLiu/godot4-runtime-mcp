using System.ComponentModel;
using McpServer.Services;
using ModelContextProtocol.Server;

namespace McpServer.Tools;

/// <summary>
/// Godot C# 脚本执行工具
/// </summary>
[McpServerToolType]
public class ScriptTools
{
    private readonly GodotClient _godotClient;

    public ScriptTools(GodotClient godotClient)
    {
        _godotClient = godotClient;
    }

    [McpServerTool, Description("执行 C# 代码片段")]
    public async Task<string> ExecuteCSharp(
        [Description("要执行的 C# 代码")] string code)
    {
        return await _godotClient.ExecuteCSharpAsync(code);
    }

    [McpServerTool, Description("获取全局变量")]
    public async Task<string> GetGlobalVariables()
    {
        return await _godotClient.GetGlobalVariablesAsync();
    }
}
