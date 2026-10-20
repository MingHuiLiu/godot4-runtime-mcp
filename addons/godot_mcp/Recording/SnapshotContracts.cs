using System.Text.Json.Nodes;

namespace GodotRuntimeMcp.Recording;

public sealed record SnapshotValue(string Type, JsonNode? Data = null, string? RefId = null);
public sealed record SnapshotIssue(string Code, string? NodeId, string? Property, string Message);
public sealed record RecordedNode(
    string NodeId, string? ParentId, int SiblingIndex, string Name, string NativeClass,
    string? ScriptPath, string? SceneFilePath, string OriginalPath, string? OriginalOwnerId,
    bool IsInternal, bool UniqueNameInOwner, List<string> Groups,
    Dictionary<string, SnapshotValue> Properties,
    Dictionary<string, SnapshotValue> DiagnosticProperties, List<SnapshotIssue> Issues);
public sealed record RecordedResource(
    string ResourceId, string ClassName, string? ResourcePath, string? Sha256,
    bool LocalToScene, Dictionary<string, SnapshotValue> Properties, List<SnapshotIssue> Issues);
public sealed record SceneCheckpoint(
    int SchemaVersion, string SnapshotId, string SessionId, string ProjectId,
    string InstanceId, string RootNodeId, long StartedPhysicsFrame, long EndedPhysicsFrame,
    long StartedProcessFrame, long EndedProcessFrame,
    List<RecordedNode> Nodes, List<RecordedResource> Resources,
    Dictionary<string, SnapshotValue> RuntimeState, List<SnapshotIssue> Issues);
public sealed record CheckpointManifest(
    int SchemaVersion, string SnapshotId, string SessionId, string ProjectId, string InstanceId,
    string Status, string Quality, int NodeCount, int ResourceCount, DateTimeOffset CreatedUtc,
    Dictionary<string, string> FileHashes);
public sealed record SnapshotJobInfo(
    string JobId, string Status, string? SnapshotId = null,
    string? Code = null, string? Message = null);

public static class SnapshotSchema
{
    public const int CurrentVersion = 1;
    public static void Validate(SceneCheckpoint snapshot)
    {
        if (snapshot.SchemaVersion != CurrentVersion)
            throw new NotSupportedException($"unsupported_schema_version: {snapshot.SchemaVersion}");
    }
}
