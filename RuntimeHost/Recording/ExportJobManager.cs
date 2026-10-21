using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotRuntimeMcp.Recording;

namespace GodotRuntimeMcp;

/// <summary>Starts a separate, trusted Godot Mono worker and stores durable job state.</summary>
public sealed class ExportJobManager
{
    private static readonly Regex SafeId = new(
        "^[a-zA-Z0-9_-]{1,100}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly SemaphoreSlim Slots = new(2, 2);
    private const string ExportWorkerScene =
        "res://addons/godot_mcp/Recording/export_worker.tscn";

    private static string Root(string project)
    {
        string records = Path.Combine(RuntimeRegistry.ConfigRoot, "recordings");
        string projectDir = Path.Combine(records, CheckpointStore.ProjectId(project));
        string exports = Path.Combine(projectDir, "exports");
        foreach (string directory in new[] { RuntimeRegistry.ConfigRoot, records, projectDir, exports })
            if (Directory.Exists(directory) && new DirectoryInfo(directory).LinkTarget is not null)
                throw new UnauthorizedAccessException("unsafe_output_path: symbolic link");
        return exports;
    }

    public SnapshotJobInfo Start(
        string projectPath, string snapshotId, bool strict, string? godotBinary)
    {
        string jobId = "export_" + Guid.NewGuid().ToString("N");
        try
        {
            projectPath = RuntimeRegistry.Normalize(projectPath);
            var store = new CheckpointStore();
            var manifest = store.Info(projectPath, snapshotId);
            string snapshotDirectory = store.GetSnapshotDirectory(projectPath, snapshotId);
            string binary = !string.IsNullOrWhiteSpace(godotBinary) ? godotBinary :
                System.Environment.GetEnvironmentVariable("GODOT_BIN") ??
                (OperatingSystem.IsMacOS() ? "/Applications/Godot_mono.app/Contents/MacOS/Godot" : "godot");
            if (!Path.IsPathFullyQualified(binary) || !File.Exists(binary))
                return new SnapshotJobInfo(jobId, "failed", snapshotId,
                    "export_engine_unavailable", "Godot Mono executable not found");
            if (!IsMonoExecutable(binary))
                return new SnapshotJobInfo(jobId, "failed", snapshotId,
                    "export_engine_unavailable", "Executable is not Godot Mono");
            if (!File.Exists(Path.Combine(projectPath, "addons", "godot_mcp", "Recording",
                    "export_worker.tscn")))
                return new SnapshotJobInfo(jobId, "failed", snapshotId,
                    "export_worker_unavailable", "Project does not contain export_worker.tscn");
            if (!Slots.Wait(0))
                return new SnapshotJobInfo(jobId, "failed", snapshotId,
                    "snapshot_busy", "At most two exports may run concurrently");

            try
            {
                string baseDir = Root(projectPath);
                Directory.CreateDirectory(baseDir);
                string jobDir = Path.Combine(baseDir, jobId);
                Directory.CreateDirectory(jobDir);
                if (!OperatingSystem.IsWindows())
                {
                    RuntimeRegistry.SecureDirectory(baseDir);
                    RuntimeRegistry.SecureDirectory(jobDir);
                }
                string output = Path.Combine(jobDir, "output");
                string input = Path.Combine(jobDir, "job.json");
                var request = new SnapshotExportRequest(snapshotDirectory, output, strict);
                File.WriteAllText(input, JsonSerializer.Serialize(request,
                    SnapshotJsonContext.Default.SnapshotExportRequest));

                var psi = new ProcessStartInfo(binary) { UseShellExecute = false,
                    RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (string argument in new[] { "--headless", "--path", projectPath,
                    "--scene", ExportWorkerScene, "--", "--snapshot-job", input })
                    psi.ArgumentList.Add(argument);
                psi.Environment["GODOT_MCP_CONFIG_DIR"] = RuntimeRegistry.ConfigRoot;
                psi.Environment["GODOT_MCP_EXPORT_WORKER"] = "1";
                var process = Process.Start(psi)
                    ?? throw new InvalidOperationException("export_worker_unavailable");
                var status = new SceneExportJobStatus(jobId, "running", null, null,
                    null, null, process.Id);
                WriteStatus(jobDir, status);
                _ = Task.Run(() => WatchProcessAsync(process, jobDir, jobId));
                return new SnapshotJobInfo(jobId, "running", snapshotId);
            }
            catch
            {
                Slots.Release();
                throw;
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or
            UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new SnapshotJobInfo(jobId, "failed", snapshotId,
                ex is UnauthorizedAccessException ? "unsafe_output_path" :
                ex is FileNotFoundException ? "snapshot_not_found" : "export_start_failed",
                ex.Message);
        }
    }

    public SceneExportJobStatus Status(string projectPath, string jobId)
    {
        if (!SafeId.IsMatch(jobId))
            throw new ArgumentException("unsafe_output_path: invalid export job ID");
        string jobDir = Path.Combine(Root(projectPath), jobId);
        if (!Directory.Exists(jobDir) || new DirectoryInfo(jobDir).LinkTarget is not null)
            throw new FileNotFoundException("snapshot_export_job_not_found", jobId);
        string report = Path.Combine(jobDir, "output", "reconstruction_report.json");
        if (File.Exists(report))
        {
            var result = JsonSerializer.Deserialize(
                File.ReadAllText(report), SnapshotJsonContext.Default.SceneExportResult)
                ?? throw new InvalidDataException("snapshot_corrupt: export report is empty");
            if (result.Status is "completed" or "failed")
                return new SceneExportJobStatus(jobId, result.Status,
                    result.Status == "completed" ? result.ScenePath : null,
                    report, result.Code, result.Message);
        }
        string statusFile = Path.Combine(jobDir, "status.json");
        if (!File.Exists(statusFile))
            throw new InvalidDataException("snapshot_incomplete: missing export job status");
        var value = JsonSerializer.Deserialize(
            File.ReadAllText(statusFile), RuntimeJsonContext.Default.SceneExportJobStatus)
            ?? throw new InvalidDataException("snapshot_corrupt: malformed job status");
        if (value.Status == "running" && value.Pid is int pid)
        {
            bool alive;
            try { using var p = Process.GetProcessById(pid); alive = !p.HasExited; }
            catch (ArgumentException) { alive = false; }
            if (!alive)
                return value with { Status = "failed", Code = "export_process_exited",
                    Message = "Worker exited without a successful verified report" };
        }
        return value;
    }

    private static bool IsMonoExecutable(string file)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(file)
            {
                UseShellExecute = false, RedirectStandardOutput = true,
                RedirectStandardError = true, ArgumentList = { "--version" }
            });
            if (process is null || !process.WaitForExit(5000)) return false;
            string version = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            return process.ExitCode == 0 &&
                   version.Contains("mono", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static async Task WatchProcessAsync(Process process, string jobDir, string jobId)
    {
        try
        {
            var output = ReadBoundedAsync(process.StandardOutput);
            var errors = ReadBoundedAsync(process.StandardError);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                WriteStatus(jobDir, new SceneExportJobStatus(jobId, "failed", null, null,
                    "export_timeout", "Godot worker exceeded 2 minutes", process.Id));
                return;
            }
            string logs = (await output) + (await errors);
            File.WriteAllText(Path.Combine(jobDir, "worker.log"), logs);
            string reportPath = Path.Combine(jobDir, "output", "reconstruction_report.json");
            bool success = process.ExitCode == 0 && File.Exists(reportPath);
            WriteStatus(jobDir, new SceneExportJobStatus(jobId,
                success ? "completed" : "failed",
                success ? Path.Combine(jobDir, "output", "scene.tscn") : null,
                success ? reportPath : null,
                success ? null : "export_validation_failed",
                success ? null : $"Worker exit {process.ExitCode}; see worker.log",
                process.Id));
        }
        catch (Exception ex)
        {
            WriteStatus(jobDir, new SceneExportJobStatus(jobId, "failed", null, null,
                "export_process_error", ex.Message, process.Id));
        }
        finally { process.Dispose(); Slots.Release(); }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader)
    {
        var result = new StringBuilder();
        while (await reader.ReadLineAsync() is { } line)
            if (result.Length < 128 * 1024)
                result.AppendLine(line[..Math.Min(line.Length, 128 * 1024 - result.Length)]);
        return result.ToString();
    }

    private static void WriteStatus(string jobDir, SceneExportJobStatus status)
    {
        string temp = Path.Combine(jobDir, "status." + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllText(temp, JsonSerializer.Serialize(status,
            RuntimeJsonContext.Default.SceneExportJobStatus));
        File.Move(temp, Path.Combine(jobDir, "status.json"), true);
    }
}
