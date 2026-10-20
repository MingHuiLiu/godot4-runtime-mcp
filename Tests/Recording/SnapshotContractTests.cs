using System.Text.Json;
using System.Text.Json.Nodes;
using GodotRuntimeMcp.Recording;

public static class SnapshotContractTests
{
    public static void Run(Action<bool, string> assert)
    {
        var player = new RecordedNode(
            "n_1", null, 0, "Player", "CharacterBody2D",
            "res://player.cs", "res://player.tscn", "/root/Main/Player", null,
            false, true, new List<string> { "team" },
            new Dictionary<string, SnapshotValue>
            {
                ["health"] = new("int64", JsonValue.Create(80), null),
                ["target"] = new("node_ref", null, "n_2"),
                ["shape"] = new("resource_ref", null, "r_1")
            },
            new Dictionary<string, SnapshotValue>(),
            new List<SnapshotIssue>());
        var shape = new RecordedResource(
            "r_1", "RectangleShape2D", null, null, true,
            new Dictionary<string, SnapshotValue>
            {
                ["size"] = new("vector2", JsonNode.Parse("{\"x\":32,\"y\":48}"), null)
            },
            new List<SnapshotIssue>());
        var cp = new SceneCheckpoint(
            1, "snap_a", "session_a", "project_a", "instance_a",
            "n_1", 12, 12, 21, 21,
            new List<RecordedNode> { player }, new List<RecordedResource> { shape },
            new Dictionary<string, SnapshotValue>(), new List<SnapshotIssue>());
        var json = JsonSerializer.Serialize(cp, SnapshotJsonContext.Default.SceneCheckpoint);
        var restored = JsonSerializer.Deserialize(json, SnapshotJsonContext.Default.SceneCheckpoint);
        assert(restored is { SchemaVersion: 1, RootNodeId: "n_1" } &&
               restored.Nodes[0].Groups.Contains("team") &&
               restored.Nodes[0].Properties["target"].RefId == "n_2" &&
               restored.Nodes[0].Properties["shape"].RefId == "r_1" &&
               restored.Resources[0].Properties["size"].Data?["y"]?.GetValue<int>() == 48,
            "Scene checkpoint preserves typed properties, owner and resource references");
        bool rejected = false;
        try { SnapshotSchema.Validate(cp with { SchemaVersion = 2 }); }
        catch (NotSupportedException) { rejected = true; }
        assert(rejected, "Checkpoint schema rejects unknown future versions");
    }
}
