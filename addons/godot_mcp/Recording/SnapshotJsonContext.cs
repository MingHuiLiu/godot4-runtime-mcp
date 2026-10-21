using System.Text.Json.Serialization;

namespace GodotRuntimeMcp.Recording;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SceneCheckpoint))]
[JsonSerializable(typeof(RecordedNode))]
[JsonSerializable(typeof(RecordedResource))]
[JsonSerializable(typeof(SnapshotValue))]
[JsonSerializable(typeof(SnapshotIssue))]
[JsonSerializable(typeof(List<SnapshotIssue>))]
[JsonSerializable(typeof(CheckpointManifest))]
[JsonSerializable(typeof(List<CheckpointManifest>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(SnapshotJobInfo))]
[JsonSerializable(typeof(SnapshotExportRequest))]
[JsonSerializable(typeof(SceneExportDependencies))]
[JsonSerializable(typeof(SceneExportResult))]
[JsonSerializable(typeof(TimelineBatch))]
[JsonSerializable(typeof(TimelineFrameImage))]
[JsonSerializable(typeof(ObservedRuntimeEvent))]
[JsonSerializable(typeof(List<ObservedRuntimeEvent>))]
[JsonSerializable(typeof(TimelineEvent))]
[JsonSerializable(typeof(TimelineSessionStatus))]
[JsonSerializable(typeof(TimelineEventPage))]
[JsonSerializable(typeof(List<TimelineSessionStatus>))]
[JsonSerializable(typeof(List<TimelineBatch>))]
[JsonSerializable(typeof(Dictionary<string, SnapshotValue>))]
public partial class SnapshotJsonContext : JsonSerializerContext { }
