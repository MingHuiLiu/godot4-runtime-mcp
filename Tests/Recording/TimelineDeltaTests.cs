using GodotRuntimeMcp.Recording;
using System.Text.Json.Nodes;

public static class TimelineDeltaTests
{
    public static void Run(Action<bool, string> assert)
    {
        static RecordedNode Node(string id, string? parent, string name, int hp = 1) =>
            new(id, parent, 0, name, "Node2D", null, null, "/root/" + name, null,
                false, false, new(), new()
                {
                    ["health"] = new SnapshotValue("int64", JsonValue.Create(hp))
                }, new(), new());
        static SceneCheckpoint Checkpoint(string id, params RecordedNode[] nodes) =>
            new(1, id, "session_rec", "project_1", "instance_1", "n0",
                10, 10, 10, 10, nodes.ToList(), new(), new(), new());
        var a = Checkpoint("snap_1", Node("n0", null, "World"),
            Node("n1", "n0", "Enemy", 8),
            Node("n2", "n0", "Gone"));
        var b = Checkpoint("snap_2", Node("n0", null, "World"),
            Node("n1", "n0", "Renamed", 3),
            Node("n3", "n1", "New"));
        var batch = TimelineDeltaBuilder.Compare(a, b, 1, 300);
        assert(batch.Events.Count(e => e.Kind == "node_added") == 1 &&
               batch.Events.Count(e => e.Kind == "node_removed") == 1 &&
               batch.Events.Count(e => e.Kind == "node_renamed") == 1 &&
               batch.Events.Count(e => e.Kind == "property_changed") == 1,
            "Timeline deltas preserve births, disappearances, identity and value changes");
        assert(batch.Events.Single(e => e.Kind == "property_changed").OldValue!.Data!.GetValue<int>() == 8 &&
               batch.Events.Single(e => e.Kind == "property_changed").NewValue!.Data!.GetValue<int>() == 3,
            "Timeline changes retain typed before and after values");
        assert(batch.FromSnapshotId == "snap_1" && batch.ToSnapshotId == "snap_2" &&
               batch.SampleIndex == 1 && batch.ObservedProcessFrame == 300,
            "Timeline batches identify two observed sample boundaries, not continuous truth");

        bool rejected = false;
        try { TimelineDeltaBuilder.Compare(a with { SchemaVersion = 999 }, b, 1, 300); }
        catch (NotSupportedException) { rejected = true; }
        assert(rejected, "Timeline comparison rejects an unknown snapshot schema");
        bool capped = false;
        try { TimelineDeltaBuilder.Compare(a, b, 1, 300, maxEvents: 1); }
        catch (InvalidDataException e) { capped = e.Message.Contains("timeline_event_limit"); }
        assert(capped, "Timeline enforces an explicit per-sample event limit");
    }
}
