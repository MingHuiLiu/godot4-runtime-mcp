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

        var aResource = new RecordedResource("r_1", "RectangleShape2D", null, null,
            false, new() { ["height"] = new SnapshotValue("int64", JsonValue.Create(2)) }, new());
        var bResource = aResource with { Properties = new() {
            ["height"] = new SnapshotValue("int64", JsonValue.Create(7)) } };
        var aEnriched = a with { Resources = new() { aResource }, Nodes = a.Nodes
            .Select(n => n.NodeId != "n1" ? n : n with {
                DiagnosticProperties = new() {
                    ["global_position"] = new SnapshotValue("vector2",
                        JsonNode.Parse("{\"x\":1,\"y\":2}")) }
            }).ToList() };
        var bEnriched = b with { Resources = new() { bResource }, Nodes = b.Nodes
            .Select(n => n.NodeId != "n1" ? n : n with {
                Groups = new() { "enemy" },
                DiagnosticProperties = new() {
                    ["global_position"] = new SnapshotValue("vector2",
                        JsonNode.Parse("{\"x\":5,\"y\":8}")) }
            }).ToList() };
        var changes = TimelineDeltaBuilder.Compare(aEnriched, bEnriched, 1, 301);
        assert(changes.Events.Any(e => e.Kind == "diagnostic_changed" && e.NodeId == "n1") &&
               changes.Events.Any(e => e.Kind == "groups_changed" && e.NodeId == "n1") &&
               changes.Events.Any(e => e.Kind == "resource_property_changed" &&
                   e.NodeId == "r_1" && e.Property == "height"),
            "Timeline reports observed diagnostic, group and Resource property changes");
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
