using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GodotRuntimeMcp.Recording;

/// <summary>
/// On-disk checkpoint format. The manifest is published only after every payload is durable.
/// This class deliberately has no Godot dependency so the host can inspect offline captures.
/// </summary>
public static class CheckpointArchive
{
    private const long MaxExpandedBytes = 64L * 1024 * 1024;
    private static readonly string[] PayloadFiles =
        ["nodes.jsonl.gz", "resources.jsonl.gz", "state.json", "issues.json", "checksums.json"];

    public static async Task<CheckpointManifest> CommitAsync(
        string snapshotDirectory, SceneCheckpoint checkpoint, CancellationToken ct)
    {
        SnapshotSchema.Validate(checkpoint);
        Directory.CreateDirectory(snapshotDirectory);
        string staging = Path.Combine(snapshotDirectory, ".stage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            await WriteJsonLinesAsync(Path.Combine(staging, "nodes.jsonl.gz"),
                checkpoint.Nodes.Select(n => JsonSerializer.Serialize(n, SnapshotJsonContext.Default.RecordedNode)), ct);
            await WriteJsonLinesAsync(Path.Combine(staging, "resources.jsonl.gz"),
                checkpoint.Resources.Select(n => JsonSerializer.Serialize(n, SnapshotJsonContext.Default.RecordedResource)), ct);
            string state = JsonSerializer.Serialize(checkpoint, SnapshotJsonContext.Default.SceneCheckpoint);
            await File.WriteAllTextAsync(Path.Combine(staging, "state.json"), state, ct);
            string issues = JsonSerializer.Serialize(checkpoint.Issues, SnapshotJsonContext.Default.ListSnapshotIssue);
            await File.WriteAllTextAsync(Path.Combine(staging, "issues.json"), issues, ct);
            var hashes = new Dictionary<string, string>();
            foreach (string name in PayloadFiles.Where(n => n != "checksums.json"))
                hashes[name] = await HashAsync(Path.Combine(staging, name), ct);
            string checksums = JsonSerializer.Serialize(hashes, SnapshotJsonContext.Default.DictionaryStringString);
            await File.WriteAllTextAsync(Path.Combine(staging, "checksums.json"), checksums, ct);
            hashes["checksums.json"] = await HashAsync(Path.Combine(staging, "checksums.json"), ct);
            var manifest = new CheckpointManifest(
                checkpoint.SchemaVersion, checkpoint.SnapshotId, checkpoint.SessionId,
                checkpoint.ProjectId, checkpoint.InstanceId, "completed",
                checkpoint.Issues.Count == 0 ? "checkpoint_exact" : "partial",
                checkpoint.Nodes.Count, checkpoint.Resources.Count, DateTimeOffset.UtcNow, hashes);
            await File.WriteAllTextAsync(Path.Combine(staging, "manifest.json"),
                JsonSerializer.Serialize(manifest, SnapshotJsonContext.Default.CheckpointManifest), ct);
            foreach (string name in PayloadFiles)
                File.Move(Path.Combine(staging, name), Path.Combine(snapshotDirectory, name), true);
            // Publishing the manifest is the commit marker; incomplete writes are never readable.
            File.Move(Path.Combine(staging, "manifest.json"), Path.Combine(snapshotDirectory, "manifest.json"), false);
            return manifest;
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, true);
        }
    }

    public static CheckpointManifest ReadManifest(string snapshotDirectory)
    {
        string path = Path.Combine(snapshotDirectory, "manifest.json");
        if (!File.Exists(path))
            throw new InvalidDataException("snapshot_incomplete: manifest missing");
        try
        {
            var value = JsonSerializer.Deserialize(
                File.ReadAllText(path), SnapshotJsonContext.Default.CheckpointManifest);
            if (value is null || value.Status != "completed")
                throw new InvalidDataException("snapshot_incomplete: uncommitted manifest");
            if (value.SchemaVersion != SnapshotSchema.CurrentVersion)
                throw new NotSupportedException("unsupported_schema_version");
            return value;
        }
        catch (JsonException e)
        {
            throw new InvalidDataException("snapshot_corrupt: invalid manifest", e);
        }
    }

    public static async Task<SceneCheckpoint> ReadAsync(string snapshotDirectory, CancellationToken ct)
    {
        var manifest = ReadManifest(snapshotDirectory);
        foreach (var (name, expected) in manifest.FileHashes)
        {
            if (!PayloadFiles.Contains(name) || !File.Exists(Path.Combine(snapshotDirectory, name)))
                throw new InvalidDataException("snapshot_corrupt: missing or unknown payload");
            string actual = await HashAsync(Path.Combine(snapshotDirectory, name), ct);
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("snapshot_corrupt: payload checksum mismatch");
        }
        if (manifest.FileHashes.Count != PayloadFiles.Length)
            throw new InvalidDataException("snapshot_corrupt: incomplete checksums");
        try
        {
            // Verify that compressed streams are readable and bounded, not only that bytes match a hash.
            long expanded = 0;
            foreach (string name in new[] { "nodes.jsonl.gz", "resources.jsonl.gz" })
            {
                await using var file = File.OpenRead(Path.Combine(snapshotDirectory, name));
                await using var gzip = new GZipStream(file, CompressionMode.Decompress);
                byte[] buffer = new byte[8192];
                int count;
                while ((count = await gzip.ReadAsync(buffer, ct)) != 0)
                {
                    expanded += count;
                    if (expanded > MaxExpandedBytes)
                        throw new InvalidDataException("snapshot_corrupt: decompressed payload too large");
                }
            }
            string statePath = Path.Combine(snapshotDirectory, "state.json");
            if (new FileInfo(statePath).Length > MaxExpandedBytes)
                throw new InvalidDataException("snapshot_corrupt: state too large");
            var result = JsonSerializer.Deserialize(
                await File.ReadAllTextAsync(statePath, ct), SnapshotJsonContext.Default.SceneCheckpoint);
            if (result is null || result.SnapshotId != manifest.SnapshotId ||
                result.Nodes.Count != manifest.NodeCount || result.Resources.Count != manifest.ResourceCount)
                throw new InvalidDataException("snapshot_corrupt: state inconsistent with manifest");
            SnapshotSchema.Validate(result);
            return result;
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            throw new InvalidDataException("snapshot_corrupt: invalid payload", e);
        }
    }

    private static async Task WriteJsonLinesAsync(
        string path, IEnumerable<string> lines, CancellationToken ct)
    {
        await using var file = File.Create(path);
        await using var gzip = new GZipStream(file, CompressionLevel.Fastest, leaveOpen: false);
        await using var writer = new StreamWriter(gzip, Encoding.UTF8, bufferSize: 1024, leaveOpen: false);
        foreach (string line in lines)
        {
            ct.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(line.AsMemory(), ct);
        }
    }

    private static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        await using var file = File.OpenRead(path);
        byte[] hash = await SHA256.HashDataAsync(file, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
