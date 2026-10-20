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
