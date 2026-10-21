using Godot;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace GodotRuntimeMcp.Recording;

/// <summary>
/// Bounded, opt-in main-thread scene sampling. Immutable checkpoint I/O and
/// timeline writes are serialized off-thread; no live Node enters the worker.
/// </summary>
public sealed class RuntimeTimelineController
{
    private sealed class Session
    {
        public required TimelineSessionStatus Status;
        public required string RootPath;
        public required string SessionDirectory;
        public required bool Strict;
        public bool Busy = true;
        public bool StopRequested;
        public int PendingDrops;
        public int DroppedObserved;
        public readonly List<ObservedRuntimeEvent> PendingObserved = new();
        public long Ticks;
        public SceneCheckpoint? Previous;
    }

    private const int MaxObservedPerBatch = 256;
    private readonly Node _backend;
    private readonly Func<string?>? _instance;
    private readonly object _sync = new();
    private Session? _latest;

    public RuntimeTimelineController(Node backend, Func<string?>? instance = null)
    {
        _backend = backend;
        _instance = instance;
    }

    public TimelineSessionStatus Start(
        string? rootPath, int sampleEveryFrames = 30,
        int maxSamples = 60, bool strict = true,
        bool captureSignals = false, bool captureInput = false,
        bool captureScreenshots = false)
    {
        lock (_sync)
        {
            if (_latest is { Status.Status: "starting" or "running" or "stopping" })
                return Failure("timeline_busy", "One timeline is already being recorded");
            if (sampleEveryFrames is < 1 or > 600 || maxSamples is < 1 or > 240)
                return Failure("timeline_limit_exceeded",
                    "sampleEveryFrames must be 1..600, maxSamples must be 1..240");
            string project = Path.GetFullPath(ProjectSettings.GlobalizePath("res://"))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string projectId = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(project))).ToLowerInvariant();
            string sessionId = "rec_" + Guid.NewGuid().ToString("N");
            string config = System.Environment.GetEnvironmentVariable("GODOT_MCP_CONFIG_DIR") ??
                Path.Combine(System.Environment.GetFolderPath(
                    System.Environment.SpecialFolder.UserProfile), ".config", "godot-runtime-mcp");
            string dir = Path.Combine(config, "recordings", projectId, sessionId);
            var state = new TimelineSessionStatus(
                TimelineDeltaBuilder.SchemaVersion, sessionId, projectId,
                _instance?.Invoke() ?? "unregistered", "starting",
                sampleEveryFrames, maxSamples, 0, -1, 0, null,
                CaptureSignals: captureSignals, CaptureInput: captureInput,
                CaptureScreenshots: captureScreenshots);
            var record = new Session
            {
                Status = state, SessionDirectory = dir,
                RootPath = rootPath ?? "", Strict = strict
            };
            _latest = record;
            _ = Task.Run(async () =>
            {
                try
                {
                    await TimelineArchive.CreateAsync(
                        Path.Combine(dir, "timeline"),
                        state with { Status = "running" }, CancellationToken.None);
                    lock (_sync)
                    {
                        record.Status = state with { Status = "running" };
                        record.Busy = false;
                    }
                }
                catch (Exception ex) { Fail(record, "timeline_start_failed", ex.Message); }
            });
            return state;
        }
    }

    private static TimelineSessionStatus Failure(string code, string message) =>
        new(1, "", "", "", "failed", 30, 1, 0, -1, 0, null, code, message);

    public TimelineSessionStatus Status(string sessionId)
    {
        lock (_sync)
            return _latest is { } s && s.Status.SessionId == sessionId
                ? s.Status with { DroppedSamples = s.Status.DroppedSamples + s.PendingDrops }
                : Failure("timeline_session_not_found", "Unknown recording session");
    }

    public TimelineSessionStatus Stop(string sessionId)
    {
        lock (_sync)
        {
            if (_latest is not { } s || s.Status.SessionId != sessionId)
                return Failure("timeline_session_not_found", "Unknown recording session");
            if (s.Status.Status is "running" or "starting")
            {
                s.StopRequested = true;
                s.Status = s.Status with { Status = "stopping" };
            }
            return s.Status;
        }
    }

    private void PushObserved(string kind, string source, string name,
        Dictionary<string, SnapshotValue> fields)
    {
        lock (_sync)
        {
            var current = _latest;
            if (current is null || current.StopRequested ||
                current.Status.Status is not ("running" or "starting") ||
                (kind == "signal" ? !current.Status.CaptureSignals : !current.Status.CaptureInput))
                return;
            if (current.PendingObserved.Count == MaxObservedPerBatch)
            {
                current.DroppedObserved++;
                return;
            }
            current.PendingObserved.Add(new ObservedRuntimeEvent(kind,
                source[..Math.Min(source.Length, 512)], name[..Math.Min(name.Length, 128)],
                (long)Engine.GetProcessFrames(), (long)Engine.GetPhysicsFrames(),
                DateTimeOffset.UtcNow, fields));
        }
    }

    /// <summary>Only callback-delivered signals are captured; arbitrary signals may not be subscribed.</summary>
    public void ObserveSignal(Node node, string signalName, params Variant[] arguments)
    {
        if (!GodotObject.IsInstanceValid(node) || !node.IsInsideTree()) return;
        string path = node.GetPath().ToString();
        lock (_sync)
        {
            var current = _latest;
            if (current is null || !current.Status.CaptureSignals || current.StopRequested ||
                current.Status.Status is not ("running" or "starting")) return;
            string? boundary = string.IsNullOrEmpty(current.RootPath)
                ? _backend.GetTree().CurrentScene?.GetPath().ToString()
                : current.RootPath;
            if (string.IsNullOrEmpty(boundary) ||
                (path != boundary &&
                 !path.StartsWith(boundary.TrimEnd('/') + "/", StringComparison.Ordinal)))
                return;
        }
        var fields = new Dictionary<string, SnapshotValue>();
        for (int i = 0; i < Math.Min(arguments.Length, 4); i++)
        {
            var arg = arguments[i];
            fields["arg" + i] = arg.VariantType switch
            {
                Variant.Type.Int => new SnapshotValue("int64", JsonValue.Create(arg.AsInt64())),
                Variant.Type.Bool => new SnapshotValue("bool", JsonValue.Create(arg.AsBool())),
                Variant.Type.Float when double.IsFinite(arg.AsDouble()) =>
                    new SnapshotValue("float64", JsonValue.Create(arg.AsDouble())),
                _ => new SnapshotValue("redacted") // Never archive text/Unicode or Resource objects.
            };
        }
        if (arguments.Length > 4) fields["extraArguments"] = new SnapshotValue("redacted");
        PushObserved("signal", path, signalName, fields);
    }

    /// <summary>Capture bounded event metadata, never Unicode characters or text payloads.</summary>
    public void ObserveInput(InputEvent evt)
    {
        Dictionary<string, SnapshotValue> fields = new();
        static SnapshotValue Int(long number) => new("int64", JsonValue.Create(number));
        static SnapshotValue Bool(bool state) => new("bool", JsonValue.Create(state));
        string name;
        // Keycodes can reveal what someone typed even without storing Unicode.
        // Do not capture keys while the scene is editing text (especially secrets).
        // Other game-specific sensitive controls still require caller discretion.
        if (evt is InputEventKey &&
            _backend.GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit)
            return;
        switch (evt)
        {
            case InputEventKey key:
                name = "key";
                fields["keycode"] = Int((long)key.Keycode);
                fields["physicalKeycode"] = Int((long)key.PhysicalKeycode);
                fields["pressed"] = Bool(key.Pressed);
                fields["echo"] = Bool(key.Echo);
                // key.Unicode, label, layout/IME preedit intentionally NEVER serialized.
                break;
            case InputEventMouseButton mouse:
                name = "mouse_button";
                fields["button"] = Int((long)mouse.ButtonIndex);
                fields["pressed"] = Bool(mouse.Pressed);
                fields["x"] = Int((long)mouse.Position.X);
                fields["y"] = Int((long)mouse.Position.Y);
                break;
            case InputEventMouseMotion motion:
                name = "mouse_motion";
                fields["deltaX"] = Int((long)motion.Relative.X);
                fields["deltaY"] = Int((long)motion.Relative.Y);
                break;
            case InputEventJoypadButton joy:
                name = "joy_button";
                fields["button"] = Int((long)joy.ButtonIndex);
                fields["pressed"] = Bool(joy.Pressed);
                break;
            default:
                return; // Exclude unbounded text, unknown and synthetic payloads.
        }
        PushObserved("input", "/viewport", name, fields);
    }

    /// <summary>
    /// Called only from the game main thread. A viewport screenshot is the most
    /// recent rendered frame, not necessarily the exact instant of the scene sample.
    /// Native Image/Texture/Viewport objects never leave this thread.
    /// </summary>
    private (byte[]? Png, TimelineFrameImage? Status) CaptureViewportImage()
    {
        try
        {
            // Godot's headless dummy renderer has no texture readback; attempting
            // GetImage() produces a native error. Never fabricate a blank PNG.
            if (DisplayServer.GetName() == "headless")
                return (null, new TimelineFrameImage("unavailable", null, null,
                    0, 0, 0, "renderer_unavailable"));
            var viewport = _backend.GetViewport();
            if (viewport is null || viewport.GetTexture() is null)
                return (null, new TimelineFrameImage("unavailable", null, null,
                    0, 0, 0, "viewport_unavailable"));
            using var img = viewport.GetTexture().GetImage();
            if (img is null || img.IsEmpty())
                return (null, new TimelineFrameImage("unavailable", null, null,
                    0, 0, 0, "viewport_unavailable"));
            int width = img.GetWidth();
            int height = img.GetHeight();
            if (width < 1 || height < 1)
                throw new InvalidDataException("empty_frame");
            if (width > TimelineArchive.MaxDimension || height > TimelineArchive.MaxDimension)
            {
                double ratio = Math.Min((double)TimelineArchive.MaxDimension / width,
                    (double)TimelineArchive.MaxDimension / height);
                img.Resize(Math.Max(1, (int)Math.Floor(width * ratio)),
                    Math.Max(1, (int)Math.Floor(height * ratio)), Image.Interpolation.Lanczos);
            }
            var png = img.SavePngToBuffer();
            if (png.Length is < 33 or > TimelineArchive.MaxPngBytes)
                return (null, new TimelineFrameImage("unavailable", null, null,
                    0, 0, 0, "image_byte_limit"));
            return (png, null);
        }
        catch (Exception)
        {
            return (null, new TimelineFrameImage("unavailable", null, null,
                0, 0, 0, "capture_failed"));
        }
    }

    public void Pump()
    {
        Session? session;
        bool finish = false;
        lock (_sync)
        {
            session = _latest;
            if (session is null) return;
            if (session.Busy)
            {
                if (session.Status.Status == "running" &&
                    ++session.Ticks % session.Status.SampleEveryFrames == 0)
                    session.PendingDrops++;
                return;
            }
            if (session.StopRequested)
            {
                session.Busy = true;
                finish = session.PendingObserved.Count == 0 ||
                    session.Status.SampleCount >= session.Status.MaxSamples;
            }
            else
            {
                if (session.Status.Status != "running") return;
                session.Ticks++;
                if (session.Ticks % session.Status.SampleEveryFrames != 0) return;
                session.Busy = true;
            }
        }
        if (finish)
        {
            Complete(session, "stopped");
            return;
        }

        try
        {
            Node? root = string.IsNullOrWhiteSpace(session.RootPath)
                ? _backend.GetTree().CurrentScene
                : _backend.GetNodeOrNull(new NodePath(session.RootPath));
            if (root is null)
                throw new InvalidDataException("scene_unavailable");
            var state = session.Status;
            string snapshotId = "snap_" + Guid.NewGuid().ToString("N");
            var snapshot = SceneCheckpointCapture.Capture(root, snapshotId,
                state.SessionId, state.ProjectId, state.InstanceId, session.Strict);
            var batch = session.Previous is null
                ? new TimelineBatch(1, state.SessionId, 0, snapshotId,
                    snapshotId, snapshot.EndedProcessFrame,
                    new List<TimelineEvent>(), session.PendingDrops)
                : TimelineDeltaBuilder.Compare(session.Previous, snapshot,
                    state.SampleCount, snapshot.EndedProcessFrame);
            var screenshot = session.Status.CaptureScreenshots
                ? CaptureViewportImage()
                : (Png: (byte[]?)null, Status: (TimelineFrameImage?)null);
            lock (_sync)
            {
                batch = batch with {
                    DroppedSamples = session.PendingDrops,
                    ObservedEvents = session.PendingObserved.ToList(),
                    DroppedObservedEvents = session.DroppedObserved
                };
                session.PendingObserved.Clear();
                session.DroppedObserved = 0;
                session.PendingDrops = 0;
            }
            _ = Task.Run(async () =>
            {
                try
                {
                    await CheckpointArchive.CommitAsync(
                        Path.Combine(session.SessionDirectory, snapshotId),
                        snapshot, CancellationToken.None);
                    TimelineFrameImage? frame = screenshot.Status;
                    if (screenshot.Png is not null)
                    {
                        try
                        {
                            frame = await TimelineArchive.WriteImageAsync(
                                Path.Combine(session.SessionDirectory, "timeline"),
                                batch.SampleIndex, screenshot.Png, CancellationToken.None);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            frame = new TimelineFrameImage("unavailable", null, null,
                                0, 0, 0, "image_write_failed");
                        }
                    }
                    batch = batch with { Screenshot = frame };
                    var saved = await TimelineArchive.AppendAsync(
                        Path.Combine(session.SessionDirectory, "timeline"),
                        batch, CancellationToken.None);
                    bool complete;
                    lock (_sync)
                    {
                        session.Previous = snapshot;
                        session.Status = saved;
                        complete = saved.SampleCount >= saved.MaxSamples || session.StopRequested;
                        session.Busy = complete;
                    }
                    if (complete)
                        await FinishAsync(session, session.StopRequested ? "stopped" : "completed");
                }
                catch (Exception ex) { await FailAsync(session, "timeline_write_failed", ex.Message); }
            });
        }
        catch (Exception ex)
        {
            _ = Task.Run(() => FailAsync(session, "timeline_capture_failed", ex.Message));
        }
    }

    private void Complete(Session session, string state) =>
        _ = Task.Run(() => FinishAsync(session, state));

    private async Task FinishAsync(Session session, string state)
    {
        try
        {
            int dropped;
            int skipped;
            lock (_sync)
            {
                dropped = session.PendingObserved.Count + session.DroppedObserved;
                skipped = session.PendingDrops;
                session.PendingObserved.Clear();
                session.DroppedObserved = 0;
                session.PendingDrops = 0;
            }
            var finished = await TimelineArchive.CompleteAsync(
                Path.Combine(session.SessionDirectory, "timeline"),
                state, CancellationToken.None, skippedSamples: skipped,
                skippedObservedEvents: dropped);
            lock (_sync) { session.Status = finished; session.Busy = false; }
        }
        catch (Exception ex) { Fail(session, "timeline_stop_failed", ex.Message); }
    }

    private async Task FailAsync(Session session, string code, string message)
    {
        try
        {
            var failed = await TimelineArchive.CompleteAsync(
                Path.Combine(session.SessionDirectory, "timeline"),
                "failed", CancellationToken.None, code, message);
            lock (_sync) { session.Status = failed; session.Busy = false; }
        }
        catch { Fail(session, code, message); }
    }

    private void Fail(Session session, string code, string message)
    {
        lock (_sync)
        {
            session.Status = session.Status with
            { Status = "failed", Code = code, Message = message };
            session.Busy = false;
        }
    }
}
