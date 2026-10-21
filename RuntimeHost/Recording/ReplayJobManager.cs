using GodotRuntimeMcp.Recording;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GodotRuntimeMcp;

/// <summary>
/// Starts a bounded, trusted independent Godot Mono replay worker and reports
/// observable state comparison (not deterministic process reproduction).
/// </summary>
public sealed class ReplayJobManager
{
    private static readonly Regex SafeId = new(
        "^[A-Za-z0-9_-]{1,100}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly SemaphoreSlim Slots = new(2, 2);
    private const string Worker = "res://addons/godot_mcp/Recording/replay_worker.tscn";

    private static string Root(string project)
    {
        string config = RuntimeRegistry.ConfigRoot;
        string records = Path.Combine(config, "recordings");
        string owned = Path.Combine(records, CheckpointStore.ProjectId(project));
        string jobs = Path.Combine(owned, "replays");
        foreach (string d in new[] { config, records, owned, jobs })
            if (Directory.Exists(d) && new DirectoryInfo(d).LinkTarget is not null)
                throw new UnauthorizedAccessException("unsafe_output_path: replay symlink");
        return jobs;
    }

    public ReplayJobStatus Start(string projectPath, string sessionId, string? godotBinary)
    {
        string jobId = "replay_" + Guid.NewGuid().ToString("N");
        if (!SafeId.IsMatch(sessionId))
            return new ReplayJobStatus(jobId, "failed", sessionId, null,
                "replay_session_not_found", "Invalid session ID");
        try
        {
            projectPath = RuntimeRegistry.Normalize(projectPath);
            var store = new TimelineStore();
            var status = store.Info(projectPath, sessionId);
            // Check all committed hashes and replay eligibility before starting code.
            var page = store.Events(projectPath, sessionId, 0, 100);
            try { ReplayPlanBuilder.Build(status, page.Items); }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
            {
                return new ReplayJobStatus(jobId, "inconclusive", sessionId,
                    null, "replay_incomplete", ex.Message);
            }
            string binary = godotBinary ??
                System.Environment.GetEnvironmentVariable("GODOT_BIN") ??
                (OperatingSystem.IsMacOS()
                    ? "/Applications/Godot_mono.app/Contents/MacOS/Godot" : "godot");
            if (!Path.IsPathFullyQualified(binary) || !File.Exists(binary) ||
                !IsMono(binary))
                return new ReplayJobStatus(jobId, "failed", sessionId, null,
                    "replay_engine_unavailable", "Godot .NET/Mono executable not found or invalid");
            if (!File.Exists(Path.Combine(projectPath, "addons", "godot_mcp",
                "Recording", "replay_worker.tscn")))
                return new ReplayJobStatus(jobId, "failed", sessionId, null,
                    "replay_worker_unavailable", "Installed addon lacks replay worker");
            if (!Slots.Wait(0))
                return new ReplayJobStatus(jobId, "failed", sessionId, null,
                    "replay_busy", "Two concurrent replay workers are already running");
            try
            {
                string jobs = Root(projectPath);
                Directory.CreateDirectory(jobs);
                string jobDir = Path.Combine(jobs, jobId);
                Directory.CreateDirectory(jobDir);
                if (!OperatingSystem.IsWindows())
                {
                    RuntimeRegistry.SecureDirectory(jobs);
                    RuntimeRegistry.SecureDirectory(jobDir);
                }
                string output = Path.Combine(jobDir, "output");
                string requestPath = Path.Combine(jobDir, "request.json");
                string sessionDir = Directory.GetParent(
                    store.TimelineDirectory(projectPath, sessionId))!.FullName;
                var request = new ReplayRequest(projectPath, sessionDir, output);
                File.WriteAllText(requestPath, JsonSerializer.Serialize(request,
                    SnapshotJsonContext.Default.ReplayRequest));
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(requestPath,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite);
                var psi = new ProcessStartInfo(binary)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                foreach (string argument in new[] { "--headless", "--path", projectPath,
                    "--scene", Worker, "--", "--replay-job", requestPath })
                    psi.ArgumentList.Add(argument);
                psi.Environment["GODOT_MCP_CONFIG_DIR"] = RuntimeRegistry.ConfigRoot;
                psi.Environment["GODOT_MCP_REPLAY_WORKER"] = "1";
                var process = Process.Start(psi) ??
                    throw new InvalidOperationException("replay_start_failed");
                var running = new ReplayJobStatus(jobId, "running", sessionId,
                    null, null, null, process.Id);
                WriteStatus(jobDir, running);
                _ = Task.Run(() => Watch(process, jobDir, jobId, sessionId));
                return running;
            }
            catch
            {
                Slots.Release();
                throw;
            }
        }
        catch (Exception e) when (e is ArgumentException or IOException or
            UnauthorizedAccessException or InvalidOperationException or
            System.ComponentModel.Win32Exception)
        {
            return new ReplayJobStatus(jobId, "failed", sessionId, null,
                e is UnauthorizedAccessException ? "unsafe_output_path" :
                e is FileNotFoundException ? "replay_session_not_found" :
                "replay_start_failed", e.Message);
        }
    }

    public ReplayJobStatus Status(string projectPath, string jobId)
    {
        if (!SafeId.IsMatch(jobId))
            throw new ArgumentException("unsafe_output_path: invalid replay job ID");
        string jobDir = Path.Combine(Root(projectPath), jobId);
        if (!Directory.Exists(jobDir) || new DirectoryInfo(jobDir).LinkTarget is not null)
            throw new FileNotFoundException("replay_job_not_found", jobId);
        string output = Path.Combine(jobDir, "output");
        if (Directory.Exists(output) && new DirectoryInfo(output).LinkTarget is not null)
            throw new UnauthorizedAccessException("unsafe_output_path: linked replay output");
        string reportPath = Path.Combine(output, "replay_report.json");
        if (File.Exists(reportPath))
        {
            if (new FileInfo(reportPath).LinkTarget is not null)
                throw new UnauthorizedAccessException("unsafe_output_path: linked replay report");
            var report = JsonSerializer.Deserialize(File.ReadAllText(reportPath),
                SnapshotJsonContext.Default.ReplayReport)
                ?? throw new InvalidDataException("replay_corrupt: empty report");
            if (report.Status is "matched" or "diverged" or "inconclusive" or "failed")
                return new ReplayJobStatus(jobId, report.Status, report.SessionId,
                    reportPath, report.Code, report.Message);
        }
        string statusPath = Path.Combine(jobDir, "status.json");
        if (!File.Exists(statusPath) || new FileInfo(statusPath).LinkTarget is not null)
            throw new InvalidDataException("replay_corrupt: missing status");
        var value = JsonSerializer.Deserialize(File.ReadAllText(statusPath),
            RuntimeJsonContext.Default.ReplayJobStatus)
            ?? throw new InvalidDataException("replay_corrupt: invalid status");
        if (value.Status == "running" && value.Pid is int pid)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                if (!process.HasExited) return value;
            }
            catch (ArgumentException) { }
            return value with { Status = "failed", Code = "replay_process_exited",
                Message = "Replay worker exited without a verified final report" };
        }
        return value;
    }

    public ReplayReport Report(string projectPath, string jobId)
    {
        var state = Status(projectPath, jobId);
        if (state.ReportPath is null)
            throw new FileNotFoundException("replay_report_not_ready", jobId);
        return JsonSerializer.Deserialize(File.ReadAllText(state.ReportPath),
            SnapshotJsonContext.Default.ReplayReport)
            ?? throw new InvalidDataException("replay_corrupt: invalid report");
    }

    private static bool IsMono(string binary)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(binary)
            {
                UseShellExecute = false, RedirectStandardOutput = true,
                RedirectStandardError = true, ArgumentList = { "--version" }
            });
            if (p is null || !p.WaitForExit(5000)) return false;
            string version = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            return p.ExitCode == 0 && version.Contains("mono",
                StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static async Task Watch(Process process, string dir, string jobId,
        string sessionId)
    {
        try
        {
            var standard = Consume(process.StandardOutput);
            var errors = Consume(process.StandardError);
            using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try { await process.WaitForExitAsync(stop.Token); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                WriteStatus(dir, new ReplayJobStatus(jobId, "failed", sessionId,
                    null, "replay_timeout", "Worker exceeded 2 minutes", process.Id));
                return;
            }
            string output = (await standard) + (await errors);
            string log = Path.Combine(dir, "worker.log");
            File.WriteAllText(log, output);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(log, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            string report = Path.Combine(dir, "output", "replay_report.json");
            // Divergence legitimately yields exit code 1. A structured report
            // always takes precedence over exit status, including "inconclusive".
            if (File.Exists(report)) return;
            WriteStatus(dir, new ReplayJobStatus(jobId, "failed", sessionId,
                null, "replay_process_exited", "Worker exit " + process.ExitCode, process.Id));
        }
        catch (Exception ex)
        {
            WriteStatus(dir, new ReplayJobStatus(jobId, "failed", sessionId,
                null, "replay_process_error", ex.Message, process.Id));
        }
        finally { process.Dispose(); Slots.Release(); }
    }

    private static async Task<string> Consume(StreamReader reader)
    {
        var lines = new System.Text.StringBuilder();
        while (await reader.ReadLineAsync() is { } line)
            if (lines.Length < 128 * 1024)
                lines.AppendLine(line[..Math.Min(line.Length, 128 * 1024 - lines.Length)]);
        return lines.ToString();
    }

    private static void WriteStatus(string dir, ReplayJobStatus status)
    {
        string temporary = Path.Combine(dir, "status." + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllText(temporary, JsonSerializer.Serialize(status,
            RuntimeJsonContext.Default.ReplayJobStatus));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(temporary,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temporary, Path.Combine(dir, "status.json"), true);
    }
}
