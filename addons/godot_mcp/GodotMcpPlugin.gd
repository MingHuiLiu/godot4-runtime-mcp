@tool
extends EditorPlugin

const AUTOLOAD_NAME := "GodotMcpServer"
const AUTOLOAD_SCENE := "res://addons/godot_mcp/mcp_server.tscn"

func _enter_tree() -> void:
    if not ProjectSettings.has_setting("autoload/" + AUTOLOAD_NAME):
        add_autoload_singleton(AUTOLOAD_NAME, AUTOLOAD_SCENE)

func _exit_tree() -> void:
    # Editor shutdown also calls this method; leave installed autoload intact.
    # Users removing the addon can remove the autoload in Project Settings.
    pass
