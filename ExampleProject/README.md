# Example Godot C# game

This small Godot .NET project is a testing fixture for the shared backend. It intentionally does not vendor a second copy of the Godot addon.

From this repository root:

    npm run pack
    node bin/godot-mcp.js install ExampleProject --archive dist/godot-mcp-addon.zip
    dotnet build ExampleProject/ExampleProject.csproj
    # Run ExampleProject in Godot .NET
    node bin/godot-mcp.js instances
    node bin/godot-mcp.js call get_scene_tree --project ExampleProject

For normal users, replace --archive with the latest GitHub Release download (omit that option). The first release ZIP must be uploaded manually; no GitHub Actions are used.

See ../README.md and ../skills/godot-runtime/SKILL.md for multi-project and multi-process routing.
