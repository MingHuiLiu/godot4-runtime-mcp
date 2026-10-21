using GodotRuntimeMcp.Recording;
using System.Text.RegularExpressions;

namespace GodotRuntimeMcp;

/// <summary>Offline, authenticated-by-project-path read-only view of observed timelines.</summary>
public sealed class TimelineStore
{
    private static readonly Regex SafeId = new(
        "^[A-Za-z0-9_-]{1,100}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static void AssertNoLink(string path)
    {
        if (Directory.Exists(path) && new DirectoryInfo(path).LinkTarget is not null ||
            File.Exists(path) && new FileInfo(path).LinkTarget is not null)
            throw new UnauthorizedAccessException("unsafe_output_path: symlink in timeline store");
    }

    private static string Root(string project)
    {
        string config = RuntimeRegistry.ConfigRoot;
        AssertNoLink(config);
        string recordings = Path.Combine(config, "recordings");
        AssertNoLink(recordings);
        string projectRoot = Path.Combine(recordings, CheckpointStore.ProjectId(project));
        AssertNoLink(projectRoot);
        return projectRoot;
    }

    private static string Resolve(string project, string sessionId)
    {
        if (!SafeId.IsMatch(sessionId))
            throw new ArgumentException("unsafe_output_path: invalid recording session ID",
                nameof(sessionId));
        string projectRoot = Root(project);
        string session = Path.Combine(projectRoot, sessionId);
        AssertNoLink(session);
        string timeline = Path.Combine(session, "timeline");
        AssertNoLink(timeline);
        if (!Directory.Exists(timeline))
            throw new FileNotFoundException("timeline_session_not_found", sessionId);
        string events = Path.Combine(timeline, "batches");
        AssertNoLink(events);
        AssertNoLink(Path.Combine(timeline, "status.json"));
        return timeline;
    }

    public IReadOnlyList<TimelineSessionStatus> List(string project, int limit = 50)
    {
        if (limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(limit));
        string root = Root(project);
        if (!Directory.Exists(root)) return Array.Empty<TimelineSessionStatus>();
        var sessions = new List<TimelineSessionStatus>();
        foreach (string session in Directory.GetDirectories(root))
        {
            AssertNoLink(session);
            string timeline = Path.Combine(session, "timeline");
            if (!Directory.Exists(timeline)) continue;
            AssertNoLink(timeline);
            AssertNoLink(Path.Combine(timeline, "status.json"));
            try
            {
                var state = TimelineArchive.ReadStatus(timeline);
                if (state.ProjectId == CheckpointStore.ProjectId(project) &&
                    state.SessionId == Path.GetFileName(session))
                    sessions.Add(state);
            }
            catch (InvalidDataException) { /* incomplete session is not a published result */ }
            catch (NotSupportedException) { }
        }
        return sessions.OrderByDescending(s => s.SessionId, StringComparer.Ordinal)
            .Take(limit).ToList();
    }

    public TimelineSessionStatus Info(string project, string sessionId)
    {
        var value = TimelineArchive.ReadStatus(Resolve(project, sessionId));
        if (value.ProjectId != CheckpointStore.ProjectId(project) || value.SessionId != sessionId)
            throw new UnauthorizedAccessException("timeline_project_mismatch");
        return value;
    }

    public TimelineEventPage Events(string project, string sessionId, int offset, int limit)
    {
        string root = Resolve(project, sessionId);
        var status = Info(project, sessionId);
        for (int i = offset; i < Math.Min(status.SampleCount, offset + limit); i++)
        {
            string name = i.ToString("D6");
            AssertNoLink(Path.Combine(root, "batches", name + ".json"));
            AssertNoLink(Path.Combine(root, "batches", name + ".sha256"));
        }
        return TimelineArchive.ReadPage(root, offset, limit);
    }
}
