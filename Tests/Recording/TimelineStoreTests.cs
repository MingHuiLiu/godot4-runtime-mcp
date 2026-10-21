using System.Text.Json.Nodes;
using GodotRuntimeMcp;
using GodotRuntimeMcp.Recording;

public static class TimelineStoreTests
{
    public static async Task RunAsync(Action<bool, string> assert, string scratch)
    {
        string project = Path.Combine(scratch, "timeline-game");
        Directory.CreateDirectory(project);
        await File.WriteAllTextAsync(Path.Combine(project, "project.godot"), "[application]\n");
        string projectId = CheckpointStore.ProjectId(project);
        string dir = Path.Combine(RuntimeRegistry.ConfigRoot, "recordings",
            projectId, "rec_store", "timeline");
        await TimelineArchive.CreateAsync(dir,
            new TimelineSessionStatus(1, "rec_store", projectId,
                "inst_store", "running", 2, 8, 0, -1, 0, null),
            CancellationToken.None);
        await TimelineArchive.AppendAsync(dir,
            new TimelineBatch(1, "rec_store", 0, "snap_st",
                "snap_st", 14, new()), CancellationToken.None);
        await TimelineArchive.AppendAsync(dir,
            new TimelineBatch(1, "rec_store", 1, "snap_st", "snap_st2", 16,
                new() { new("node_removed", "n_99", OldText: "/root/Old") }),
            CancellationToken.None);
        await TimelineArchive.CompleteAsync(dir, "completed", CancellationToken.None);

        var store = new TimelineStore();
        assert(store.List(project).Count == 1 &&
               store.List(project)[0].SessionId == "rec_store" &&
               store.Info(project, "rec_store").SampleCount == 2,
            "Offline timeline store discovers persisted sessions without a running game");
        var page = store.Events(project, "rec_store", 1, 1);
        assert(page.Total == 2 && page.Items.Single().Events.Single().Kind == "node_removed",
            "Offline timeline query pages committed event batches");
        bool traversal = false;
        try { store.Info(project, "../bad"); }
        catch (ArgumentException) { traversal = true; }
        assert(traversal, "Offline timeline lookup rejects path traversal");

        string unrelated = Path.Combine(scratch, "different-timeline-game");
        Directory.CreateDirectory(unrelated);
        await File.WriteAllTextAsync(Path.Combine(unrelated, "project.godot"), "[application]\n");
        bool foreign = false;
        try { store.Info(unrelated, "rec_store"); }
        catch (FileNotFoundException) { foreign = true; }
        assert(foreign, "Offline timeline sessions are isolated per project");

        var tools = new ToolService();
        assert(tools.Exists("godot_timeline_sessions") &&
               tools.Exists("godot_timeline_info") &&
               tools.Exists("godot_timeline_events"),
            "MCP/CLI catalog includes three offline timeline query tools");
        var result = await tools.RunAsync("godot_timeline_events", new JsonObject {
            ["project"] = project, ["sessionId"] = "rec_store",
            ["offset"] = 1, ["limit"] = 1
        });
        assert(result?["items"]?.AsArray().Count == 1 &&
               result["items"]![0]!["events"]![0]!["kind"]!.ToString() == "node_removed",
            "Offline MCP event query returns typed per-session observed changes");
    }
}
