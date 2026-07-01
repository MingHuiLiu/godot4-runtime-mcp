#if TOOLS
using Godot;
using System;

[Tool]
public partial class GodotMcpPlugin : EditorPlugin
{
    private const string AutoloadName = "GodotMcpServer";
    private const string AutoloadScene = "res://addons/godot_mcp/mcp_server.tscn";

    public override void _EnterTree()
    {
        GD.Print("[GodotMcp][Plugin] Enabled — registering autoload");

        if (Engine.IsEditorHint())
        {
            if (!ProjectSettings.HasSetting($"autoload/{AutoloadName}"))
            {
                AddAutoloadSingleton(AutoloadName, AutoloadScene);
                GD.Print($"[GodotMcp][Plugin] Autoload '{AutoloadName}' registered via scene");
            }
            else
            {
                GD.Print($"[GodotMcp][Plugin] Autoload '{AutoloadName}' already registered");
            }
        }
    }

    public override void _ExitTree()
    {
        if (Engine.IsEditorHint())
        {
            if (ProjectSettings.HasSetting($"autoload/{AutoloadName}"))
            {
                RemoveAutoloadSingleton(AutoloadName);
                GD.Print($"[GodotMcp][Plugin] Autoload '{AutoloadName}' removed");
            }
        }
        GD.Print("[GodotMcp][Plugin] Disabled");
    }
}
#endif
