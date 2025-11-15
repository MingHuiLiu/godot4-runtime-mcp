using Godot;
using System;

#if TOOLS
[Tool]
#endif
public partial class McpPlugin : EditorPlugin
{
    private const string AutoloadName = "McpClientAutoload";
    private const string AutoloadPath = "res://addons/mcp_client/McpClient.cs";

    public override void _EnterTree()
    {
        GD.Print("MCP Plugin 已启用");
        
        // 在编辑器模式下，自动添加 McpClient 为 AutoLoad
        if (Engine.IsEditorHint())
        {
            // 检查是否已经添加了 AutoLoad
            if (!ProjectSettings.HasSetting($"autoload/{AutoloadName}"))
            {
                AddAutoloadSingleton(AutoloadName, AutoloadPath);
                GD.Print($"已添加 {AutoloadName} 到 AutoLoad");
            }
        }
    }

    public override void _ExitTree()
    {
        // 插件禁用时移除 AutoLoad
        if (Engine.IsEditorHint())
        {
            if (ProjectSettings.HasSetting($"autoload/{AutoloadName}"))
            {
                RemoveAutoloadSingleton(AutoloadName);
                GD.Print($"已从 AutoLoad 移除 {AutoloadName}");
            }
        }
        GD.Print("MCP Plugin 已禁用");
    }
}
