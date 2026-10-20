using GodotRuntimeMcp;

public static class SnapshotToolTests
{
    public static void Run(Action<bool, string> assert)
    {
        var tools = new ToolService();
        assert(tools.Exists("godot_snapshot_capture") &&
               tools.Exists("godot_snapshot_job_status"),
            "Snapshot capture and status tools appear in canonical manifest");
        var capture = tools.CatalogModels.Single(t => t.Name == "godot_snapshot_capture");
        assert(capture.InputSchema.Properties.ContainsKey("rootPath") &&
               capture.InputSchema.Properties.ContainsKey("strict") &&
               capture.InputSchema.Properties.ContainsKey("project") &&
               capture.InputSchema.Properties.ContainsKey("instance"),
            "Snapshot capture tool inherits existing project/instance routing");
    }
}
