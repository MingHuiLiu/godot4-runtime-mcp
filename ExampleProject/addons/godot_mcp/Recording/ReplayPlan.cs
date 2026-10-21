using System.Text.Json.Nodes;

namespace GodotRuntimeMcp.Recording;

/// <summary>Limited, explicitly verifiable input replay profile, NOT engine determinism.</summary>
public sealed record ReplayIssue(string Code, string? NodePath, string? Property,
    string Message);

public sealed record ReplayStep(int SampleIndex, string SnapshotId, long ProcessFrame,
    List<ObservedRuntimeEvent> Events);

public sealed record ReplayPlan(string Status, string SessionId, string ProjectId,
    string InstanceId, List<ReplayStep> Steps);

public sealed record ReplayRequest(string ProjectPath, string SessionDirectory,
    string OutputDirectory, bool DeliberateDivergence = false);

public sealed record ReplayReport(string Status, string Code, string? Message,
    string SessionId, int ComparedSamples, int MatchedSamples, int InjectedEvents,
    List<ReplayIssue> Issues, string? ReportPath = null);

public static class ReplayPlanBuilder
{
    public static ReplayPlan Build(TimelineSessionStatus status,
        IReadOnlyList<TimelineBatch> batches)
    {
        if (status.SchemaVersion != 1)
            throw new NotSupportedException("unsupported_replay_schema");
        if (status.Status is not ("completed" or "stopped") ||
            !status.CaptureInput || status.SampleEveryFrames != 1 ||
            status.DroppedSamples != 0 || status.DroppedObservedEvents != 0 ||
            status.SampleCount is < 2 or > 60 || batches.Count != status.SampleCount)
            throw new InvalidDataException(
                "replay_incomplete: input capture, every-frame sampling and no drops are required");
        if (batches.Count > 60 || batches[^1].ObservedProcessFrame -
            batches[0].ObservedProcessFrame > 1200)
            throw new InvalidDataException("replay_limit: recording window too long");
        var steps = new List<ReplayStep>(batches.Count);
        for (int i = 0; i < batches.Count; i++)
        {
            var batch = batches[i];
            if (batch.SchemaVersion != 1 || batch.SessionId != status.SessionId ||
                batch.SampleIndex != i ||
                (i == 0 && batch.FromSnapshotId != batch.ToSnapshotId) ||
                (i > 0 && (batch.FromSnapshotId != batches[i - 1].ToSnapshotId ||
                            batch.ObservedProcessFrame < batches[i - 1].ObservedProcessFrame)) ||
                batch.DroppedSamples != 0 || batch.DroppedObservedEvents != 0)
                throw new InvalidDataException("replay_incomplete: batch continuity violated");
            var events = batch.ObservedEvents ?? new();
            if (i == 0 && events.Any(e => e.Kind == "input"))
                throw new InvalidDataException(
                    "replay_incomplete: initial sample has pre-baseline input");
            if (events.Count > 256)
                throw new InvalidDataException("replay_limit: too many input events");
            foreach (var e in events)
            {
                if (e.Kind != "input" || e.Name is not ("key" or "mouse_button" or "joy_button"))
                    throw new NotSupportedException(
                        "replay_unsupported_event: only key, mouse button and joy button are injectable");
                if (e.ProcessFrame > batch.ObservedProcessFrame ||
                    e.ProcessFrame <= batches[0].ObservedProcessFrame)
                    throw new InvalidDataException("replay_incomplete: unbounded input event frame");
                if (!e.Fields.TryGetValue("pressed", out var pressed) || pressed.Type != "bool" ||
                    pressed.Data is null || !pressed.Data.GetValueKind().Equals(
                        System.Text.Json.JsonValueKind.True) &&
                        !pressed.Data.GetValueKind().Equals(
                        System.Text.Json.JsonValueKind.False))
                    throw new InvalidDataException("replay_invalid_event: pressed value missing");
                string code = e.Name == "key" ? "keycode" :
                    e.Name == "mouse_button" ? "button" : "button";
                if (!e.Fields.TryGetValue(code, out var value) ||
                    value.Type != "int64" || value.Data is null ||
                    !long.TryParse(value.Data.ToJsonString(), out long inputCode) ||
                    inputCode is < 0 or > 1000000)
                    throw new InvalidDataException("replay_invalid_event: input code out of bounds");
            }
            steps.Add(new ReplayStep(i, batch.ToSnapshotId,
                batch.ObservedProcessFrame, events.ToList()));
        }
        return new ReplayPlan("ready", status.SessionId, status.ProjectId,
            status.InstanceId, steps);
    }
}

public static class ReplayStateComparer
{
    /// <summary>
    /// Cross-process comparison by relative topology: process-local NodeId is NEVER
    /// used for equality. Properties include exported storage fields, not private
    /// C# fields, engine transient state or global frame number.
    /// </summary>
    public static List<ReplayIssue> Compare(SceneCheckpoint expected,
        SceneCheckpoint observed, int maxIssues = 100)
    {
        SnapshotSchema.Validate(expected);
        SnapshotSchema.Validate(observed);
        var issues = new List<ReplayIssue>();
        static Dictionary<string, RecordedNode> ByPath(SceneCheckpoint s)
        {
            var ids = s.Nodes.ToDictionary(n => n.NodeId);
            var result = new Dictionary<string, RecordedNode>(StringComparer.Ordinal);
            foreach (var n in s.Nodes)
            {
                var parts = new List<string>();
                var current = n;
                for (int depth = 0; depth < 256; depth++)
                {
                    parts.Add(current.Name + "#" + current.SiblingIndex);
                    if (current.ParentId is null) break;
                    if (!ids.TryGetValue(current.ParentId, out current!))
                        throw new InvalidDataException("replay_incomplete: orphan node");
                }
                parts.Reverse();
                string path = string.Join("/", parts.Skip(1)); // Root may be renamed in isolated worker
                if (!result.TryAdd(path, n))
                    throw new InvalidDataException("replay_incomplete: ambiguous node path");
            }
            return result;
        }
        var a = ByPath(expected);
        var b = ByPath(observed);
        void Add(string code, string? path, string? prop, string message)
        {
            if (issues.Count < maxIssues)
                issues.Add(new ReplayIssue(code, path, prop, message));
        }
        foreach (var (path, exp) in a)
        {
            if (!b.TryGetValue(path, out var got))
            {
                Add("replay_node_missing", path, null, "Expected node is absent");
                continue;
            }
            if (exp.NativeClass != got.NativeClass || exp.ScriptPath != got.ScriptPath ||
                exp.Name != got.Name)
                Add("replay_node_diverged", path, null, "Type/name/script differs");
            foreach (var (name, value) in exp.Properties)
            {
                if (!got.Properties.TryGetValue(name, out var observedValue) ||
                    !Same(value, observedValue))
                    Add("replay_property_diverged", path, name,
                        "Serialized state differs at the observed sample boundary");
            }
            foreach (string extra in got.Properties.Keys.Except(exp.Properties.Keys))
                Add("replay_property_diverged", path, extra,
                    "Unexpected new serialized property");
        }
        foreach (string path in b.Keys.Except(a.Keys))
            Add("replay_node_unexpected", path, null, "Unexpected node appeared");
        if (expected.Resources.Count != observed.Resources.Count)
            Add("replay_resources_diverged", null, null, "Resource graph count differs");
        return issues;
    }

    private static bool Same(SnapshotValue expected, SnapshotValue actual) =>
        expected.Type == actual.Type &&
        (expected.Type is not ("node_ref" or "resource_ref")
            ? expected.RefId == actual.RefId
            : true) && JsonNode.DeepEquals(expected.Data, actual.Data);
}
