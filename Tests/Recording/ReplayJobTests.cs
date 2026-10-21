using GodotRuntimeMcp;
using GodotRuntimeMcp.Recording;
using System.Text.Json.Nodes;

public static class ReplayJobTests
{
    public static async Task RunAsync(Action<bool, string> assert, string scratch)
    {
        string project = Path.Combine(scratch, "replay-host-game");
        Directory.CreateDirectory(project);
        await File.WriteAllTextAsync(Path.Combine(project, "project.godot"), "[application]\n");
        string projectId = CheckpointStore.ProjectId(project);
        string timeline = Path.Combine(RuntimeRegistry.ConfigRoot, "recordings",
            projectId, "rec_host", "timeline");
        await TimelineArchive.CreateAsync(timeline,
            new TimelineSessionStatus(1, "rec_host", projectId, "instance_host",
                "running", 5, 2, 0, -1, 0, null,
                CaptureInput: true), CancellationToken.None);
        await TimelineArchive.AppendAsync(timeline, new TimelineBatch(
            1, "rec_host", 0, "snap_start", "snap_start", 1, new()), CancellationToken.None);
        await TimelineArchive.AppendAsync(timeline, new TimelineBatch(
            1, "rec_host", 1, "snap_start", "snap_end", 2, new()), CancellationToken.None);
        await TimelineArchive.CompleteAsync(timeline, "completed", CancellationToken.None);

        string incompleteDir = Path.Combine(RuntimeRegistry.ConfigRoot, "recordings",
            projectId, "rec_unverified", "timeline");
        await TimelineArchive.CreateAsync(incompleteDir,
            new TimelineSessionStatus(1, "rec_unverified", projectId, "instance_host",
                "running", 5, 2, 0, -1, 0, null, CaptureInput: false),
            CancellationToken.None);
        await TimelineArchive.AppendAsync(incompleteDir, new TimelineBatch(
            1, "rec_unverified", 0, "snap_pre", "snap_pre", 1, new()),
            CancellationToken.None);
        await TimelineArchive.AppendAsync(incompleteDir, new TimelineBatch(
            1, "rec_unverified", 1, "snap_pre", "snap_post", 6, new()),
            CancellationToken.None);
        await TimelineArchive.CompleteAsync(incompleteDir, "completed", CancellationToken.None);
        var manager = new ReplayJobManager();
        var notReplayable = manager.Start(project, "rec_unverified", null);
        assert(notReplayable.Status == "inconclusive" &&
               notReplayable.Code == "replay_incomplete",
            "Replay manager rejects recordings without opted-in input before starting a worker");
        var missingEngine = manager.Start(project, "rec_host", "/does/not/exist/Godot");
        assert(missingEngine.Status == "failed" &&
               missingEngine.Code == "replay_engine_unavailable",
               "Replay refuses missing Godot Mono binary");
        bool traversal = false;
        try { manager.Status(project, "../other"); }
        catch (ArgumentException) { traversal = true; }
        assert(traversal, "Replay job paths reject traversal");
        var unsupported = manager.Start(project, "../other", null);
        assert(unsupported.Status == "failed" &&
               unsupported.Code == "replay_session_not_found",
            "Replay refuses unknown or cross-project session IDs");
        var tools = new ToolService();
        assert(tools.Exists("godot_replay_start") && tools.Exists("godot_replay_status"),
            "Offline replay commands appear in MCP/CLI tool catalog");
        assert(tools.CatalogModels.Single(t => t.Name == "godot_replay_start")
            .InputSchema.Properties.ContainsKey("sessionId"),
            "Replay start requires an explicit recording session");
        bool missing = false;
        try
        {
            await tools.RunAsync("godot_replay_status", new JsonObject {
                ["project"] = project, ["jobId"] = "replay_nonexistent"
            });
        }
        catch (FileNotFoundException) { missing = true; }
        assert(missing, "Offline replay status refuses nonexistent jobs");
    }
}
