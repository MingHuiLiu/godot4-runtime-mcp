using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GodotRuntimeMcp;

public sealed record RuntimeEntry(int Protocol, string InstanceId, int Pid,
    string ProjectPath, int Port, string Token, string StartedAt, string Heartbeat)
{
    public JsonNode Public => new JsonObject
    {
        ["projectPath"] = ProjectPath, ["instanceId"] = InstanceId,
        ["pid"] = Pid, ["port"] = Port, ["startedAt"] = StartedAt, ["heartbeat"] = Heartbeat
    };
}

public static class RuntimeRegistry
{
    internal static readonly HttpClient Http = new(new HttpClientHandler { UseProxy = false })
    { Timeout = TimeSpan.FromSeconds(19) };

    public static string Root => Environment.GetEnvironmentVariable("GODOT_MCP_REGISTRY_DIR")
        ?? Path.Combine(Path.GetTempPath(), "godot-runtime-mcp");

    public static string ConfigRoot => Environment.GetEnvironmentVariable("GODOT_MCP_CONFIG_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "godot-runtime-mcp");

    public static string Normalize(string path)
    {
        if (path == "~" || path.StartsWith("~" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + path[1..];
        return Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)));
    }

    public static IReadOnlyList<string> Projects()
    {
        var file = Path.Combine(ConfigRoot, "projects.json");
        try
        {
            var projects = JsonSerializer.Deserialize(File.ReadAllText(file), RuntimeJsonContext.Default.ListString) ?? [];
            return projects.Where(p => Path.IsPathFullyQualified(p) && File.Exists(Path.Combine(p, "project.godot")))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
        catch (JsonException) { return []; }
    }

    public static void Remember(string project)
    {
        project = Normalize(project);
        Directory.CreateDirectory(ConfigRoot);
        SecureDirectory(ConfigRoot);
        // Independent CLI/MCP installer processes can register different projects
        // concurrently; serialize the read-modify-replace across processes.
        FileStream? handle = null;
        var lockPath = Path.Combine(ConfigRoot, "projects.lock");
        for (var retry = 0; retry < 120; retry++)
        {
            try
            {
                handle = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                break;
            }
            catch (IOException) when (retry < 119)
            {
                Thread.Sleep(25);
            }
        }
        using (handle ?? throw new IOException("Cannot acquire project registry lock"))
        {
            SecureFile(lockPath);
            var projects = Projects().Append(project).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).ToArray();
            var temp = Path.Combine(ConfigRoot, $"projects.{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(projects, RuntimeJsonContext.Default.StringArray));
                SecureFile(temp);
                File.Move(temp, Path.Combine(ConfigRoot, "projects.json"), true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }

    public static void SecureDirectory(string directory)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    public static void SecureFile(string file)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static bool OwnedAndPrivate(string file)
    {
        if (new FileInfo(file).LinkTarget != null) return false;
        if (OperatingSystem.IsWindows()) return true;
        return (File.GetUnixFileMode(file) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite |
            UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite |
            UnixFileMode.OtherExecute)) == 0;
    }

    public static async Task<List<RuntimeEntry>> LiveAsync()
    {
        if (!Directory.Exists(Root) || new DirectoryInfo(Root).LinkTarget != null)
            return [];
        var candidates = new List<RuntimeEntry>();
        foreach (var file in Directory.EnumerateFiles(Root, "runtime-*.json"))
        {
            try
            {
                var name = Path.GetFileName(file);
                if (!Regex.IsMatch(name, @"^runtime-[0-9]+-[0-9a-f]{32}\.json$") ||
                    !OwnedAndPrivate(file) || new FileInfo(file).Length > 16384)
                    continue;
                var entry = JsonSerializer.Deserialize(File.ReadAllText(file), RuntimeJsonContext.Default.RuntimeEntry);
                if (entry == null || entry.Protocol != 1 || entry.Pid <= 0 ||
                    entry.Port < 1 || entry.Port > 65535 ||
                    !Regex.IsMatch(entry.Token ?? "", "^[0-9a-f]{64}$") ||
                    !Regex.IsMatch(entry.InstanceId ?? "", "^[0-9a-f]{32}$") ||
                    !Path.IsPathFullyQualified(entry.ProjectPath))
                    continue;
                using var process = Process.GetProcessById(entry.Pid);
                if (process.HasExited) continue;
                candidates.Add(entry);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
            { /* stale or untrusted record; never let one record take down discovery */ }
        }

        var health = await Task.WhenAll(candidates.Select(HealthyAsync));
        return candidates.Where((_, i) => health[i])
            .OrderBy(e => e.ProjectPath, StringComparer.Ordinal)
            .ThenBy(e => e.Pid).ToList();
    }

    private static async Task<bool> HealthyAsync(RuntimeEntry entry)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(700));
        try
        {
            using var response = await Http.GetAsync($"http://127.0.0.1:{entry.Port}/health", cts.Token);
            if (!response.IsSuccessStatusCode) return false;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cts.Token));
            var root = json.RootElement;
            return root.GetProperty("protocol").GetInt32() == 1 &&
                root.GetProperty("status").GetString() == "running" &&
                root.GetProperty("instanceId").GetString() == entry.InstanceId &&
                root.GetProperty("projectPath").GetString() == entry.ProjectPath &&
                root.GetProperty("pid").GetInt32() == entry.Pid &&
                root.GetProperty("port").GetInt32() == entry.Port;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException
            or KeyNotFoundException or OperationCanceledException)
        { return false; }
    }

    public static async Task<RuntimeEntry> SelectAsync(string? project, string? instance)
    {
        var choices = await LiveAsync();
        if (!string.IsNullOrWhiteSpace(project))
        {
            string path;
            if (Path.IsPathFullyQualified(project) || project.Contains(Path.DirectorySeparatorChar))
                path = Normalize(project);
            else
            {
                var matches = choices.Select(e => e.ProjectPath).Concat(Projects())
                    .Where(p => Path.GetFileName(p) == project).Distinct().ToArray();
                if (matches.Length > 1)
                    throw new InvalidOperationException("Ambiguous project name; specify absolute project path: " + string.Join(", ", matches));
                path = matches.Length == 1 ? matches[0] : Normalize(project);
            }
            choices = choices.Where(e => Normalize(e.ProjectPath) == path).ToList();
        }
        if (!string.IsNullOrWhiteSpace(instance))
            choices = choices.Where(e => e.InstanceId.StartsWith(instance, StringComparison.Ordinal)).ToList();
        if (choices.Count == 0)
            throw new InvalidOperationException("Godot backend offline/not installed for selected target. " +
                "Use 'godot-runtime-cli install --project PATH', build the C# project and run the game. " +
                "Offline setup, tool discovery and installation remain available.");
        if (choices.Count != 1)
            throw new InvalidOperationException("Multiple matching instances: " +
                string.Join(", ", choices.Select(e => $"{e.ProjectPath}:{e.InstanceId}")) +
                ". Specify --project and --instance to avoid controlling the wrong game.");
        return choices[0];
    }

    public static async Task<JsonElement> CallAsync(string tool, JsonElement args,
        string? project, string? instance)
    {
        var entry = await SelectAsync(project, instance);
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"http://127.0.0.1:{entry.Port}/call");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", entry.Token);
        request.Content = new StringContent(new JsonObject
        {
            ["tool"] = tool,
            ["arguments"] = JsonNode.Parse(args.ValueKind == JsonValueKind.Object ? args.GetRawText() : "{}")
        }.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Godot backend HTTP {(int)response.StatusCode}: {body}");
        using var result = JsonDocument.Parse(body);
        if (!result.RootElement.TryGetProperty("success", out var ok))
            throw new InvalidOperationException("Invalid response from Godot backend");
        if (!ok.GetBoolean())
            throw new InvalidOperationException(result.RootElement.TryGetProperty("error", out var error)
                ? error.ToString() : "Godot operation failed");
        return result.RootElement.TryGetProperty("data", out var data)
            ? data.Clone() : JsonDocument.Parse("null").RootElement.Clone();
    }
}
