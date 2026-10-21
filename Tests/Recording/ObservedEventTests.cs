using GodotRuntimeMcp.Recording;
using System.Text.Json;
using System.Text.Json.Nodes;

public static class ObservedEventTests
{
    public static async Task RunAsync(Action<bool,string> assert, string scratch)
    {
        var item = new ObservedRuntimeEvent("signal", "n42", "pressed", 112, 55,
            DateTimeOffset.UtcNow, new() { ["button"] = new SnapshotValue("int64", JsonValue.Create(1)) });
        var observed = new TimelineBatch(1, "rec_observed", 0, "snap_1", "snap_1", 112,
            new(), 0, new() { item }, 2);
        string baseDir = Path.Combine(scratch, "observed-events");
        await TimelineArchive.CreateAsync(baseDir,
            new TimelineSessionStatus(1, "rec_observed", "project_o", "instance_o",
                "running", 1, 3, 0, -1, 0, null, CaptureSignals: true,
                CaptureInput: true), CancellationToken.None);
        await TimelineArchive.AppendAsync(baseDir, observed, CancellationToken.None);
        var stored = TimelineArchive.ReadPage(baseDir, 0, 10);
        assert(stored.Items[0].ObservedEvents?.Single().Name == "pressed" &&
               stored.Items[0].ObservedEvents![0].ProcessFrame == 112 &&
               stored.Items[0].DroppedObservedEvents == 2,
            "Observed signal/input data and dropped counts persist with checksum-verified timeline batches");
        assert(TimelineArchive.ReadStatus(baseDir).CaptureInput &&
               TimelineArchive.ReadStatus(baseDir).CaptureSignals,
            "Opt-in input and signal settings remain inspectable offline");
        var json = JsonSerializer.Serialize(observed, SnapshotJsonContext.Default.TimelineBatch);
        assert(JsonSerializer.Deserialize(json, SnapshotJsonContext.Default.TimelineBatch)?.ObservedEvents?.Count == 1,
            "Native AOT source-generated timeline JSON preserves event payloads");
    }
}
