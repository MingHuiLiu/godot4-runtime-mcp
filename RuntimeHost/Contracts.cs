using System.Text.Json;
using System.Text.Json.Serialization;

namespace GodotRuntimeMcp;

// Wire contracts are shared between the CLI, MCP adapter and local Godot runtime.
// JSON is restricted to tool-specific arguments and returned Godot Variant data.
public sealed record RuntimeInstanceInfo(
    string ProjectPath, string InstanceId, int Pid, int Port, string StartedAt, string Heartbeat);
public sealed record ProjectInfo(string Path, bool BackendInstalled);
public sealed record RuntimeStatus(IReadOnlyList<ProjectInfo> Projects, IReadOnlyList<RuntimeInstanceInfo> Instances);
public sealed record SetupGuide(
    string Offline, string Install, string LocalInstall, string Build,
    string Runtime, string Mcp, string Cli, string Releases);
public sealed record InstallResult(
    string Status, string Project, string Origin, int Files, string? Backup = null, string? Next = null);
public sealed record PackResult(string Archive, int Files, string Release);
public sealed record BackendCallRequest(string Tool, JsonElement Arguments);
public sealed record BackendCallResult(bool Success, JsonElement Data, string? Error);
public sealed record BackendHealth(int Protocol, string Status, string InstanceId, int Pid, string ProjectPath, int Port);
public sealed record GithubRelease(
    [property: JsonPropertyName("tag_name")] string TagName,
    IReadOnlyList<GithubReleaseAsset> Assets);
public sealed record GithubReleaseAsset(
    string Name, [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);

// Typed tool schemas are parsed once from the canonical Godot addon manifest.
public sealed record ToolDefinition(string Name, string Description, ToolInputSchema InputSchema);
public sealed record ToolInputSchema(string Type, Dictionary<string, ToolParameterDefinition> Properties,
    List<string>? Required = null);
public sealed record ToolParameterDefinition(string? Type = null, string? Description = null,
    IReadOnlyList<string>? Enum = null);

public sealed record SnapshotNodePage(int Total, int Offset, int Limit,
    List<GodotRuntimeMcp.Recording.RecordedNode> Items);

public sealed record SceneExportJobStatus(string JobId, string Status,
    string? ScenePath, string? ReportPath, string? Code, string? Message,
    int? Pid = null);

/// <summary>Persistent isolated input-replay job state (matched is observational).</summary>
public sealed record ReplayJobStatus(string JobId, string Status, string SessionId,
    string? ReportPath, string? Code, string? Message, int? Pid = null);
