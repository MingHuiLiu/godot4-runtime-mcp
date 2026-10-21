using Godot;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GodotRuntimeMcp.Recording;

/// <summary>
/// Opt-in *observed-state* input replay in a dedicated trusted Godot process.
/// It never accesses the user's live game process. Other game Autoloads can still
/// run, so the worker is NOT a security sandbox or deterministic physics VM.
/// </summary>
public partial class ReplayWorker : Node
{
    private ReplayRequest? _request;
    private ReplayPlan? _plan;
    private Node? _root;
    private string _reportPath = "";
    private int _index = 1;
    private int _stepFrames;
    private bool _awaitCompare;
    private int _compared;
    private int _matched;
    private int _injected;
    private int _ticks;
    private readonly List<ReplayIssue> _issues = new();

    public override void _Ready()
    {
        try
        {
            string[] args = OS.GetCmdlineUserArgs();
            int option = Array.IndexOf(args, "--replay-job");
            if (option < 0 || option + 1 >= args.Length)
                throw new ArgumentException("replay_job_missing");
            _request = JsonSerializer.Deserialize(File.ReadAllText(args[option + 1]),
                SnapshotJsonContext.Default.ReplayRequest)
                ?? throw new InvalidDataException("replay_invalid_job");
            string configured = System.Environment.GetEnvironmentVariable("GODOT_MCP_CONFIG_DIR")
                ?? Path.Combine(System.Environment.GetFolderPath(
                    System.Environment.SpecialFolder.UserProfile), ".config", "godot-runtime-mcp");
            string configRoot = Path.GetFullPath(configured).TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string project = Path.GetFullPath(ProjectSettings.GlobalizePath("res://"))
                .TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(project, Path.GetFullPath(_request.ProjectPath)
                .TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal))
                throw new UnauthorizedAccessException("replay_project_mismatch");
            string projectId = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(project))).ToLowerInvariant();
            string recordings = Path.Combine(configRoot, "recordings", projectId)
                + Path.DirectorySeparatorChar;
            string sessionDir = Path.GetFullPath(_request.SessionDirectory);
            string output = Path.GetFullPath(_request.OutputDirectory);
            if (!sessionDir.StartsWith(recordings, StringComparison.Ordinal) ||
                !output.StartsWith(configRoot, StringComparison.Ordinal) ||
                !Path.IsPathFullyQualified(_request.SessionDirectory) ||
                !Path.IsPathFullyQualified(_request.OutputDirectory))
                throw new UnauthorizedAccessException("unsafe_output_path");
            if (Path.GetFileName(sessionDir).Contains('.') ||
                new DirectoryInfo(sessionDir).LinkTarget is not null ||
                Directory.Exists(output) && new DirectoryInfo(output).LinkTarget is not null)
                throw new UnauthorizedAccessException("unsafe_output_path");
            Directory.CreateDirectory(output);
            _reportPath = Path.Combine(output, "replay_report.json");
            var timelineDir = Path.Combine(sessionDir, "timeline");
            var status = TimelineArchive.ReadStatus(timelineDir);
            if (status.ProjectId != projectId ||
                status.SessionId != Path.GetFileName(sessionDir))
                throw new UnauthorizedAccessException("replay_project_mismatch");
            var page = TimelineArchive.ReadPage(timelineDir, 0, 60);
            try { _plan = ReplayPlanBuilder.Build(status, page.Items); }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
            {
                Finish("inconclusive", "replay_incomplete", ex.Message);
                return;
            }

            var baseline = LoadSnapshot(sessionDir, _plan.Steps[0].SnapshotId);
            _root = SnapshotSceneBuilder.Build(baseline, strict: true).Root;
            // A restored script is attached to a fresh scene tree. Godot dispatches
            // Input.ParseInputEvent callbacks normally to this subtree.
            AddChild(_root);
            var initialActual = Capture();
            var initialIssues = ReplayStateComparer.Compare(baseline, initialActual);
            if (initialIssues.Count != 0)
            {
                _issues.AddRange(initialIssues);
                Finish("diverged", "replay_baseline_diverged",
                    "The reconstructed scene did not match its baseline before any input");
                return;
            }
            GD.Print("REPLAY_WORKER_START: " + _plan.SessionId +
                " checkpoints=" + _plan.Steps.Count);
        }
        catch (Exception ex)
        {
            Finish("failed", "replay_start_failed", ex.Message);
        }
    }

    public override void _Process(double delta)
    {
        if (_plan is null || _request is null || _root is null) return;
        if (++_ticks > 1500)
        {
            Finish("inconclusive", "replay_timeout", "Too many engine frames");
            return;
        }
        try
        {
            if (!_awaitCompare)
            {
                // Preserve the recorded *relative process-frame spacing* of each
                // input event. This improves repeatability but cannot control
                // physics, timing of other Autoloads, RNG or external I/O.
                _stepFrames++;
                long from = _plan.Steps[_index - 1].ProcessFrame;
                long replayedFrame = from + _stepFrames;
                foreach (var observed in _plan.Steps[_index].Events.Where(e =>
                    e.ProcessFrame == replayedFrame))
                {
                    using var input = BuildInput(observed);
                    Input.ParseInputEvent(input);
                    _injected++;
                }
                if (replayedFrame >= _plan.Steps[_index].ProcessFrame)
                    _awaitCompare = true;
                return;
            }

            var expected = LoadSnapshot(_request.SessionDirectory,
                _plan.Steps[_index].SnapshotId);
            var actual = Capture();
            var difference = ReplayStateComparer.Compare(expected, actual);
            _compared++;
            if (difference.Count != 0)
            {
                _issues.AddRange(difference);
                Finish("diverged", "replay_state_diverged",
                    "Serialized scene state differs after injected input");
                return;
            }
            _matched++;
            _index++;
            _stepFrames = 0;
            _awaitCompare = false;
            if (_index == _plan.Steps.Count)
                Finish("matched", "matched_observed_state",
                    "All supported exported scene properties matched at sample boundaries");
        }
        catch (Exception ex)
        {
            Finish("failed", "replay_worker_failed", ex.Message);
        }
    }

    private SceneCheckpoint Capture()
    {
        var p = _plan ?? throw new InvalidOperationException("No active replay plan");
        if (_root is null) throw new InvalidOperationException("No reconstructed root");
        return SceneCheckpointCapture.Capture(_root, "snap_replayed",
            p.SessionId, p.ProjectId, p.InstanceId, strict: true);
    }

    private static SceneCheckpoint LoadSnapshot(string sessionDirectory, string id)
    {
        if (id.Length is < 3 or > 100 ||
            !id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
            throw new InvalidDataException("replay_incomplete: unsafe snapshot ID");
        string path = Path.Combine(sessionDirectory, id);
        if (!Directory.Exists(path) || new DirectoryInfo(path).LinkTarget is not null)
            throw new InvalidDataException("replay_incomplete: snapshot not found");
        // Off-main BCL archive read avoids Godot's synchronization context deadlock.
        return Task.Run(() => CheckpointArchive.ReadAsync(path, CancellationToken.None))
            .GetAwaiter().GetResult();
    }

    private static InputEvent BuildInput(ObservedRuntimeEvent value)
    {
        long GetInt(string key) =>
            value.Fields.TryGetValue(key, out var v) && v.Data is not null
            ? long.Parse(v.Data.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture)
            : 0;
        bool GetBool(string key) =>
            value.Fields.TryGetValue(key, out var v) && v.Data is not null &&
            v.Data.GetValue<bool>();
        return value.Name switch
        {
            "key" => new InputEventKey
            {
                Keycode = (Key)GetInt("keycode"),
                PhysicalKeycode = (Key)GetInt("physicalKeycode"),
                Pressed = GetBool("pressed"),
                Echo = GetBool("echo")
                // Unicode/IME intentionally unrecoverable.
            },
            "mouse_button" => new InputEventMouseButton
            {
                ButtonIndex = (MouseButton)GetInt("button"),
                Pressed = GetBool("pressed")
            },
            "joy_button" => new InputEventJoypadButton
            {
                ButtonIndex = (JoyButton)GetInt("button"),
                Pressed = GetBool("pressed")
            },
            _ => throw new NotSupportedException("replay_unsupported_event")
        };
    }

    private void Finish(string status, string code, string message)
    {
        if (_reportPath.Length > 0)
        {
            var report = new ReplayReport(status, code, message,
                _plan?.SessionId ?? "", _compared, _matched, _injected,
                _issues.ToList(), _reportPath);
            try
            {
                string temp = _reportPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(report,
                    SnapshotJsonContext.Default.ReplayReport));
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                File.Move(temp, _reportPath, true);
            }
            catch (Exception ex) { GD.PushError("REPLAY_REPORT_WRITE_FAILED: " + ex); }
        }
        if (_root is not null && GodotObject.IsInstanceValid(_root))
            _root.Free();
        _root = null;
        _plan = null;
        GD.Print("REPLAY_WORKER_RESULT: " + status + " " + code);
        GetTree().Quit(status == "matched" ? 0 : 1);
    }
}
