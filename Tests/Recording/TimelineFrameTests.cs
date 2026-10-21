using GodotRuntimeMcp.Recording;

public static class TimelineFrameTests
{
    public static async Task RunAsync(Action<bool, string> assert, string scratch)
    {
        // A minimal PNG with a 1x1 IHDR, independent of Godot's renderer.
        byte[] png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLttAAAAABJRU5ErkJggg==");
        string root = Path.Combine(scratch, "timeline-image");
        var initial = new TimelineSessionStatus(1, "rec_image", "project_image",
            "instance_image", "running", 1, 4, 0, -1, 0, null,
            CaptureScreenshots: true);
        await TimelineArchive.CreateAsync(root, initial, CancellationToken.None);
        var image = await TimelineArchive.WriteImageAsync(root, 0, png, CancellationToken.None);
        assert(image.State == "captured" && image.FileName == "000000.png" &&
               image.Width == 1 && image.Height == 1 && image.ByteLength == png.Length &&
               image.Sha256?.Length == 64, "PNG capture is size-bounded and hashed");
        var batch = new TimelineBatch(1, initial.SessionId, 0, "snap_image",
            "snap_image", 7, new(), Screenshot: image);
        await TimelineArchive.AppendAsync(root, batch, CancellationToken.None);
        var fromDisk = TimelineArchive.ReadPage(root, 0, 10);
        assert(fromDisk.Items[0].Screenshot?.Sha256 == image.Sha256 &&
               TimelineArchive.ReadImage(root, fromDisk.Items[0]).SequenceEqual(png) &&
               TimelineArchive.ReadStatus(root).ScreenshotCount == 1,
            "Committed timeline image survives offline read and status accounting");
        if (!OperatingSystem.IsWindows())
        {
            var perms = File.GetUnixFileMode(Path.Combine(root, "frames", "000000.png"));
            assert((perms & (UnixFileMode.GroupRead | UnixFileMode.OtherRead)) == 0,
                "Screenshots are stored with owner-private permissions");
        }
        bool duplicate = false;
        try { await TimelineArchive.WriteImageAsync(root, 0, png, CancellationToken.None); }
        catch (IOException) { duplicate = true; }
        assert(duplicate, "Screenshot cannot overwrite existing committed frame");
        await File.AppendAllTextAsync(Path.Combine(root, "frames", "000000.png"), "tamper");
        bool corrupt = false;
        try { TimelineArchive.ReadPage(root, 0, 10); }
        catch (InvalidDataException e) { corrupt = e.Message.Contains("timeline_image_corrupt"); }
        assert(corrupt, "Timeline rejects corrupted PNG bytes and never displays unverified images");
        bool oversized = false;
        try { await TimelineArchive.WriteImageAsync(
            root, 1, new byte[700_000], CancellationToken.None); }
        catch (InvalidDataException e) { oversized = e.Message.Contains("timeline_image_limit"); }
        assert(oversized, "Screenshots exceeding the PNG limit are rejected before writing");
        bool traversal = false;
        try { TimelineArchive.ReadImage(root, batch with {
            Screenshot = image with { FileName = "../outside.png" }
        }); }
        catch (InvalidDataException e) { traversal = e.Message.Contains("timeline_image_corrupt"); }
        assert(traversal, "Screenshot metadata cannot escape its own timeline directory");
        var unavailable = batch with { SampleIndex = 1, Screenshot =
            new TimelineFrameImage("unavailable", null, null, 0, 0, 0, "renderer_unavailable") };
        await TimelineArchive.AppendAsync(root, unavailable, CancellationToken.None);
        var status = TimelineArchive.ReadStatus(root);
        assert(status.SkippedScreenshots == 1 &&
               TimelineArchive.ReadPage(root, 1, 1).Items[0].Screenshot?.Code ==
                   "renderer_unavailable",
            "Unavailable captures are counted explicitly without inventing an image");
    }
}
