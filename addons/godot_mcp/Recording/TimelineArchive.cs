using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GodotRuntimeMcp.Recording;

/// <summary>
/// Crash-safe, append-only observed-frame timeline. Checkpoint bytes live alongside
/// this directory in the same per-project session and are committed first.
/// </summary>
public static class TimelineArchive
{
    private const int MaxBatchBytes = 2 * 1024 * 1024;

    private static void Validate(TimelineSessionStatus value)
    {
        if (value.SchemaVersion != TimelineDeltaBuilder.SchemaVersion)
            throw new NotSupportedException("unsupported_timeline_schema");
        if (value.SampleEveryFrames < 1 || value.MaxSamples is < 1 or > 1000 ||
            value.SampleCount < 0 || value.SampleCount > value.MaxSamples)
            throw new InvalidDataException("timeline_limit_exceeded");
    }

    private static void Secure(string path, bool directory)
    {
        if (OperatingSystem.IsWindows()) return;
        File.SetUnixFileMode(path, directory
            ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            : UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public static async Task CreateAsync(
        string directory, TimelineSessionStatus initial, CancellationToken ct)
    {
        Validate(initial);
        if (initial.SampleCount != 0 || initial.LastCommittedIndex != -1 ||
            initial.Status != "running")
            throw new InvalidDataException("timeline_invalid_initial_state");
        if (Directory.Exists(directory))
            throw new IOException("timeline_already_exists");
        Directory.CreateDirectory(Path.Combine(directory, "batches"));
        Secure(directory, true);
        Secure(Path.Combine(directory, "batches"), true);
        await PublishStatusAsync(directory, initial, ct);
    }

    public static TimelineSessionStatus ReadStatus(string directory)
    {
        string path = Path.Combine(directory, "status.json");
        if (!File.Exists(path))
            throw new InvalidDataException("timeline_incomplete: status missing");
        try
        {
            var status = JsonSerializer.Deserialize(
                File.ReadAllText(path), SnapshotJsonContext.Default.TimelineSessionStatus)
                ?? throw new InvalidDataException("timeline_corrupt: empty session status");
            Validate(status);
            return status;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("timeline_corrupt: invalid status JSON", ex);
        }
    }

    public static async Task<TimelineSessionStatus> AppendAsync(
        string directory, TimelineBatch batch, CancellationToken ct)
    {
        var before = ReadStatus(directory);
        if (before.Status != "running" || batch.SchemaVersion != TimelineDeltaBuilder.SchemaVersion ||
            batch.SessionId != before.SessionId || batch.SampleIndex != before.LastCommittedIndex + 1 ||
            before.SampleCount >= before.MaxSamples)
            throw new InvalidDataException("timeline_sequence_invalid");

        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            batch, SnapshotJsonContext.Default.TimelineBatch));
        if (bytes.Length > MaxBatchBytes)
            throw new InvalidDataException("timeline_batch_limit_exceeded");
        string batches = Path.Combine(directory, "batches");
        string filename = batch.SampleIndex.ToString("D6");
        string dataPath = Path.Combine(batches, filename + ".json");
        string hashPath = Path.Combine(batches, filename + ".sha256");
        if (File.Exists(dataPath) || File.Exists(hashPath))
            throw new IOException("timeline_already_exists: sequence files exist");
        string temporary = dataPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, ct);
            Secure(temporary, false);
            File.Move(temporary, dataPath);
            string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            await File.WriteAllTextAsync(hashPath, hash, ct);
            Secure(hashPath, false);
            // The status is the final commit marker. Orphan batches after a crash
            // are never visible through ReadPage.
            var next = before with
            {
                SampleCount = before.SampleCount + 1,
                LastCommittedIndex = batch.SampleIndex,
                LastSnapshotId = batch.ToSnapshotId,
                DroppedSamples = before.DroppedSamples + batch.DroppedSamples,
                DroppedObservedEvents = before.DroppedObservedEvents + batch.DroppedObservedEvents
            };
            await PublishStatusAsync(directory, next, ct);
            return next;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static async Task<TimelineSessionStatus> CompleteAsync(
        string directory, string terminalStatus, CancellationToken ct,
        string? code = null, string? message = null, int skippedSamples = 0,
        int skippedObservedEvents = 0)
    {
        if (terminalStatus is not ("stopped" or "completed" or "failed"))
            throw new ArgumentException("timeline_invalid_terminal_status");
        var previous = ReadStatus(directory);
        if (previous.Status != "running")
            throw new InvalidDataException("timeline_terminal_state: session is no longer running");
        var next = previous with { Status = terminalStatus, Code = code, Message = message,
            DroppedSamples = previous.DroppedSamples + Math.Max(0, skippedSamples),
            DroppedObservedEvents = previous.DroppedObservedEvents + Math.Max(0, skippedObservedEvents) };
        await PublishStatusAsync(directory, next, ct);
        return next;
    }

    public static TimelineEventPage ReadPage(string directory, int offset, int limit)
    {
        if (offset < 0 || limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(limit));
        var status = ReadStatus(directory);
        var rows = new List<TimelineBatch>();
        for (int index = offset; index < Math.Min((long)status.SampleCount, (long)offset + limit); index++)
        {
            string name = index.ToString("D6");
            string dataPath = Path.Combine(directory, "batches", name + ".json");
            string hashPath = Path.Combine(directory, "batches", name + ".sha256");
            if (!File.Exists(dataPath) || !File.Exists(hashPath))
                throw new InvalidDataException("timeline_corrupt: committed sample missing");
            byte[] bytes = File.ReadAllBytes(dataPath);
            if (bytes.Length > MaxBatchBytes ||
                !string.Equals(Convert.ToHexString(SHA256.HashData(bytes)),
                    File.ReadAllText(hashPath).Trim(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("timeline_corrupt: batch checksum mismatch");
            try
            {
                var batch = JsonSerializer.Deserialize(bytes, SnapshotJsonContext.Default.TimelineBatch);
                if (batch is null || batch.SampleIndex != index ||
                    batch.SessionId != status.SessionId ||
                    batch.SchemaVersion != TimelineDeltaBuilder.SchemaVersion)
                    throw new InvalidDataException("timeline_corrupt: batch metadata mismatch");
                rows.Add(batch);
            }
            catch (JsonException e)
            {
                throw new InvalidDataException("timeline_corrupt: invalid event data", e);
            }
        }
        return new TimelineEventPage(status.SessionId, status.SampleCount, offset, limit, rows);
    }

    private static async Task PublishStatusAsync(
        string directory, TimelineSessionStatus value, CancellationToken ct)
    {
        string dest = Path.Combine(directory, "status.json");
        string temp = dest + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(
                value, SnapshotJsonContext.Default.TimelineSessionStatus), ct);
            Secure(temp, false);
            File.Move(temp, dest, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
