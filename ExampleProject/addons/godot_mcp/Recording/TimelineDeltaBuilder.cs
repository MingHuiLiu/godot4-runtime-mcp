using System.Text.Json.Nodes;

namespace GodotRuntimeMcp.Recording;

/// <summary>A change observed between two checkpoints, not necessarily the instant of change.</summary>
public sealed record TimelineEvent(
    string Kind, string NodeId, string? Property = null,
    SnapshotValue? OldValue = null, SnapshotValue? NewValue = null,
    string? OldText = null, string? NewText = null);

public sealed record TimelineBatch(
    int SchemaVersion, string SessionId, int SampleIndex,
    string FromSnapshotId, string ToSnapshotId, long ObservedProcessFrame,
    List<TimelineEvent> Events, int DroppedSamples = 0);

public sealed record TimelineSessionStatus(
    int SchemaVersion, string SessionId, string ProjectId, string InstanceId,
    string Status, int SampleEveryFrames, int MaxSamples, int SampleCount,
    int LastCommittedIndex, int DroppedSamples, string? LastSnapshotId,
    string? Code = null, string? Message = null);

public sealed record TimelineEventPage(
    string SessionId, int Total, int Offset, int Limit, List<TimelineBatch> Items);

/// <summary>Bounded observational diffs preserving Godot live node identity across samples.</summary>
public static class TimelineDeltaBuilder
{
    public const int SchemaVersion = 1;

    public static TimelineBatch Compare(
        SceneCheckpoint before, SceneCheckpoint after,
        int sampleIndex, long observedProcessFrame, int maxEvents = 10000)
    {
        SnapshotSchema.Validate(before);
        SnapshotSchema.Validate(after);
        if (before.ProjectId != after.ProjectId ||
            before.InstanceId != after.InstanceId ||
            before.SessionId != after.SessionId)
            throw new ArgumentException("timeline_identity_mismatch: cannot compare different sessions");
        if (sampleIndex < 1 || maxEvents < 1)
            throw new ArgumentOutOfRangeException(nameof(sampleIndex));
        var previous = before.Nodes.ToDictionary(node => node.NodeId, StringComparer.Ordinal);
        var current = after.Nodes.ToDictionary(node => node.NodeId, StringComparer.Ordinal);
        var events = new List<TimelineEvent>();

        void Add(TimelineEvent item)
        {
            if (events.Count >= maxEvents)
                throw new InvalidDataException("timeline_event_limit: observed changes exceed configured maximum");
            events.Add(item);
        }

        foreach (var node in before.Nodes)
            if (!current.ContainsKey(node.NodeId))
                Add(new TimelineEvent("node_removed", node.NodeId,
                    OldText: node.OriginalPath));
        foreach (var node in after.Nodes)
        {
            if (!previous.TryGetValue(node.NodeId, out var old))
            {
                Add(new TimelineEvent("node_added", node.NodeId, NewText: node.OriginalPath));
                continue;
            }
            if (old.ParentId != node.ParentId || old.SiblingIndex != node.SiblingIndex)
                Add(new TimelineEvent("node_moved", node.NodeId,
                    OldText: old.ParentId + ":" + old.SiblingIndex,
                    NewText: node.ParentId + ":" + node.SiblingIndex));
            if (old.Name != node.Name)
                Add(new TimelineEvent("node_renamed", node.NodeId,
                    OldText: old.Name, NewText: node.Name));

            foreach (string key in old.Properties.Keys.Union(node.Properties.Keys, StringComparer.Ordinal))
            {
                old.Properties.TryGetValue(key, out var a);
                node.Properties.TryGetValue(key, out var b);
                if (!EqualsValue(a, b))
                    Add(new TimelineEvent("property_changed", node.NodeId,
                        key, a, b));
            }
        }
        return new TimelineBatch(SchemaVersion, after.SessionId, sampleIndex,
            before.SnapshotId, after.SnapshotId, observedProcessFrame, events);
    }

    private static bool EqualsValue(SnapshotValue? a, SnapshotValue? b) =>
        ReferenceEquals(a, b) || (a is not null && b is not null &&
          a.Type == b.Type && a.RefId == b.RefId && JsonNode.DeepEquals(a.Data, b.Data));
}
