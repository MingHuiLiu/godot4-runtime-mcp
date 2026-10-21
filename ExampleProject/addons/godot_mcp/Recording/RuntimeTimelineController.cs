using Godot;
using System.Security.Cryptography;
using System.Text;

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
        public long Ticks;
        public SceneCheckpoint? Previous;
    }

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
        int maxSamples = 60, bool strict = true)
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
                sampleEveryFrames, maxSamples, 0, -1, 0, null);
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
                finish = true;
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
            batch = batch with { DroppedSamples = session.PendingDrops };
            session.PendingDrops = 0;
            _ = Task.Run(async () =>
            {
                try
                {
                    await CheckpointArchive.CommitAsync(
                        Path.Combine(session.SessionDirectory, snapshotId),
                        snapshot, CancellationToken.None);
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
            var finished = await TimelineArchive.CompleteAsync(
                Path.Combine(session.SessionDirectory, "timeline"),
                state, CancellationToken.None, skippedSamples: session.PendingDrops);
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
