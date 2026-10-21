using GodotRuntimeMcp;

public static class TimelineToolTests
{
    public static void Run(Action<bool, string> assert)
    {
        var tools = new ToolService();
        assert(tools.Exists("godot_timeline_start") &&
               tools.Exists("godot_timeline_status") &&
               tools.Exists("godot_timeline_stop"),
            "Timeline sampling exposes live start, status and stop over MCP");
        var begin = tools.CatalogModels.Single(x => x.Name == "godot_timeline_start");
        assert(begin.InputSchema.Properties.ContainsKey("sampleEveryFrames") &&
               begin.InputSchema.Properties.ContainsKey("maxSamples") &&
               begin.InputSchema.Properties.ContainsKey("project") &&
               begin.InputSchema.Properties.ContainsKey("instance"),
            "Recording controls retain project/instance routing and bounded sample configuration");
    }
}
