using GodotRuntimeMcp.Recording;
using System.Text.Json.Nodes;

public static class ReplayContractTests
{
    public static void Run(Action<bool, string> assert)
    {
        static RecordedNode Node(string id, string? parent, string name, int clicks) =>
            new(id, parent, 0, name, "Node2D", null, null, "/root/" + name,
                null, false, false, new(), new() {
                    ["hits"] = new SnapshotValue("int64", JsonValue.Create(clicks))
                }, new(), new());
        static SceneCheckpoint Cp(string id, int hits, string nodeId = "n1") =>
            new(1, id, "rec_demo", "project_demo", "instance_demo", "n0",
                0, 0, 0, 0, new() { Node("n0", null, "World", 0),
                  Node(nodeId, "n0", "Counter", hits) }, new(), new(), new());
        var first = Cp("snap_a", 0);
        var second = Cp("snap_b", 1);
        var source = new TimelineSessionStatus(1, "rec_demo", "project_demo", "instance_demo",
            "completed", 1, 4, 2, 1, 0, "snap_b", CaptureInput: true);
        var start = new TimelineBatch(1, "rec_demo", 0, "snap_a", "snap_a", 10, new());
        var key = new ObservedRuntimeEvent("input", "/viewport", "key", 11, 11,
            DateTimeOffset.UtcNow, new() {
                ["keycode"] = new("int64", JsonValue.Create(65)),
                ["physicalKeycode"] = new("int64", JsonValue.Create(65)),
                ["pressed"] = new("bool", JsonValue.Create(true)),
                ["echo"] = new("bool", JsonValue.Create(false))
            });
        var next = new TimelineBatch(1, "rec_demo", 1, "snap_a", "snap_b",
            12, new(), ObservedEvents: new() { key });
        var plan = ReplayPlanBuilder.Build(source, new[] { start, next });
        assert(plan.Status == "ready" && plan.Steps.Count == 2 &&
               plan.Steps[1].Events.Single().Name == "key",
            "Verified replay plan is ordered and includes bounded synthetic input");
        var match = ReplayStateComparer.Compare(second, Cp("other", 1, "new-id"));
        assert(match.Count == 0,
            "Replay compares structural paths/properties rather than process-local node IDs");
        var mismatch = ReplayStateComparer.Compare(second, Cp("other", 3, "new-id"));
        assert(mismatch.Any(i => i.Code == "replay_property_diverged" &&
                                  i.Property == "hits"),
            "Diverged properties produce actionable typed diagnostics");
        bool dropped = false;
        try { ReplayPlanBuilder.Build(source with { DroppedObservedEvents = 1 },
              new[] { start, next }); }
        catch (InvalidDataException e) { dropped = e.Message.Contains("replay_incomplete"); }
        assert(dropped, "Lost input events prohibit an exact replay claim");
        bool sparse = false;
        try { ReplayPlanBuilder.Build(source with { SampleEveryFrames = 5 },
              new[] { start, next }); }
        catch (InvalidDataException e) { sparse = e.Message.Contains("replay_incomplete"); }
        assert(sparse, "Sparse sampling is rejected for verified replay profile");
        bool invalid = false;
        try { ReplayPlanBuilder.Build(source,
              new[] { start, next with { ObservedEvents = new() {
                  key with { Name = "touch", Fields = new() } } } }); }
        catch (NotSupportedException) { invalid = true; }
        assert(invalid, "Unsupported input kinds are rejected, never silently ignored");
        bool skipped = false;
        try { ReplayPlanBuilder.Build(source, new[] { start,
                next with { FromSnapshotId = "wrong" } }); }
        catch (InvalidDataException) { skipped = true; }
        assert(skipped, "Replay must verify every checkpoint transition and sample index");
        bool signal = false;
        try { ReplayPlanBuilder.Build(source, new[] { start,
                next with { ObservedEvents = new() {
                    key with { Kind = "signal", Name = "pressed" } } } }); }
        catch (NotSupportedException) { signal = true; }
        assert(signal, "Non-injectable signal observations are explicitly out of replay profile");
    }
}
