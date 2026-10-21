using GodotRuntimeMcp;
using GodotRuntimeMcp.Recording;
using System.Text.Json.Nodes;

public static class TimelineGalleryTests
{
    public static async Task RunAsync(Action<bool, string> assert, string scratch)
    {
        string project = Path.Combine(scratch, "gallery-game");
        Directory.CreateDirectory(project);
        await File.WriteAllTextAsync(Path.Combine(project, "project.godot"), "[application]\n");
        string projectId = CheckpointStore.ProjectId(project);
        string timeline = Path.Combine(RuntimeRegistry.ConfigRoot, "recordings",
            projectId, "rec_gallery", "timeline");
        await TimelineArchive.CreateAsync(timeline,
            new TimelineSessionStatus(1, "rec_gallery", projectId, "instance_g",
                "running", 1, 4, 0, -1, 0, null, CaptureScreenshots: true),
            CancellationToken.None);
        byte[] png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLttAAAAABJRU5ErkJggg==");
        var shot = await TimelineArchive.WriteImageAsync(timeline, 0, png, CancellationToken.None);
        await TimelineArchive.AppendAsync(timeline,
            new TimelineBatch(1, "rec_gallery", 0, "snap_0", "snap_0", 111,
                new() { new TimelineEvent("node_added", "<img onerror=alert(1)>") },
                Screenshot: shot), CancellationToken.None);
        await TimelineArchive.AppendAsync(timeline,
            new TimelineBatch(1, "rec_gallery", 1, "snap_0", "snap_1", 112,
                new() { new TimelineEvent("node_renamed", "n0",
                    OldText: "A <script>alert(1)</script>", NewText: "B") },
                Screenshot: new TimelineFrameImage("unavailable",
                    null, null, 0, 0, 0, "renderer_unavailable")),
            CancellationToken.None);
        await TimelineArchive.CompleteAsync(timeline, "completed", CancellationToken.None);

        var gallery = new TimelineGalleryExporter();
        var exported = gallery.Export(project, "rec_gallery", 0, 10);
        string html = await File.ReadAllTextAsync(exported.HtmlPath);
        assert(exported.Total == 2 && exported.ImageCount == 1 &&
               exported.UnavailableCount == 1 && exported.IncludedSamples == 2,
            "HTML timeline exports exact sample and screenshot counts");
        assert(html.Contains("data:image/png;base64,") &&
               html.Contains("node_added") && html.Contains("node_renamed") &&
               html.Contains("renderer_unavailable") && html.Contains("111"),
            "Standalone offline gallery aligns frames, skipped markers and events");
        assert(!html.Contains("<script") &&
               !html.Contains("<img onerror=alert(1)>") &&
               html.Contains("&lt;img onerror=alert(1)&gt;") &&
               html.Contains("Content-Security-Policy"),
            "Gallery escapes event content and forbids active scripts");
        if (!OperatingSystem.IsWindows())
        {
            var perms = File.GetUnixFileMode(exported.HtmlPath);
            assert((perms & (UnixFileMode.GroupRead | UnixFileMode.OtherRead)) == 0,
                "Offline HTML contains private screenshots and uses owner-only file permissions");
        }
        var tools = new ToolService();
        assert(tools.Exists("godot_timeline_export_gallery"),
            "Gallery exporter appears in MCP and CLI tool catalogs");
        var json = await tools.RunAsync("godot_timeline_export_gallery", new JsonObject
        {
            ["project"] = project, ["sessionId"] = "rec_gallery",
            ["offset"] = 0, ["limit"] = 10
        });
        assert(json?["imageCount"]?.GetValue<int>() == 1 &&
               File.Exists(json["htmlPath"]!.GetValue<string>()),
            "Offline MCP gallery tool writes a real local standalone HTML file");
        bool invalid = false;
        try { gallery.Export(project, "../elsewhere", 0, 5); }
        catch (ArgumentException) { invalid = true; }
        assert(invalid, "Gallery cannot escape project timeline path");
        bool badPage = false;
        try { gallery.Export(project, "rec_gallery", 0, 10000); }
        catch (ArgumentOutOfRangeException) { badPage = true; }
        assert(badPage, "Gallery enforces bounded page sizes");
        await File.AppendAllTextAsync(Path.Combine(timeline, "frames", "000000.png"), "corrupt");
        bool corrupt = false;
        try { gallery.Export(project, "rec_gallery", 0, 10); }
        catch (InvalidDataException e) { corrupt = e.Message.Contains("timeline_image_corrupt"); }
        assert(corrupt, "Gallery never embeds tampered screenshot bytes");
    }
}
