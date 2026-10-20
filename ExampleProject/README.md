# Godot Runtime MCP v3 demo (Godot 4.7 .NET)

Build and launch: `dotnet build ExampleProject/ExampleProject.csproj` followed by Godot `--headless --path ExampleProject`. The C# backend is registered as an Autoload in `project.godot` (no editor plugin).

List instances with `./bin/godot-mcp instances`, inspect via `./bin/godot-mcp call get_scene_tree_simple --project ExampleProject`. Run two game processes concurrently to verify that `--instance ID` is required when routing would otherwise be ambiguous. It does not use port 7777.
