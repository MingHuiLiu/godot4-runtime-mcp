using GodotRuntimeMcp.Recording;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace GodotRuntimeMcp;

/// <summary>
/// Read-only, project-scoped offline checkpoint store.
/// Never accepts a raw file path or a runtime-provided project identity from MCP arguments.
/// </summary>
public sealed class CheckpointStore
{
    private static readonly Regex SafeId = new(
        "^[a-zA-Z0-9_-]{1,100}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string ProjectId(string projectPath) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            RuntimeRegistry.Normalize(projectPath)))).ToLowerInvariant();

    private static void AssertNoLink(string path)
    {
        if (Directory.Exists(path) && new DirectoryInfo(path).LinkTarget != null)
            throw new UnauthorizedAccessException("unsafe_output_path: linked snapshot directory");
        if (File.Exists(path) && new FileInfo(path).LinkTarget != null)
            throw new UnauthorizedAccessException("unsafe_output_path: linked snapshot file");
    }

    private static string ProjectRoot(string projectPath)
    {
        string config = RuntimeRegistry.ConfigRoot;
        AssertNoLink(config);
        string records = Path.Combine(config, "recordings");
        AssertNoLink(records);
        string root = Path.Combine(records, ProjectId(projectPath));
        AssertNoLink(root);
        return root;
    }

    private static string Resolve(string projectPath, string snapshotId)
    {
        if (!SafeId.IsMatch(snapshotId))
            throw new ArgumentException("unsafe_output_path: invalid snapshotId", nameof(snapshotId));
        string root = ProjectRoot(projectPath);
        if (!Directory.Exists(root))
            throw new FileNotFoundException("snapshot_not_found: project has no checkpoint store", snapshotId);
        foreach (var session in Directory.GetDirectories(root))
        {
            AssertNoLink(session);
            string path = Path.Combine(session, snapshotId);
            if (!Directory.Exists(path)) continue;
            AssertNoLink(path);
            foreach (string file in Directory.GetFiles(path)) AssertNoLink(file);
            return path;
        }
        throw new FileNotFoundException("snapshot_not_found: snapshot not present in project", snapshotId);
    }

    public string GetSnapshotDirectory(string projectPath, string snapshotId) =>
        Resolve(projectPath, snapshotId);

    public IReadOnlyList<CheckpointManifest> List(string projectPath, int limit = 50)
    {
        if (limit < 1 || limit > 100)
            throw new ArgumentOutOfRangeException(nameof(limit), "limit must be 1..100");
        string root = ProjectRoot(projectPath);
        if (!Directory.Exists(root)) return Array.Empty<CheckpointManifest>();
        var snapshots = new List<CheckpointManifest>();
        foreach (string session in Directory.GetDirectories(root))
        {
            AssertNoLink(session);
            foreach (string path in Directory.GetDirectories(session))
            {
                AssertNoLink(path);
                foreach (string file in Directory.GetFiles(path)) AssertNoLink(file);
                // Do not fabricate success for incomplete or corrupt archives.
                try
                {
                    var snapshot = CheckpointArchive.ReadAsync(path, CancellationToken.None)
                        .GetAwaiter().GetResult();
                    if (snapshot.ProjectId != ProjectId(projectPath)) continue;
                    snapshots.Add(CheckpointArchive.ReadManifest(path));
                }
                catch (InvalidDataException) { }
                catch (NotSupportedException) { }
            }
        }
        return snapshots.OrderByDescending(x => x.CreatedUtc).Take(limit).ToList();
    }

    public CheckpointManifest Info(string projectPath, string snapshotId)
    {
        string directory = Resolve(projectPath, snapshotId);
        var content = CheckpointArchive.ReadAsync(directory, CancellationToken.None)
            .GetAwaiter().GetResult();
        if (content.ProjectId != ProjectId(projectPath))
            throw new UnauthorizedAccessException("snapshot_project_mismatch");
        return CheckpointArchive.ReadManifest(directory);
    }

    public SnapshotNodePage QueryNodes(
        string projectPath, string snapshotId, string? nodeId,
        string? pathPrefix, string? className, int offset, int limit)
    {
        if (offset < 0 || limit < 1 || limit > 100)
            throw new ArgumentOutOfRangeException(nameof(limit), "offset >=0 and limit 1..100 required");
        string directory = Resolve(projectPath, snapshotId);
        var content = CheckpointArchive.ReadAsync(directory, CancellationToken.None)
            .GetAwaiter().GetResult();
        if (content.ProjectId != ProjectId(projectPath))
            throw new UnauthorizedAccessException("snapshot_project_mismatch");
        var filtered = content.Nodes.Where(n =>
            (string.IsNullOrEmpty(nodeId) || n.NodeId == nodeId) &&
            (string.IsNullOrEmpty(pathPrefix) ||
                n.OriginalPath.StartsWith(pathPrefix, StringComparison.Ordinal)) &&
            (string.IsNullOrEmpty(className) || n.NativeClass == className)).ToList();
        return new SnapshotNodePage(filtered.Count, offset, limit,
            filtered.Skip(offset).Take(limit).ToList());
    }
}
