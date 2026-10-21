using GodotRuntimeMcp.Recording;

public static class TimelineArchiveTests
{
    public static async Task RunAsync(Action<bool, string> assert, string scratch)
    {
        string root = Path.Combine(scratch, "timeline-archive");
        var session = new TimelineSessionStatus(1, "session_t1", "proj_t",
            "inst_t", "running", 3, 10, 0, -1, 0, null);
        await TimelineArchive.CreateAsync(root, session, CancellationToken.None);
        var first = new TimelineBatch(1, "session_t1", 0, "snap_1",
            "snap_1", 100, new List<TimelineEvent>());
        var status = await TimelineArchive.AppendAsync(root, first, CancellationToken.None);
        assert(status.SampleCount == 1 && status.LastCommittedIndex == 0 &&
               status.LastSnapshotId == "snap_1",
            "Timeline publishes first sample after durable batch commit");
        var another = new TimelineBatch(1, "session_t1", 1, "snap_1", "snap_2",
            103, new List<TimelineEvent> { new("node_added", "n9", NewText: "/root/New") });
        status = await TimelineArchive.AppendAsync(root, another, CancellationToken.None);
        var events = TimelineArchive.ReadPage(root, 0, 10);
        assert(events.Total == 2 && events.Items[1].Events.Single().NodeId == "n9",
            "Timeline batches survive restart and retain observable delta events");
        bool duplicate = false;
        try { await TimelineArchive.AppendAsync(root, another, CancellationToken.None); }
        catch (InvalidDataException e) { duplicate = e.Message.Contains("timeline_sequence_invalid"); }
        assert(duplicate, "Timeline refuses duplicate or out-of-order sequence indexes");
        await TimelineArchive.CompleteAsync(root, "stopped", CancellationToken.None);
        assert(TimelineArchive.ReadStatus(root).Status == "stopped",
            "Stopped timeline remains inspectable without a running game");
        bool reopened = false;
        try { await TimelineArchive.CompleteAsync(root, "completed", CancellationToken.None); }
        catch (InvalidDataException e) { reopened = e.Message.Contains("timeline_terminal_state"); }
        assert(reopened, "Terminal timeline status cannot be silently rewritten");
        assert(TimelineArchive.ReadPage(root, int.MaxValue, 10).Items.Count == 0,
            "Very large page offset does not overflow or return stale events");
        bool invalidSchema = false;
        try { await TimelineArchive.CreateAsync(Path.Combine(scratch, "timeline-future"),
            session with { SchemaVersion = 2 }, CancellationToken.None); }
        catch (NotSupportedException) { invalidSchema = true; }
        assert(invalidSchema, "Unknown timeline format version is rejected");
        string batchPath = Path.Combine(root, "batches", "000001.json");
        await File.AppendAllTextAsync(batchPath, "\ncorruption");
        bool corrupt = false;
        try { TimelineArchive.ReadPage(root, 0, 10); }
        catch (InvalidDataException e) { corrupt = e.Message.Contains("timeline_corrupt"); }
        assert(corrupt, "Timeline reader rejects tampered event data");
    }
}
