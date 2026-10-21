using System.IO.Compression;
using System.Diagnostics;
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
    SnapshotContractTests.Run(Assert);
    TimelineDeltaTests.Run(Assert);
    TimelineToolTests.Run(Assert);
    await TimelineArchiveTests.RunAsync(Assert, scratch);
    await TimelineFrameTests.RunAsync(Assert, scratch);
    await TimelineGalleryTests.RunAsync(Assert, scratch);
    await ObservedEventTests.RunAsync(Assert, scratch);
    await TimelineStoreTests.RunAsync(Assert, scratch);
    await CheckpointArchiveTests.RunAsync(Assert, scratch);
    SnapshotToolTests.Run(Assert);
    await CheckpointStoreTests.RunAsync(Assert, scratch);
    await ExportJobManagerTests.RunAsync(Assert, scratch);
    var tools = new ToolService();
    using var canonicalManifest = JsonDocument.Parse(File.ReadAllText(
        Path.Combine(root, "addons", "godot_mcp", "tool_manifest.json")));
    var runtimeNames = canonicalManifest.RootElement.EnumerateArray()
        .Select(item => item.GetProperty("name").GetString()!).ToArray();
    var managementNames = new[] { "godot_status", "godot_list_instances", "godot_list_projects",
        "godot_setup_guide", "godot_install_backend", "godot_snapshot_list",
        "godot_snapshot_info", "godot_snapshot_nodes", "godot_snapshot_export_tscn",
        "godot_snapshot_export_status", "godot_timeline_sessions",
        "godot_timeline_info", "godot_timeline_events",
        "godot_timeline_export_gallery" };
    int expectedTools = runtimeNames.Length + managementNames.Length;
    Assert(tools.Catalog().Count == expectedTools &&
        runtimeNames.All(tools.Exists) && managementNames.All(tools.Exists) &&
        tools.CatalogModels.Select(t => t.Name).Distinct().Count() == expectedTools,
        "Every canonical runtime and offline management tool is registered exactly once");
    Assert(tools.CatalogModels.Count == expectedTools &&
        tools.CatalogModels.All(t => t.InputSchema.Type == "object"),
        "Canonical tool catalog deserializes into strongly typed ToolDefinition records");
    var github = JsonSerializer.Deserialize("""
        {"tag_name":"v3.1.0","assets":[{"name":"godot-runtime-addon.zip","browser_download_url":"https://example.invalid/addon.zip"}]}
        """, RuntimeJsonContext.Default.GithubRelease);
    Assert(github?.TagName == "v3.1.0" && github.Assets[0].Name == "godot-runtime-addon.zip",
        "GitHub Release metadata parses into strongly typed models");
    using var callArgs = JsonDocument.Parse("""{"maxDepth":2}""");
    var wire = JsonSerializer.Serialize(new BackendCallRequest("get_scene_tree_simple", callArgs.RootElement),
        RuntimeJsonContext.Default.BackendCallRequest);
    Assert(wire.Contains("\"tool\":\"get_scene_tree_simple\"") && wire.Contains("\"arguments\""),
        "Typed loopback HTTP request contract preserves Godot arguments");
    // Every MCP tool has the same direct, schema-driven CLI entrypoint.
    Assert(tools.Catalog().All(t =>
        ToolCommandLine.Resolve(tools, t!["name"]!.ToString().Replace('_', '-')) ==
            t["name"]!.ToString()),
        "All MCP tools have direct kebab-case CLI commands");
    var schemaFailures = new List<string>();
    foreach (var tool in tools.Catalog().OfType<JsonObject>())
    {
        var name = tool["name"]!.ToString();
        var props = tool["inputSchema"]?["properties"]?.AsObject() ?? new JsonObject();
        var flags = new List<string>();
        foreach (var (field, schema) in props)
        {
            var type = schema?["type"]?.ToString();
            flags.Add("--" + field.Replace("_", "-").SelectMany(c =>
                char.IsUpper(c) ? new[] { '-', char.ToLowerInvariant(c) } : new[] { c })
                .Aggregate("", (acc, c) => acc + c));
            flags.Add(type switch
            {
                "boolean" => "true",
                "integer" => "2",
                "number" => "1.5",
                "array" => "[]",
                "object" => "{}",
                _ => "sample"
            });
        }
        try
        {
            var parsed = ToolCommandLine.Parse(tools, name.Replace('_', '-'), flags);
            if (parsed.Count != props.Count) schemaFailures.Add(name + ": incomplete flags");
        }
        catch (ArgumentException ex) { schemaFailures.Add(name + ": " + ex.Message); }
    }
    Assert(schemaFailures.Count == 0,
        "All CLI commands accept typed flags for every manifest property: " +
            string.Join("; ", schemaFailures));
    var cliHelp = ToolCommandLine.Describe(ToolCommandLine.Find(tools, "get-node-info")!);
    Assert(cliHelp.Contains("--node-path") && cliHelp.Contains("(required)") &&
        cliHelp.Contains("godot-runtime-cli get-node-info"),
        "Per-tool CLI help displays required flags and examples");
    var cliScene = ToolCommandLine.Parse(tools, "get-scene-tree-simple",
        ["--max-depth", "2", "--root-path=/root", "--project", "/tmp/Game"]);
    Assert(cliScene["maxDepth"]!.GetValue<long>() == 2 &&
        cliScene["rootPath"]!.ToString() == "/root" &&
        cliScene["project"]!.ToString() == "/tmp/Game",
        "Typed integer, kebab-case flags and project selection");
    var cliValue = ToolCommandLine.Parse(tools, "set-property",
        ["--node-path", "/root/Main", "--property-name", "visible", "--value", "false"]);
    Assert(cliValue["value"]!.GetValue<bool>() == false,
        "Untyped Godot property values parse boolean JSON scalars");
    var cliArray = ToolCommandLine.Parse(tools, "call-method",
        ["--node-path", "/root/Main", "--method-name", "Test", "--args", "[1,true]"]);
    Assert(cliArray["args"] is JsonArray array && array.Count == 2,
        "Tool array-valued --args flag is distinct from legacy JSON payload");
    var cliMerge = ToolCommandLine.Parse(tools, "get-node-info",
        ["--json", "{\"nodePath\":\"/root/Old\"}", "--node-path", "/root/New"]);
    Assert(cliMerge["nodePath"]!.ToString() == "/root/New",
        "Named flags override optional JSON object payload");
    var cliBoolean = ToolCommandLine.Parse(tools, "get-scene-tree",
        ["--no-include-properties"]);
    Assert(cliBoolean["includeProperties"]!.GetValue<bool>() == false,
        "Negative boolean CLI flags are supported");
    var cliValidationErrors = 0;
    foreach (var invalid in new[]
    {
        new[] { "get-node-info" },
        new[] { "get-node-info", "--node-path" },
        new[] { "get-node-info", "--node-pat", "/root/Main" },
        new[] { "get-scene-tree-simple", "--max-depth", "banana" },
        new[] { "call-method", "--node-path", "/root/Main", "--method-name", "Test", "--args", "{}" }
    })
    {
        try { ToolCommandLine.Parse(tools, invalid[0], invalid.Skip(1).ToArray()); }
        catch (ArgumentException) { cliValidationErrors++; }
    }
    Assert(cliValidationErrors == 5,
        "Direct CLI catches required flags, missing values, typos and type mismatches");
    Assert(ToolCommandLine.CatalogText(tools, "node").Contains("get-node-info"),
        "CLI tool catalog supports searchable human-readable commands");
    var standaloneSkill = File.ReadAllText(Path.Combine(root, "skills", "godot-runtime", "SKILL.md"));
    Assert(standaloneSkill.Contains("api.github.com/repos/") &&
        standaloneSkill.Contains("godot-runtime-tools-$TAG-$RID.tar.gz") &&
        standaloneSkill.Contains("shasum -a 256 -c") &&
        standaloneSkill.Contains("godot-runtime-cli"),
        "Single standalone Skill describes GitHub CLI bootstrap and checksum verification");
    var backendImplementation = File.ReadAllText(Path.Combine(root, "addons", "godot_mcp", "GodotRuntimeBackend.cs"));
    var missingDispatches = runtimeNames
        .Where(name => !backendImplementation.Contains("\"" + name + "\" =>", StringComparison.Ordinal))
        .ToArray();
    Assert(missingDispatches.Length == 0,
        "Canonical tool manifest matches all Godot C# runtime operation dispatches");
    var setSchema = tools.Catalog().First(t => t?["name"]?.ToString() == "set_property")!["inputSchema"]!["properties"]!["value"];
    Assert(setSchema?["type"] == null, "set_property schema accepts scalar and structured JSON values");
    var status = await tools.RunAsync("godot_status", new JsonObject());
    Assert(JsonSerializer.Serialize(status).Contains("\"instances\":[]"),
        "Offline status without Godot");
    // Protocol version negotiation, notifications and JSON-RPC framing are handled by
    // the official MCP SDK, tested below via its real stdio client.
    Assert(tools.Exists("godot_status") && tools.Exists("get_scene_tree_simple"),
        "MCP SDK receives a consistent catalog of runtime and management tools");
    var offlineError = false;
    try { await tools.RunAsync("get_time", new JsonObject()); }
    catch (InvalidOperationException) { offlineError = true; }
    Assert(offlineError, "Runtime tools report offline rather than fabricating results");
    var a = Path.Combine(scratch, "game-a");
    Directory.CreateDirectory(a);
    await File.WriteAllTextAsync(Path.Combine(a, "project.godot"), "[application]\nconfig/name=\"Game A\"\n");
    var source = root;
    var installed = await BackendInstaller.InstallAsync(a, "local", path: source);
    Assert(installed.Status == "installed",
        "Install from local C# backend source into project with no running Godot");
    Assert(File.Exists(Path.Combine(a, "addons", "godot_mcp", "Recording",
            "SnapshotSceneBuilder.cs")) &&
        File.Exists(Path.Combine(a, "addons", "godot_mcp", "Recording", "export_worker.tscn")),
        "Installer copies nested recording addon files");
    var settings = await File.ReadAllTextAsync(Path.Combine(a, "project.godot"));
    Assert(settings.Contains("GodotRuntimeBackend=\"*res://addons/godot_mcp/runtime_backend.tscn\""),
        "Installer registers Godot autoload");
    var again = await BackendInstaller.InstallAsync(a, "local", path: source);
    Assert(again.Status == "already-installed",
        "Installation is idempotent");
    Assert((await File.ReadAllTextAsync(Path.Combine(a, "project.godot")))
        .Split("GodotRuntimeBackend=", StringSplitOptions.None).Length == 2,
        "Autoload registration is not duplicated");

    // The example is a standalone Godot project with a checked-in addon copy.
    // Guard against drifting from the canonical backend used by releases.
    var syncMirror = Path.Combine(scratch, "addon-sync-probe");
    using (var sync = Process.Start(new ProcessStartInfo("sh")
    {
        ArgumentList = { Path.Combine(root, "scripts", "sync-example-addon.sh"), syncMirror },
        UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
    })!)
    {
        await sync.WaitForExitAsync();
        Assert(sync.ExitCode == 0 && File.Exists(Path.Combine(syncMirror, "Recording",
            "SnapshotSceneBuilder.cs")) && File.Exists(Path.Combine(syncMirror, "Recording",
            "export_worker.tscn")),
            "Canonical addon sync copies nested recording files recursively to alternate destination");
    }
    var canonical = Path.Combine(root, "addons", "godot_mcp");
    var bundledExample = Path.Combine(root, "ExampleProject", "addons", "godot_mcp");
    static string[] RelativeAddonFiles(string directory) =>
        Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(".uid", StringComparison.Ordinal) &&
                           !path.EndsWith(".import", StringComparison.Ordinal) &&
                           !path.EndsWith(".DS_Store", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(directory, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal).ToArray();
    var canonicalFiles = RelativeAddonFiles(canonical);
    var exampleFiles = RelativeAddonFiles(bundledExample);
    Assert(canonicalFiles.SequenceEqual(exampleFiles) &&
        canonicalFiles.All(name => File.ReadAllBytes(Path.Combine(canonical, name))
            .AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(bundledExample, name)))),
        "Recursive Godot example addon matches every canonical release file");

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
    using (var package = ZipFile.OpenRead(zip))
        Assert(package.GetEntry("addons/godot_mcp/Recording/SnapshotSceneBuilder.cs") is not null &&
            package.GetEntry("addons/godot_mcp/Recording/export_worker.tscn") is not null &&
            package.GetEntry("addons/godot_mcp/Recording/RuntimeTimelineController.cs") is not null &&
            package.GetEntry("addons/godot_mcp/Recording/TimelineArchive.cs") is not null &&
            package.GetEntry("addons/godot_mcp/Recording/TimelineDeltaBuilder.cs") is not null,
            "Release ZIP retains nested runtime recording files");
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
    var managementStatus = await tools.RunAsync("godot_status", new JsonObject());
    Assert(managementStatus?["projects"]?.AsArray().Count > 0,
        "MCP management tools work without active backend");
    var mcpProject = Path.Combine(scratch, "mcp-installer");
    Directory.CreateDirectory(mcpProject);
    await File.WriteAllTextAsync(Path.Combine(mcpProject, "project.godot"), "[application]\n");
    var mcpInstalled = await tools.RunAsync("godot_install_backend", new JsonObject
    {
        ["project"] = mcpProject, ["source"] = "local", ["path"] = root
    });
    Assert(mcpInstalled?["status"]?.ToString() == "installed" &&
        File.Exists(Path.Combine(mcpProject, "addons", "godot_mcp", "GodotRuntimeBackend.cs")),
        "MCP godot_install_backend installs real Godot C# backend while Godot is absent");
    File.AppendAllText(Path.Combine(a, "addons", "godot_mcp", "README.md"),
        "\nUser-customized addon documentation\n");
    var conflict = false;
    try { await BackendInstaller.InstallAsync(a, "local", path: source); }
    catch (InvalidOperationException) { conflict = true; }
    Assert(conflict, "Installer refuses to overwrite modified backend without --force");
    var forced = await BackendInstaller.InstallAsync(a, "local", path: source, force: true);
    var backup = forced.Backup!;
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

    var canary = await tools.RunAsync("godot_setup_guide", new JsonObject());
    Assert(canary?["install"] != null,
        "MCP setup guide always available");

    var homeDotnet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".dotnet", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
    await GodotRuntimeMcp.Tests.McpSdkSmoke.RunAsync(
        typeof(ToolService).Assembly.Location,
        Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ??
        (File.Exists(homeDotnet) ? homeDotnet : "dotnet"), expectedTools);
    Assert(true, "Official MCP SDK client discovers all runtime and offline tools over stdio");
    await GodotRuntimeMcp.Tests.McpSdkSmoke.RunHttpAsync(
        typeof(ToolService).Assembly.Location,
        Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ??
        (File.Exists(homeDotnet) ? homeDotnet : "dotnet"), expectedTools);
    Assert(true, "Official MCP SDK HTTP client discovers all tools");

    Console.WriteLine($"\nALL {passed} ASSERTIONS PASSED");
    return 0;
}
finally
{
    try { Directory.Delete(scratch, true); }
    catch (IOException) { }
}
