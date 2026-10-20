using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using GodotRuntimeMcp;
using GodotRuntimeMcp.Recording;

public static class CheckpointStoreTests
{
    public static async Task RunAsync(Action<bool, string> assert, string scratch)
    {
        string game = Path.Combine(scratch, "snapshot-game");
        Directory.CreateDirectory(game);
        await File.WriteAllTextAsync(Path.Combine(game, "project.godot"), "[application]\n");
        string projectId = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(RuntimeRegistry.Normalize(game)))).ToLowerInvariant();
        string snapshots = Path.Combine(RuntimeRegistry.ConfigRoot, "recordings",
            projectId, "session_abc", "snap_abc");
        var cp = new SceneCheckpoint(1, "snap_abc", "session_abc", projectId, "inst_abc",
            "n_1", 1, 1, 2, 2, new() {
                new RecordedNode("n_1", null, 0, "Root", "Node2D", null,
                    null, "/root/Main", null, false, false,
                    new(), new(), new(), new()),
                new RecordedNode("n_2", "n_1", 0, "Enemy", "CharacterBody2D", null,
                    null, "/root/Main/Enemy", null, false, false,
                    new(), new(), new(), new())
            }, new(), new(), new());
        await CheckpointArchive.CommitAsync(snapshots, cp, CancellationToken.None);

        var store = new CheckpointStore();
        var list = store.List(game);
        assert(list.Count == 1 && list[0].SnapshotId == "snap_abc",
            "Offline checkpoint store discovers verified snapshots without Godot");
        var info = store.Info(game, "snap_abc");
        assert(info.NodeCount == 2 && info.Status == "completed",
            "Offline checkpoint info verifies persisted manifest");
        var page = store.QueryNodes(game, "snap_abc", null, "/root/Main/Enemy",
            "CharacterBody2D", 0, 20);
        assert(page.Total == 1 && page.Items.Single().NodeId == "n_2",
            "Offline checkpoint node query filters path and type");

        bool escaped = false;
        try { store.Info(game, "../escape"); }
        catch (ArgumentException) { escaped = true; }
        assert(escaped, "Checkpoint store rejects traversal snapshot identifiers");

        string other = Path.Combine(scratch, "other-game");
        Directory.CreateDirectory(other);
        await File.WriteAllTextAsync(Path.Combine(other, "project.godot"), "[application]\n");
        bool foreign = false;
        try { store.Info(other, "snap_abc"); }
        catch (FileNotFoundException) { foreign = true; }
        assert(foreign, "Checkpoint store isolates project identities");

        var tools = new ToolService();
        assert(tools.Exists("godot_snapshot_list") &&
               tools.Exists("godot_snapshot_info") &&
               tools.Exists("godot_snapshot_nodes"),
            "Offline checkpoint query tools appear in MCP catalog");
        var offline = await tools.RunAsync("godot_snapshot_list", new JsonObject { ["project"] = game });
        assert(offline is JsonArray a && a.Count == 1, "Offline MCP checkpoint listing returns data");
        bool badPage = false;
        try { store.QueryNodes(game, "snap_abc", null, null, null, -1, 10); }
        catch (ArgumentOutOfRangeException) { badPage = true; }
        assert(badPage, "Offline checkpoint query rejects negative page offset");

        string sessionLink = Path.Combine(RuntimeRegistry.ConfigRoot,
            "recordings", projectId, "session_evil_link");
        Directory.CreateSymbolicLink(sessionLink, scratch);
        bool linked = false;
        try { store.List(game); }
        catch (UnauthorizedAccessException) { linked = true; }
        finally { Directory.Delete(sessionLink); }
        assert(linked, "Checkpoint store rejects symlinked sessions escaping the recording root");

        await File.AppendAllTextAsync(Path.Combine(snapshots, "state.json"), "tampered");
        bool corrupted = false;
        try { store.Info(game, "snap_abc"); }
        catch (InvalidDataException) { corrupted = true; }
        assert(corrupted, "Offline checkpoint info rejects tampered archive payloads");
    }
}
