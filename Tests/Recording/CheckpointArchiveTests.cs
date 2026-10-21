using System.Text.Json.Nodes;
using GodotRuntimeMcp.Recording;

public static class CheckpointArchiveTests
{
    public static async Task RunAsync(Action<bool, string> assert, string scratch)
    {
        string directory = Path.Combine(scratch, "archive-check");
        var cp = new SceneCheckpoint(1, "cp123", "session1", "proj1", "instance1",
            "n1", 10, 10, 20, 20,
            new List<RecordedNode> {
                new("n1", null, 0, "Root", "Node2D", null, null, "/root/Root",
                    null, false, false, new(), new() { ["health"] =
                        new SnapshotValue("int64", JsonValue.Create(42)) }, new(), new())
            }, new(), new(), new());
        var manifest = await CheckpointArchive.CommitAsync(directory, cp, CancellationToken.None);
        var loaded = await CheckpointArchive.ReadAsync(directory, CancellationToken.None);
        assert(manifest.Status == "completed" && loaded.Nodes.Single().Properties["health"].Data!.GetValue<int>() == 42,
            "Checkpoint archive persists and reads typed node properties");
        if (!OperatingSystem.IsWindows())
        {
            const UnixFileMode other = UnixFileMode.GroupRead | UnixFileMode.GroupWrite |
                UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite |
                UnixFileMode.OtherExecute;
            assert((File.GetUnixFileMode(directory) & other) == 0 &&
                   (File.GetUnixFileMode(Path.Combine(directory, "manifest.json")) & other) == 0,
                "Checkpoint directory and manifest are private to the current OS user");
        }
        bool duplicate = false;
        try { await CheckpointArchive.CommitAsync(directory, cp, CancellationToken.None); }
        catch (IOException) { duplicate = true; }
        assert(duplicate, "Checkpoint archive refuses to overwrite committed manifest");
        // A second writer must NOT corrupt the first committed archive before failing.
        var different = cp with { Nodes = new() {
            cp.Nodes[0] with { Properties = new() { ["health"] =
                new SnapshotValue("int64", JsonValue.Create(99)) } }
        } };
        try { await CheckpointArchive.CommitAsync(directory, different, CancellationToken.None); }
        catch (IOException) { }
        var unchanged = await CheckpointArchive.ReadAsync(directory, CancellationToken.None);
        assert(unchanged.Nodes[0].Properties["health"].Data!.GetValue<int>() == 42,
            "Failed duplicate checkpoint commit preserves original payload bytes");
        string nodes = Path.Combine(directory, "nodes.jsonl.gz");
        var contents = await File.ReadAllBytesAsync(nodes);
        contents[0] ^= 0xff;
        await File.WriteAllBytesAsync(nodes, contents);
        bool corrupt = false;
        try { await CheckpointArchive.ReadAsync(directory, CancellationToken.None); }
        catch (InvalidDataException e) { corrupt = e.Message.Contains("snapshot_corrupt"); }
        assert(corrupt, "Checkpoint archive refuses corrupted GZip contents");
        string missing = Path.Combine(scratch, "archive-missing");
        Directory.CreateDirectory(missing);
        bool incomplete = false;
        try { await CheckpointArchive.ReadAsync(missing, CancellationToken.None); }
        catch (InvalidDataException e) { incomplete = e.Message.Contains("snapshot_incomplete"); }
        assert(incomplete, "Checkpoint archive refuses uncommitted snapshot");
        bool unknownVersion = false;
        try { await CheckpointArchive.CommitAsync(Path.Combine(scratch, "bad-version"),
            cp with { SchemaVersion = 2 }, CancellationToken.None); }
        catch (NotSupportedException) { unknownVersion = true; }
        assert(unknownVersion, "Checkpoint archive rejects unknown schema versions");
    }
}
