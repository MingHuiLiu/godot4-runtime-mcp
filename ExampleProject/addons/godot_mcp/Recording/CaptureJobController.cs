using Godot;
using System.Security.Cryptography;
using System.Text;

namespace GodotRuntimeMcp.Recording;

/// <summary>
/// Main-thread capture scheduler. The writer only sees pure CLR records, never live Godot objects.
/// </summary>
public sealed class CaptureJobController
{
    private sealed class Job
    {
        public required string Id;
        public required string SnapshotId;
        public required string SessionId;
        public required bool Strict;
        public required int MaxNodes;
        public string? RootPath;
        public string Status = "pending";
        public string? Code;
        public string? Message;
    }

    private readonly Node _backend;
    private readonly object _sync = new();
    private Job? _latest;

    public CaptureJobController(Node backend) => _backend = backend;

    public SnapshotJobInfo Start(string? rootPath, bool strict, int maxNodes = 2000)
    {
        lock (_sync)
        {
            if (maxNodes < 1 || maxNodes > 2000)
                return new SnapshotJobInfo("", "failed", Code: "capture_limit_exceeded",
                    Message: "maxNodes must be between 1 and 2000");
            if (_latest is { Status: "pending" or "writing" or "capturing" })
                return new SnapshotJobInfo("", "failed", Code: "snapshot_busy",
                    Message: "A capture is already running");
            var job = new Job
            {
                Id = "job_" + Guid.NewGuid().ToString("N"),
                SnapshotId = "snap_" + Guid.NewGuid().ToString("N"),
                SessionId = "session_" + Guid.NewGuid().ToString("N"),
                RootPath = rootPath,
                Strict = strict,
                MaxNodes = maxNodes
            };
            _latest = job;
            return Info(job);
        }
    }

    public SnapshotJobInfo GetStatus(string jobId)
    {
        lock (_sync)
        {
            if (_latest is null || _latest.Id != jobId)
                return new SnapshotJobInfo(jobId, "failed", Code: "snapshot_job_not_found",
                    Message: "Unknown or expired capture job");
            return Info(_latest);
        }
    }

    public void Pump()
    {
        Job? job;
        lock (_sync)
        {
            job = _latest;
            if (job is null || job.Status != "pending") return;
            job.Status = "capturing";
        }

        try
        {
            Node? root = string.IsNullOrWhiteSpace(job.RootPath)
                ? _backend.GetTree().CurrentScene
                : _backend.GetNodeOrNull(new NodePath(job.RootPath));
            if (root is null)
            {
                Fail(job, "scene_unavailable", "No current scene or the selected root path does not exist");
                return;
            }

            string projectPath = Path.GetFullPath(ProjectSettings.GlobalizePath("res://"))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string projectId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(projectPath)))
                .ToLowerInvariant();
            var snapshot = SceneCheckpointCapture.Capture(root, job.SnapshotId,
                job.SessionId, projectId, Guid.NewGuid().ToString("N"), job.Strict);
            if (snapshot.Nodes.Count > job.MaxNodes)
            {
                Fail(job, "capture_limit_exceeded", "Captured node count exceeds requested maxNodes");
                return;
            }

            var config = System.Environment.GetEnvironmentVariable("GODOT_MCP_CONFIG_DIR");
            if (string.IsNullOrWhiteSpace(config))
                config = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
                    ".config", "godot-runtime-mcp");
            string directory = Path.Combine(config, "recordings", projectId,
                job.SessionId, job.SnapshotId);
            lock (_sync) job.Status = "writing";
            // Snapshot is detached from Godot objects. Only file compression/serialization runs off-thread.
            _ = Task.Run(async () =>
            {
                try
                {
                    await CheckpointArchive.CommitAsync(directory, snapshot, CancellationToken.None);
                    lock (_sync) job.Status = "completed";
                }
                catch (Exception e)
                {
                    Fail(job, "snapshot_write_failed", e.Message);
                }
            });
        }
        catch (InvalidDataException e)
        {
            Fail(job, "capture_limit_exceeded", e.Message);
        }
        catch (Exception e)
        {
            Fail(job, "snapshot_capture_failed", e.Message);
        }
    }

    private void Fail(Job job, string code, string message)
    {
        lock (_sync)
        {
            job.Code = code;
            job.Message = message;
            job.Status = "failed";
        }
    }

    private static SnapshotJobInfo Info(Job job) =>
        new(job.Id, job.Status, job.SnapshotId, job.Code, job.Message);
}
