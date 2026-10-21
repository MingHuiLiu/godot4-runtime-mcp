using GodotRuntimeMcp;
using GodotRuntimeMcp.Recording;
using System.Text.Json.Nodes;

public static class ExportJobManagerTests
{
    public static async Task RunAsync(Action<bool, string> assert, string scratch)
    {
        // Use a dedicated intact archive: CheckpointStoreTests intentionally corrupts its fixture.
        var project = Path.Combine(scratch, "export-game");
        Directory.CreateDirectory(project);
        await File.WriteAllTextAsync(Path.Combine(project, "project.godot"), "[application]\n");
        string projectId = CheckpointStore.ProjectId(project);
        await CheckpointArchive.CommitAsync(Path.Combine(RuntimeRegistry.ConfigRoot,
            "recordings", projectId, "session_exp", "snap_abc"),
            new SceneCheckpoint(1, "snap_abc", "session_exp", projectId, "instance_exp",
                "n1", 1, 1, 1, 1, new() {
                    new RecordedNode("n1", null, 0, "World", "Node2D", null, null,
                        "/root/World", null, false, false, new(), new(), new(), new())
                }, new(), new(), new()), CancellationToken.None);
        var manager = new ExportJobManager();
        var notFound = manager.Start(project, "snap_abc", true,
            Path.Combine(scratch, "missing-godot-mono"));
        assert(notFound.Status == "failed" && notFound.Code == "export_engine_unavailable",
            "Offline export refuses missing Godot Mono executable");

        // A failed verification must remain inspectable by an AI after the worker exits.
        var failedJobDir = Path.Combine(RuntimeRegistry.ConfigRoot, "recordings",
            projectId, "exports", "export_fixture", "output");
        Directory.CreateDirectory(failedJobDir);
        var failedReport = new SceneExportResult("failed", null, null, null,
            Path.Combine(failedJobDir, "reconstruction_report.json"), "partial",
            new() { new SnapshotIssue("resource_property_mismatch", null,
                "r_2.Next", "Cyclic Resource edge missing") },
            2, 2, "export_validation_failed", "Cyclic Resource graph cannot be faithfully reloaded");
        await File.WriteAllTextAsync(Path.Combine(failedJobDir, "reconstruction_report.json"),
            System.Text.Json.JsonSerializer.Serialize(failedReport,
                SnapshotJsonContext.Default.SceneExportResult));
        var failedStatus = manager.Status(project, "export_fixture");
        assert(failedStatus.Status == "failed" &&
               failedStatus.Code == "export_validation_failed" &&
               failedStatus.ReportPath is not null &&
               failedStatus.Message!.Contains("Cyclic Resource"),
            "Failed export status surfaces persisted reconstruction report and exact error");

        bool invalidId = false;
        try { manager.Status(project, "../escape"); }
        catch (ArgumentException) { invalidId = true; }
        assert(invalidId, "Export status refuses path traversal job ID");

        var tools = new ToolService();
        assert(tools.Exists("godot_snapshot_export_tscn") &&
               tools.Exists("godot_snapshot_export_status"),
            "Offline export and status are available through MCP and CLI catalogs");

        var response = await tools.RunAsync("godot_snapshot_export_tscn", new JsonObject
        {
            ["project"] = project, ["snapshotId"] = "snap_abc", ["strict"] = true,
            ["godotBinary"] = Path.Combine(scratch, "missing-godot-mono")
        });
        assert(response?["code"]?.ToString() == "export_engine_unavailable",
            "Offline export tool reports structured missing-engine failure");
    }
}
