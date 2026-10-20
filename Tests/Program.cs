using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using GodotRuntimeMcp;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
var scratch = Path.Combine(Path.GetTempPath(), "godot-mcp-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
Environment.SetEnvironmentVariable("GODOT_MCP_REGISTRY_DIR", Path.Combine(scratch, "registry"));
Environment.SetEnvironmentVariable("GODOT_MCP_CONFIG_DIR", Path.Combine(scratch, "config"));
var passed = 0;
void Assert(bool yes, string message)
{
    if (!yes) throw new Exception("FAILED: " + message);
    passed++;
    Console.WriteLine("PASS " + message);
}

try
{
    var tools = new ToolService();
    Assert(tools.Catalog().Count == 51, "All 46 runtime tools plus 5 management tools available offline");
    var backendImplementation = File.ReadAllText(Path.Combine(root, "addons", "godot_mcp", "GodotRuntimeBackend.cs"));
    var missingDispatches = tools.Catalog().Take(46)
        .Select(t => t!["name"]!.ToString())
        .Where(name => !backendImplementation.Contains("\"" + name + "\" =>", StringComparison.Ordinal))
        .ToArray();
    Assert(missingDispatches.Length == 0,
        "Canonical tool manifest matches all Godot C# runtime operation dispatches");
    var setSchema = tools.Catalog().First(t => t?["name"]?.ToString() == "set_property")!["inputSchema"]!["properties"]!["value"];
    Assert(setSchema?["type"] == null, "set_property schema accepts scalar and structured JSON values");
    var status = await tools.RunAsync("godot_status", new JsonObject());
    Assert(JsonSerializer.Serialize(status).Contains("\"instances\":[]"),
        "Offline status without Godot");
    var mcp = new McpProtocol(tools);
    var init = await mcp.DispatchAsync("""
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{}}}
        """);
    Assert(init?["result"]?["protocolVersion"]?.ToString() == "2024-11-05",
        "MCP initialize protocol negotiation offline");
    var list = await mcp.DispatchAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
    Assert(list?["result"]?["tools"]?.AsArray().Count == 51,
        "MCP tools/list offline has full catalog");
    Assert(await mcp.DispatchAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""") == null,
        "JSON-RPC notifications have no reply");
    var bad = await mcp.DispatchAsync("""{"jsonrpc":"2.0","id":"abc","method":"tools/call","params":{"name":"get_time"}}""");
    Assert(bad?["result"]?["isError"]?.GetValue<bool>() == true &&
        bad["id"]?.ToString() == "abc",
        "MCP live tool gracefully reports offline as tool error");
    var unknown = await mcp.DispatchAsync("""{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"fake"}}""");
    Assert(unknown?["error"]?["code"]?.GetValue<int>() == -32602,
        "Unknown tool returns JSON-RPC invalid-params");
    var missing = await mcp.DispatchAsync("""{"jsonrpc":"2.0","id":4,"method":"bogus"}""");
    Assert(missing?["error"]?["code"]?.GetValue<int>() == -32601,
        "Unknown method returns JSON-RPC method not found");

    var a = Path.Combine(scratch, "game-a");
    Directory.CreateDirectory(a);
    await File.WriteAllTextAsync(Path.Combine(a, "project.godot"), "[application]\nconfig/name=\"Game A\"\n");
    var source = root;
    var installed = await BackendInstaller.InstallAsync(a, "local", path: source);
    Assert(JsonSerializer.Serialize(installed).Contains("\"status\":\"installed\""),
        "Install from local C# backend source into project with no running Godot");
    var settings = await File.ReadAllTextAsync(Path.Combine(a, "project.godot"));
    Assert(settings.Contains("GodotRuntimeBackend=\"*res://addons/godot_mcp/runtime_backend.tscn\""),
        "Installer registers Godot autoload");
    var again = await BackendInstaller.InstallAsync(a, "local", path: source);
    Assert(JsonSerializer.Serialize(again).Contains("already-installed"),
        "Installation is idempotent");
    Assert((await File.ReadAllTextAsync(Path.Combine(a, "project.godot")))
        .Split("GodotRuntimeBackend=", StringSplitOptions.None).Length == 2,
        "Autoload registration is not duplicated");

    // The example is a standalone Godot project with a checked-in addon copy.
    // Guard against drifting from the canonical backend used by releases.
    var canonical = Path.Combine(root, "addons", "godot_mcp");
    var bundledExample = Path.Combine(root, "ExampleProject", "addons", "godot_mcp");
    var canonicalFiles = Directory.GetFiles(canonical).Select(Path.GetFileName).Order().ToArray();
    var exampleFiles = Directory.GetFiles(bundledExample)
        .Where(path => !path.EndsWith(".uid", StringComparison.Ordinal))
        .Select(Path.GetFileName).Order().ToArray();
    Assert(canonicalFiles.SequenceEqual(exampleFiles) &&
        canonicalFiles.All(name => File.ReadAllBytes(Path.Combine(canonical, name!))
            .AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(bundledExample, name!)))),
        "Standalone Godot example addon matches canonical release backend");

    // Old v3 autoload should be migrated instead of creating two live backends.
    var legacyProject = Path.Combine(scratch, "legacy-autoload");
    Directory.CreateDirectory(legacyProject);
    await File.WriteAllTextAsync(Path.Combine(legacyProject, "project.godot"),
        "[autoload]\nGodotMcpServer=\"*res://addons/godot_mcp/mcp_server.tscn\"\n");
    await BackendInstaller.InstallAsync(legacyProject, "local", path: root);
    var upgradedConfig = File.ReadAllText(Path.Combine(legacyProject, "project.godot"));
    Assert(upgradedConfig.Contains("\nGodotRuntimeBackend=\"*res://addons/godot_mcp/runtime_backend.tscn\"") &&
        !upgradedConfig.Contains("GodotMcpServer="),
        "Installer migrates old Autoload name without duplicate runtime");

    var zip = Path.Combine(scratch, "release-addon.zip");
    await BackendInstaller.PackAsync(source, zip);
    Assert(File.Exists(zip) && new FileInfo(zip).Length > 1000,
        "Release archive packaging creates ZIP with C# backend");
    var b = Path.Combine(scratch, "game-b");
    Directory.CreateDirectory(b);
    await File.WriteAllTextAsync(Path.Combine(b, "project.godot"),
        "[application]\nconfig/name=\"Game B\"\n");
    await File.WriteAllTextAsync(Path.Combine(b, "GameB.csproj"), "<Project Sdk=\"Godot.NET.Sdk/4.7.0\" />");
    await BackendInstaller.InstallAsync(b, "local", path: zip);
    Assert(File.ReadAllText(Path.Combine(b, "project.godot")).Contains("project/assembly_name=\"GameB\""),
        "Installer adds assembly_name for an existing C# project");
    Assert(File.Exists(Path.Combine(b, "addons", "godot_mcp", "GodotRuntimeTransport.cs")),
        "C# backend installs from release-format ZIP");
    Assert(File.Exists(Path.Combine(b, "addons", "godot_mcp", "tool_manifest.json")),
        "Installer packages the shared tool manifest with the Godot backend");
    Assert(RuntimeRegistry.Projects().Count == 3, "Multiple projects tracked while offline");
    var parallelProjects = Enumerable.Range(1, 8).Select(i =>
    {
        var folder = Path.Combine(scratch, "parallel-" + i);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "project.godot"), "[application]\n");
        return folder;
    }).ToArray();
    await Task.WhenAll(parallelProjects.Select(p => Task.Run(() => RuntimeRegistry.Remember(p))));
    Assert(RuntimeRegistry.Projects().Count == 11,
        "Concurrent C# registrations preserve all project records");
    Directory.CreateDirectory(RuntimeRegistry.Root);
    var fakeName = Path.Combine(RuntimeRegistry.Root,
        "runtime-9999999-" + new string('a', 32) + ".json");
    await File.WriteAllTextAsync(fakeName, JsonSerializer.Serialize(new {
        protocol = 1, instanceId = new string('a', 32), pid = 9999999,
        port = 18777, projectPath = b, token = new string('b', 64),
        startedAt = DateTime.UtcNow, heartbeat = DateTime.UtcNow
    }));
    RuntimeRegistry.SecureFile(fakeName);
    Assert((await RuntimeRegistry.LiveAsync()).Count == 0,
        "Stale or spoofed registry records do not appear as online game processes");
    var v = await mcp.DispatchAsync("""{"jsonrpc":"2.0","id":8,"method":"tools/call","params":{"name":"godot_status"}}""");
    Assert(v?["result"]?["isError"]?.GetValue<bool>() == false,
        "MCP management tools work without active backend");

    var c = Path.Combine(scratch, "game-c");
    Directory.CreateDirectory(c);
    await File.WriteAllTextAsync(Path.Combine(c, "project.godot"),
        "[application]\nconfig/name=\"Game C\"\n");
    var mcpInstallerCall = JsonSerializer.Serialize(new
    {
        jsonrpc = "2.0", id = 20, method = "tools/call",
        @params = new
        {
            name = "godot_install_backend",
            arguments = new { project = c, source = "local", path = root }
        }
    });
    var mcpInstalled = await mcp.DispatchAsync(mcpInstallerCall);
    Assert(mcpInstalled?["result"]?["isError"]?.GetValue<bool>() == false &&
           File.Exists(Path.Combine(c, "addons", "godot_mcp", "GodotRuntimeTransport.cs")),
        "MCP godot_install_backend installs real Godot C# backend while Godot is absent");

    await File.AppendAllTextAsync(Path.Combine(a, "addons", "godot_mcp", "README.md"), "\ncustom\n");
    var conflict = false;
    try { await BackendInstaller.InstallAsync(a, "local", path: source); }
    catch (InvalidOperationException) { conflict = true; }
    Assert(conflict, "Installer refuses to overwrite modified backend without --force");
    var forced = await BackendInstaller.InstallAsync(a, "local", path: source, force: true);
    var backup = JsonSerializer.SerializeToElement(forced).GetProperty("backup").GetString()!;
    Assert(Directory.Exists(backup) && !backup.StartsWith(a),
        "Forced replacement backs up previous addon outside game compilation tree");

    var evil = Path.Combine(scratch, "evil.zip");
    using (var file = File.Create(evil))
    using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
    {
        using var write = new StreamWriter(archive.CreateEntry("addons/godot_mcp/../../evil.cs").Open());
        await write.WriteAsync("bad");
    }
    var protectedPath = Path.Combine(scratch, "guarded");
    Directory.CreateDirectory(protectedPath);
    await File.WriteAllTextAsync(Path.Combine(protectedPath, "project.godot"), "[application]\n");
    var rejected = false;
    try { await BackendInstaller.InstallAsync(protectedPath, "local", path: evil); }
    catch (InvalidOperationException) { rejected = true; }
    Assert(rejected && !File.Exists(Path.Combine(protectedPath, "evil.cs")),
        "Unsafe ZIP traversal is rejected");

    var canary = await mcp.DispatchAsync("""{"jsonrpc":"2.0","id":9,"method":"tools/call","params":{"name":"godot_setup_guide"}}""");
    Assert(canary?["result"]?["isError"]?.GetValue<bool>() == false,
        "MCP setup guide always available");

    Console.WriteLine($"\nALL {passed} ASSERTIONS PASSED");
    return 0;
}
finally
{
    try { Directory.Delete(scratch, true); }
    catch (IOException) { }
}
