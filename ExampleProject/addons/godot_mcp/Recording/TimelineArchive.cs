using System.Buffers.Binary;
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
    public const int MaxPngBytes = 512 * 1024;
    public const int MaxDimension = 1024;
    public const long MaxSessionImageBytes = 32L * 1024 * 1024;
    private static readonly byte[] PngMagic = [137, 80, 78, 71, 13, 10, 26, 10];

    private static (int Width, int Height) PngDimensions(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 33 || !bytes[..8].SequenceEqual(PngMagic) ||
            BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(8, 4)) != 13 ||
            !bytes.Slice(12, 4).SequenceEqual("IHDR"u8))
            throw new InvalidDataException("timeline_image_corrupt: invalid PNG header");
        int width = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(16, 4));
        int height = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(20, 4));
        if (width is < 1 or > MaxDimension || height is < 1 or > MaxDimension)
            throw new InvalidDataException("timeline_image_limit: excessive screenshot dimensions");
        return (width, height);
    }

    /// <summary>Atomic owner-private PNG write; called before publishing the batch marker.</summary>
    public static async Task<TimelineFrameImage> WriteImageAsync(
        string directory, int sampleIndex, byte[] png, CancellationToken ct)
    {
        if (sampleIndex is < 0 or >= 1000 || png.Length > MaxPngBytes || png.Length == 0)
            throw new InvalidDataException("timeline_image_limit: invalid image index/size");
        var (width, height) = PngDimensions(png);
        string framesDir = Path.Combine(directory, "frames");
        if (Directory.Exists(framesDir) && new DirectoryInfo(framesDir).LinkTarget is not null)
            throw new InvalidDataException("timeline_image_corrupt: symlink frames directory");
        Directory.CreateDirectory(framesDir);
        Secure(framesDir, true);
        string filename = sampleIndex.ToString("D6") + ".png";
        string dest = Path.Combine(framesDir, filename);
        if (File.Exists(dest) || new FileInfo(dest).LinkTarget is not null)
            throw new IOException("timeline_image_already_exists");
        long used = Directory.EnumerateFiles(framesDir, "*.png", SearchOption.TopDirectoryOnly)
            .Sum(path => new FileInfo(path).Length);
        if (used + png.Length > MaxSessionImageBytes)
            throw new InvalidDataException("timeline_image_budget_exceeded");
        string temp = dest + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temp, png, ct);
            Secure(temp, false);
            File.Move(temp, dest);
            string hash = Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant();
            return new TimelineFrameImage("captured", filename, hash,
                width, height, png.Length);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    /// <summary>Never trust disk filenames or PNG bytes without batch-bound hash verification.</summary>
    public static byte[] ReadImage(string directory, TimelineBatch batch)
    {
        var image = batch.Screenshot;
        string expected = batch.SampleIndex.ToString("D6") + ".png";
        if (image is null || image.State != "captured" || image.FileName != expected ||
            image.Sha256 is null || image.Sha256.Length != 64 ||
            image.ByteLength is < 33 or > MaxPngBytes)
            throw new InvalidDataException("timeline_image_corrupt: invalid screenshot metadata");
        string frames = Path.Combine(directory, "frames");
        if (!Directory.Exists(frames) || new DirectoryInfo(frames).LinkTarget is not null)
            throw new InvalidDataException("timeline_image_corrupt: missing/private frame directory");
        string path = Path.Combine(frames, expected);
        if (!File.Exists(path) || new FileInfo(path).LinkTarget is not null)
            throw new InvalidDataException("timeline_image_corrupt: missing or symlink screenshot");
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length != image.ByteLength || bytes.Length > MaxPngBytes ||
            !string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), image.Sha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("timeline_image_corrupt: screenshot hash mismatch");
        var (width, height) = PngDimensions(bytes);
        if (width != image.Width || height != image.Height)
            throw new InvalidDataException("timeline_image_corrupt: screenshot size mismatch");
        return bytes;
    }


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

        if (batch.Screenshot is { State: "captured" })
            _ = ReadImage(directory, batch);
        else if (batch.Screenshot is { State: not "unavailable" })
            throw new InvalidDataException("timeline_image_corrupt: invalid screenshot state");
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
                DroppedObservedEvents = before.DroppedObservedEvents + batch.DroppedObservedEvents,
                ScreenshotCount = before.ScreenshotCount + (batch.Screenshot?.State == "captured" ? 1 : 0),
                SkippedScreenshots = before.SkippedScreenshots + (batch.Screenshot?.State == "unavailable" ? 1 : 0)
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
                if (batch.Screenshot is { State: "captured" })
                    _ = ReadImage(directory, batch);
                else if (batch.Screenshot is { State: not "unavailable" })
                    throw new InvalidDataException("timeline_image_corrupt: unsupported screenshot state");
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
