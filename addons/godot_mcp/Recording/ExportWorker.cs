using System.Text.Json;
using Godot;

namespace GodotRuntimeMcp.Recording;

/// <summary>
/// A one-shot Godot --headless scene: reconstruct, pack, save, reload, and verify.
/// This process never attaches its restored tree to the currently running game.
/// </summary>
public partial class ExportWorker : Node
{
    public override void _Ready()
    {
        string jobPath = "";
        try
        {
            var args = OS.GetCmdlineUserArgs();
            int index = Array.IndexOf(args, "--snapshot-job");
            if (index < 0 || index + 1 >= args.Length)
                throw new ArgumentException("Missing --snapshot-job");
            jobPath = args[index + 1];
            var result = RunJobAsync(jobPath).GetAwaiter().GetResult();
            if (result.Status != "completed")
                throw new InvalidDataException(result.Code + ": " + result.Message);
            // Export is a one-shot process; collect released Godot RC wrappers before engine exit.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GD.Print("SNAPSHOT_EXPORT_PASS: " + result.ScenePath +
                " verified " + result.VerifiedNodeCount + "/" + result.NodeCount);
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            GD.PushError("SNAPSHOT_EXPORT_FAIL: " + ex);
            GetTree().Quit(1);
        }
    }

    public Task<SceneExportResult> RunJobAsync(string jobPath)
    {
        // All Godot operations intentionally execute synchronously on this worker's main thread.
        var request = JsonSerializer.Deserialize(
            File.ReadAllText(jobPath), SnapshotJsonContext.Default.SnapshotExportRequest)
            ?? throw new InvalidDataException("snapshot_export_invalid_job");
        if (!Path.IsPathFullyQualified(request.SnapshotDirectory) ||
            !Path.IsPathFullyQualified(request.ExportDirectory))
            throw new ArgumentException("unsafe_output_path: paths must be absolute");

        var config = System.Environment.GetEnvironmentVariable("GODOT_MCP_CONFIG_DIR");
        if (string.IsNullOrWhiteSpace(config))
            config = Path.Combine(System.Environment.GetFolderPath(
                System.Environment.SpecialFolder.UserProfile), ".config", "godot-runtime-mcp");
        string approvedRoot = Path.GetFullPath(config).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        string input = Path.GetFullPath(request.SnapshotDirectory);
        string output = Path.GetFullPath(request.ExportDirectory);
        if (!input.StartsWith(approvedRoot, StringComparison.Ordinal) ||
            !output.StartsWith(approvedRoot, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("unsafe_output_path: exports must remain inside configured root");
        if (Directory.Exists(output) && new DirectoryInfo(output).LinkTarget is not null)
            throw new UnauthorizedAccessException("unsafe_output_path: linked output directory");
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new IOException("unsafe_output_path: destination already exists and is not empty");

        SceneBuildResult? build = null;
        Node? checkForCleanup = null;
        var issues = new List<SnapshotIssue>();
        int expected = 0;
        int actual = 0;
        try
        {
            // Only BCL archive I/O runs on a worker thread; never block Godot's
            // synchronization context on async reads from _Ready().
            var snapshot = Task.Run(() => CheckpointArchive.ReadAsync(input,
                CancellationToken.None)).GetAwaiter().GetResult();
            expected = snapshot.Nodes.Count(n => !n.IsInternal);
            build = SnapshotSceneBuilder.Build(snapshot, request.Strict);
            issues.AddRange(build.Issues);
            using var packed = new PackedScene();
            if (packed.Pack(build.Root) != Error.Ok)
                throw new InvalidDataException("export_pack_failed");
            Directory.CreateDirectory(output);
            string scenePath = Path.Combine(output, "scene.tscn");
            if (ResourceSaver.Save(packed, scenePath) != Error.Ok)
                throw new InvalidDataException("export_save_failed");

            using var reloaded = ResourceLoader.Load<PackedScene>(scenePath,
                cacheMode: ResourceLoader.CacheMode.Ignore);
            if (reloaded is null) throw new InvalidDataException("export_validation_failed: load");
            var check = reloaded.Instantiate();
            checkForCleanup = check;
            actual = CountNodes(check);
            if (actual != expected)
                issues.Add(new SnapshotIssue("node_count_mismatch", null, null,
                    $"Expected {expected} nodes, got {actual}"));
            issues.AddRange(SnapshotSceneVerifier.Verify(snapshot, check));
            if (request.Strict && issues.Count > 0)
                throw new InvalidDataException("export_validation_failed: " +
                    string.Join(",", issues.Select(i => i.Code + ":" + i.Property)));

            string statePath = Path.Combine(output, "runtime_state.json");
            File.WriteAllText(statePath, JsonSerializer.Serialize(snapshot.RuntimeState,
                SnapshotJsonContext.Default.DictionaryStringSnapshotValue));
            string dependenciesPath = Path.Combine(output, "dependencies.json");
            var deps = new SceneExportDependencies(
                snapshot.Nodes.Select(n => n.ScriptPath)
                    .Concat(snapshot.Resources.Select(r => r.ScriptPath))
                    .Where(path => !string.IsNullOrEmpty(path))
                    .Select(path => path!).Distinct(StringComparer.Ordinal).ToList(),
                snapshot.Resources.Select(r => r.ResourcePath)
                    .Where(path => !string.IsNullOrEmpty(path))
                    .Select(path => path!).Distinct(StringComparer.Ordinal).ToList());
            File.WriteAllText(dependenciesPath, JsonSerializer.Serialize(deps,
                SnapshotJsonContext.Default.SceneExportDependencies));
            string reportPath = Path.Combine(output, "reconstruction_report.json");
            var result = new SceneExportResult("completed", scenePath, statePath,
                dependenciesPath, reportPath,
                issues.Count == 0 ? "checkpoint_exact" : "partial", issues, expected, actual);
            File.WriteAllText(reportPath, JsonSerializer.Serialize(result,
                SnapshotJsonContext.Default.SceneExportResult));
            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            // Godot can return Error.Ok from ResourceSaver even when a cyclic
            // Resource graph was not faithfully serialized. A failed export must
            // not leave an apparently usable scene file behind.
            Directory.CreateDirectory(output);
            string scenePath = Path.Combine(output, "scene.tscn");
            if (File.Exists(scenePath)) File.Delete(scenePath);
            string reportPath = Path.Combine(output, "reconstruction_report.json");
            string code = ex.Message.Contains("resource_dependency_changed", StringComparison.Ordinal)
                ? "resource_dependency_changed" :
                ex.Message.Contains("snapshot_corrupt", StringComparison.Ordinal) ? "snapshot_corrupt" :
                ex.Message.Contains("snapshot_incomplete", StringComparison.Ordinal) ? "snapshot_incomplete" :
                "export_validation_failed";
            issues.Add(new SnapshotIssue(code, null, null, ex.Message));
            var failure = new SceneExportResult(
                "failed", null, null, null, reportPath, "partial", issues, expected, actual,
                code, ex.Message);
            File.WriteAllText(reportPath, JsonSerializer.Serialize(
                failure, SnapshotJsonContext.Default.SceneExportResult));
            return Task.FromResult(failure);
        }
        finally
        {
            if (checkForCleanup is not null && GodotObject.IsInstanceValid(checkForCleanup))
                checkForCleanup.Free();
            if (build is not null && GodotObject.IsInstanceValid(build.Root))
                build.Root.Free();
        }
    }

    private static int CountNodes(Node root)
    {
        int count = 1;
        foreach (var child in root.GetChildren()) count += CountNodes(child);
        return count;
    }

    private static void CompareTree(
        SceneCheckpoint snapshot, Node reconstructed, List<SnapshotIssue> issues)
    {
        var rootRow = snapshot.Nodes.Single(x => x.NodeId == snapshot.RootNodeId);
        var models = snapshot.Nodes.Where(x => !x.IsInternal)
            .GroupBy(x => x.ParentId).ToDictionary(x => x.Key ?? "", x =>
                x.OrderBy(r => r.SiblingIndex).ToList());
        void Visit(RecordedNode row, Node live)
        {
            if (live.Name.ToString() != row.Name ||
                live.GetClass().ToString() != row.NativeClass)
                issues.Add(new SnapshotIssue("node_mismatch", row.NodeId, null,
                    "Node type/name differs after native scene serialization"));
            var wanted = models.GetValueOrDefault(row.NodeId) ?? new();
            var actual = live.GetChildren();
            if (actual.Count != wanted.Count)
            {
                issues.Add(new SnapshotIssue("children_mismatch", row.NodeId, null,
                    $"Expected {wanted.Count}, got {actual.Count}"));
                return;
            }
            for (int i = 0; i < wanted.Count; i++) Visit(wanted[i], actual[i]);
        }
        Visit(rootRow, reconstructed);
    }
}
