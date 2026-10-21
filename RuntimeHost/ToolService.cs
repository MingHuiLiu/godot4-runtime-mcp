using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GodotRuntimeMcp;

/// <summary>One implementation of every tool for all transports (CLI/MCP stdio/HTTP).</summary>
public sealed class ToolService
{
    private static readonly JsonSerializerOptions Pretty = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string? _project;
    private readonly string? _instance;
    private readonly IReadOnlyList<ToolDefinition> _tools;
    private readonly HashSet<string> _runtimeNames;

    public ToolService(string? project = null, string? instance = null)
    {
        _project = project;
        _instance = instance;
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceNames()
            .Where(n => n.EndsWith("tool_manifest.json", StringComparison.Ordinal))
            .Select(n => Assembly.GetExecutingAssembly().GetManifestResourceStream(n))
            .FirstOrDefault() ?? throw new InvalidOperationException("Embedded Godot tool manifest missing");
        var runtimeTools = JsonSerializer.Deserialize(stream, RuntimeJsonContext.Default.ListToolDefinition)
            ?? throw new InvalidOperationException("Invalid embedded Godot tool manifest");
        _runtimeNames = runtimeTools.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var tool in runtimeTools)
        {
            tool.InputSchema.Properties.Add("project", new ToolParameterDefinition(
                "string", "Absolute Godot project path (selects a specific project)"));
            tool.InputSchema.Properties.Add("instance", new ToolParameterDefinition(
                "string", "Running game instance ID or unique prefix"));
        }

        static ToolDefinition Management(string name, string description) =>
            new(name, description, new ToolInputSchema("object", new Dictionary<string, ToolParameterDefinition>()));
        runtimeTools.Add(Management("godot_status", "List registered projects and verified live game processes"));
        runtimeTools.Add(Management("godot_list_instances", "List verified running game instances"));
        runtimeTools.Add(Management("godot_list_projects", "List registered projects without running Godot"));
        runtimeTools.Add(Management("godot_setup_guide", "Offline installation and MCP/CLI usage guide"));
        runtimeTools.Add(new ToolDefinition("godot_install_backend", "Install or upgrade Godot C# backend",
            new ToolInputSchema("object", new Dictionary<string, ToolParameterDefinition>
            {
                ["project"] = new("string", "Target Godot project path"),
                ["source"] = new("string", "Release-first or local package source",
                    ["auto", "release", "github", "local"]),
                ["version"] = new("string", "Release tag (latest by default)"),
                ["path"] = new("string", "Local ZIP/directory when source=local"),
                ["force"] = new("boolean", "Back up existing different addon")
            }, ["project"])));
        runtimeTools.Add(new ToolDefinition("godot_snapshot_list", "List completed checkpoints offline",
            new ToolInputSchema("object", new Dictionary<string, ToolParameterDefinition>
            { ["project"] = new("string", "Absolute Godot project path"),
              ["limit"] = new("integer", "Maximum results 1..100") }, ["project"])));
        runtimeTools.Add(new ToolDefinition("godot_snapshot_info", "Validate and inspect a saved checkpoint",
            new ToolInputSchema("object", new Dictionary<string, ToolParameterDefinition>
            { ["project"] = new("string", "Absolute Godot project path"),
              ["snapshotId"] = new("string", "Saved snapshot ID") }, ["project", "snapshotId"])));
        runtimeTools.Add(new ToolDefinition("godot_snapshot_nodes", "Page and filter checkpoint nodes offline",
            new ToolInputSchema("object", new Dictionary<string, ToolParameterDefinition>
            { ["project"] = new("string", "Absolute Godot project path"),
              ["snapshotId"] = new("string", "Saved snapshot ID"),
              ["nodeId"] = new("string", "Exact node identity"),
              ["pathPrefix"] = new("string", "Original path prefix"),
              ["className"] = new("string", "Native node class"),
              ["offset"] = new("integer", "Start index"),
              ["limit"] = new("integer", "Page size 1..100") }, ["project", "snapshotId"])));
        runtimeTools.Add(new ToolDefinition("godot_snapshot_export_tscn",
            "Export a verified .tscn from a saved checkpoint using isolated Godot Mono",
            new ToolInputSchema("object", new Dictionary<string, ToolParameterDefinition>
            { ["project"] = new("string", "Absolute Godot project path"),
              ["snapshotId"] = new("string", "Saved snapshot ID"),
              ["strict"] = new("boolean", "Require lossless supported properties (default true)"),
              ["godotBinary"] = new("string", "Optional Godot Mono executable") },
              ["project", "snapshotId"])));
        runtimeTools.Add(new ToolDefinition("godot_snapshot_export_status",
            "Check asynchronous headless export job and resulting scene file",
            new ToolInputSchema("object", new Dictionary<string, ToolParameterDefinition>
            { ["project"] = new("string", "Absolute Godot project path"),
              ["jobId"] = new("string", "Export job ID") },
              ["project", "jobId"])));
        runtimeTools.Add(new ToolDefinition("godot_timeline_sessions",
            "List on-disk observed timeline sessions, even when the game has exited",
            new ToolInputSchema("object", new Dictionary<string, ToolParameterDefinition>
            { ["project"] = new("string", "Absolute Godot project path"),
              ["limit"] = new("integer", "Maximum sessions 1..100") }, ["project"])));
        runtimeTools.Add(new ToolDefinition("godot_timeline_info",
            "Read a persisted timeline's sampling and completion metadata",
            new ToolInputSchema("object", new Dictionary<string, ToolParameterDefinition>
            { ["project"] = new("string", "Absolute Godot project path"),
              ["sessionId"] = new("string", "Recording session identifier") },
              ["project", "sessionId"])));
        runtimeTools.Add(new ToolDefinition("godot_timeline_events",
            "Page checked observed time span events from a completed or ongoing timeline",
            new ToolInputSchema("object", new Dictionary<string, ToolParameterDefinition>
            { ["project"] = new("string", "Absolute Godot project path"),
              ["sessionId"] = new("string", "Recording session identifier"),
              ["offset"] = new("integer", "Start sample index"),
              ["limit"] = new("integer", "Number of samples (1..100)") },
              ["project", "sessionId"])));
        _tools = runtimeTools;
    }

    public IReadOnlyList<ToolDefinition> CatalogModels => _tools;

    // JSON is emitted only at the CLI compatibility boundary; MCP uses official
    // protocol Tool objects and direct strongly typed ToolDefinition metadata.
    public JsonArray Catalog()
    {
        var array = new JsonArray();
        foreach (var tool in _tools)
            array.Add((JsonNode?)JsonSerializer.SerializeToNode(
                tool, RuntimeJsonContext.Default.ToolDefinition));
        return array;
    }

    private static string? Str(JsonObject args, string key) =>
        args[key] is JsonValue v && v.TryGetValue<string>(out var str) ? str : null;

    private static bool Bool(JsonObject args, string key) =>
        args[key] is JsonValue v && v.TryGetValue<bool>(out var val) && val;

    public async Task<JsonNode?> RunAsync(string name, JsonObject args)
    {
        if (name == "godot_status")
        {
            var status = new RuntimeStatus(
                RuntimeRegistry.Projects().Select(path => new ProjectInfo(path,
                    File.Exists(Path.Combine(path, "addons", "godot_mcp", "GodotRuntimeTransport.cs")))).ToList(),
                (await RuntimeRegistry.LiveAsync()).Select(entry => entry.Public).ToList());
            return JsonSerializer.SerializeToNode(status, RuntimeJsonContext.Default.RuntimeStatus);
        }
        if (name == "godot_list_instances")
            return JsonSerializer.SerializeToNode(
                (await RuntimeRegistry.LiveAsync()).Select(entry => entry.Public).ToList(),
                RuntimeJsonContext.Default.ListRuntimeInstanceInfo);
        if (name == "godot_list_projects")
            return JsonSerializer.SerializeToNode(RuntimeRegistry.Projects().ToList(),
                RuntimeJsonContext.Default.ListString);
        if (name == "godot_setup_guide")
            return JsonSerializer.SerializeToNode(new SetupGuide(
                "CLI/MCP tool discovery and installation work without Godot",
                "godot-runtime-cli install --project /path/to/game",
                "godot-runtime-cli install --project /path/to/game --source local --path .",
                "Build the Godot 4 .NET C# project and launch it",
                "Each game process uses a unique loopback endpoint and instance ID",
                "godot-mcp-server (or godot-runtime-cli mcp)",
                "godot-runtime-cli get-scene-tree-simple --project /path/to/game --instance ID",
                "https://github.com/MingHuiLiu/godot4-runtime-mcp/releases"),
                RuntimeJsonContext.Default.SetupGuide);
        if (name == "godot_install_backend")
        {
            var path = Str(args, "project") ?? _project ??
                throw new ArgumentException("godot_install_backend requires project");
            return JsonSerializer.SerializeToNode(await BackendInstaller.InstallAsync(path,
                Str(args, "source") ?? "auto", Str(args, "version") ?? "latest",
                Str(args, "path"), Bool(args, "force")), RuntimeJsonContext.Default.InstallResult);
        }
        if (name is "godot_snapshot_list" or "godot_snapshot_info" or "godot_snapshot_nodes")
        {
            string targetProject = Str(args, "project") ?? _project ??
                throw new ArgumentException(name + " requires project");
            var store = new CheckpointStore();
            int Number(string key, int fallback)
            {
                if (args[key] is not JsonValue value) return fallback;
                if (value.TryGetValue<int>(out var i)) return i;
                if (value.TryGetValue<long>(out var l) && l is >= int.MinValue and <= int.MaxValue)
                    return (int)l;
                throw new ArgumentException(key + " must be an integer");
            }
            if (name == "godot_snapshot_list")
                return JsonSerializer.SerializeToNode(store.List(targetProject, Number("limit", 50)).ToList(),
                    Recording.SnapshotJsonContext.Default.ListCheckpointManifest);
            var snapshotId = Str(args, "snapshotId") ??
                throw new ArgumentException(name + " requires snapshotId");
            if (name == "godot_snapshot_info")
                return JsonSerializer.SerializeToNode(store.Info(targetProject, snapshotId),
                    Recording.SnapshotJsonContext.Default.CheckpointManifest);
            return JsonSerializer.SerializeToNode(store.QueryNodes(targetProject, snapshotId,
                Str(args, "nodeId"), Str(args, "pathPrefix"), Str(args, "className"),
                Number("offset", 0), Number("limit", 50)), RuntimeJsonContext.Default.SnapshotNodePage);
        }
        if (name is "godot_snapshot_export_tscn" or "godot_snapshot_export_status")
        {
            string targetProject = Str(args, "project") ?? _project ??
                throw new ArgumentException(name + " requires project");
            var jobs = new ExportJobManager();
            if (name == "godot_snapshot_export_tscn")
            {
                var snap = Str(args, "snapshotId") ??
                    throw new ArgumentException("snapshotId is required");
                bool strict = args["strict"] is not JsonValue val ||
                    !val.TryGetValue<bool>(out bool strictValue) || strictValue;
                return JsonSerializer.SerializeToNode(jobs.Start(targetProject, snap, strict,
                    Str(args, "godotBinary")), Recording.SnapshotJsonContext.Default.SnapshotJobInfo);
            }
            string jobId = Str(args, "jobId") ?? throw new ArgumentException("jobId required");
            return JsonSerializer.SerializeToNode(jobs.Status(targetProject, jobId),
                RuntimeJsonContext.Default.SceneExportJobStatus);
        }
        if (name is "godot_timeline_sessions" or "godot_timeline_info" or "godot_timeline_events")
        {
            string targetProject = Str(args, "project") ?? _project ??
                throw new ArgumentException(name + " requires project");
            var store = new TimelineStore();
            int Number(string key, int fallback)
            {
                if (args[key] is not JsonValue value) return fallback;
                if (value.TryGetValue<int>(out int i)) return i;
                if (value.TryGetValue<long>(out long l) && l is >= int.MinValue and <= int.MaxValue)
                    return (int)l;
                throw new ArgumentException(key + " must be an integer");
            }
            if (name == "godot_timeline_sessions")
                return JsonSerializer.SerializeToNode(
                    store.List(targetProject, Number("limit", 50)).ToList(),
                    Recording.SnapshotJsonContext.Default.ListTimelineSessionStatus);
            var session = Str(args, "sessionId") ??
                throw new ArgumentException(name + " requires sessionId");
            if (name == "godot_timeline_info")
                return JsonSerializer.SerializeToNode(store.Info(targetProject, session),
                    Recording.SnapshotJsonContext.Default.TimelineSessionStatus);
            return JsonSerializer.SerializeToNode(store.Events(targetProject, session,
                Number("offset", 0), Number("limit", 50)),
                Recording.SnapshotJsonContext.Default.TimelineEventPage);
        }
        if (!_runtimeNames.Contains(name))
            throw new ArgumentException("Unknown MCP tool: " + name);

        var project = Str(args, "project") ?? _project;
        var instance = Str(args, "instance") ?? _instance;
        var operationArgs = (JsonObject)args.DeepClone();
        operationArgs.Remove("project");
        operationArgs.Remove("instance");
        using var serialized = JsonDocument.Parse(operationArgs.ToJsonString());
        var result = await RuntimeRegistry.CallAsync(name, serialized.RootElement.Clone(), project, instance);
        return JsonNode.Parse(result.GetRawText());
    }

    public bool Exists(string name) => _tools.Any(t => t.Name == name);
    public static string Format(JsonNode? value) => value?.ToJsonString(Pretty) ?? "null";
}
