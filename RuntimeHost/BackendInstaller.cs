using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace GodotRuntimeMcp;

public static class BackendInstaller
{
    public const string Repository = "MingHuiLiu/godot4-runtime-mcp";
    private const int MaxDownload = 25 * 1024 * 1024;
    private const int MaxUnpacked = 12 * 1024 * 1024;
    private const int MaxFiles = 150;
    private static readonly HashSet<string> Required = new(StringComparer.Ordinal)
    {
        "GodotMcpServer.cs",
        "GodotRuntimeTransport.cs", "mcp_server.tscn", "tool_manifest.json"
    };
    private static readonly HttpClient DownloadClient = new()
    { Timeout = TimeSpan.FromSeconds(35) };

    public static async Task<JsonNode> InstallAsync(string project, string source = "auto",
        string version = "latest", string? path = null, bool force = false)
    {
        project = RuntimeRegistry.Normalize(project);
        var config = Path.Combine(project, "project.godot");
        if (!File.Exists(config))
            throw new InvalidOperationException("Missing project.godot: " + project);
        if (source is not ("auto" or "release" or "github" or "local"))
            throw new ArgumentException("source must be auto, release, github or local");

        Dictionary<string, byte[]> files;
        string origin;
        if (source == "local")
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("--path is required for --source local");
            (files, origin) = LocalFiles(path);
        }
        else if (source == "github")
        {
            (var bytes, origin) = await SourceArchiveAsync();
            files = ExtractAddon(bytes);
        }
        else
        {
            try
            {
                (var bytes, origin) = await ReleaseArchiveAsync(version);
                files = ExtractAddon(bytes);
            }
            catch (HttpRequestException) when (source == "auto" && version == "latest")
            {
                // Prefer Releases; if no release exists or GitHub is unreachable, the
                // C# host contains the same plugin source as an embedded fallback.
                // This works even when the game has not installed the addon yet.
                (files, origin) = BundledAddon();
            }
        }

        Validate(files);
        var original = await File.ReadAllTextAsync(config);
        var updated = EnsureAutoload(EnsureAssemblyName(original, project));
        var addons = Path.Combine(project, "addons");
        var destination = Path.Combine(addons, "godot_mcp");
        if (Directory.Exists(destination))
        {
            if (new DirectoryInfo(destination).LinkTarget != null)
                throw new InvalidOperationException("Refusing to replace symlink addon directory");
            var current = LocalDirectoryFiles(destination);
            if (FilesEqual(current, files))
            {
                if (updated != original) await AtomicWriteAsync(config, updated);
                RuntimeRegistry.Remember(project);
                return new JsonObject { ["status"] = "already-installed", ["project"] = project,
                    ["origin"] = origin, ["files"] = files.Count };
            }
            if (!force)
                throw new InvalidOperationException("Different addon already installed. Use --force to back it up and replace.");
        }
        else if (File.Exists(destination))
            throw new InvalidOperationException("Addon target is not a directory");

        Directory.CreateDirectory(addons);
        var staging = Path.Combine(addons, ".godot-mcp-" + Guid.NewGuid().ToString("N"));
        var backup = (string?)null;
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var (name, contents) in files)
            {
                var target = Path.Combine(staging, name.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await File.WriteAllBytesAsync(target, contents);
            }

            if (Directory.Exists(destination))
            {
                backup = Path.Combine(RuntimeRegistry.ConfigRoot, "backups",
                    "godot_mcp.backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" +
                    Guid.NewGuid().ToString("N")[..6]);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                Directory.Move(destination, backup);
            }
            try
            {
                Directory.Move(staging, destination);
                if (updated != original)
                    await AtomicWriteAsync(config, updated);
            }
            catch
            {
                if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
                if (backup != null && Directory.Exists(backup))
                    Directory.Move(backup, destination);
                throw;
            }
            RuntimeRegistry.Remember(project);
            return new JsonObject
            {
                ["status"] = "installed", ["project"] = project, ["origin"] = origin,
                ["files"] = files.Count, ["backup"] = backup,
                ["next"] = "Build the Godot C# project, then run each desired game process."
            };
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
    }

    public static async Task<JsonNode> PackAsync(string source, string output)
    {
        var (files, _) = LocalFiles(source);
        var path = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path)) throw new InvalidOperationException("Archive exists: " + path);
        await using var stream = File.Create(path);
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in files.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                var entry = zip.CreateEntry("addons/godot_mcp/" + name, CompressionLevel.Optimal);
                using var writer = entry.Open();
                await writer.WriteAsync(content);
            }
        }
        return new JsonObject
        {
            ["archive"] = path, ["files"] = files.Count,
            ["release"] = "Upload this ZIP to a GitHub Release (no GitHub Actions required)."
        };
    }

    private static async Task AtomicWriteAsync(string file, string content)
    {
        var temp = file + ".mcp-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, content);
            File.Move(temp, file, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static bool FilesEqual(Dictionary<string, byte[]> a, Dictionary<string, byte[]> b) =>
        a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var content) &&
                                           kv.Value.AsSpan().SequenceEqual(content));

    private static string EnsureAssemblyName(string settings, string project)
    {
        // Godot .NET must know which assembly to load. Older projects may lack
        // [dotnet] project/assembly_name even though a C# csproj exists.
        if (Regex.IsMatch(settings, @"(?m)^\s*project/assembly_name\s*="))
            return settings;
        var projects = Directory.GetFiles(project, "*.csproj", SearchOption.TopDirectoryOnly);
        if (projects.Length != 1)
            return settings; // GDScript-only or ambiguous: do not guess/create an SDK project.
        var assemblyName = Path.GetFileNameWithoutExtension(projects[0]);
        try
        {
            var xml = System.Xml.Linq.XDocument.Load(projects[0]);
            assemblyName = xml.Descendants("AssemblyName").FirstOrDefault()?.Value ?? assemblyName;
        }
        catch (System.Xml.XmlException) { /* use file name */ }
        if (!Regex.IsMatch(assemblyName, @"^[\w.\-]+$"))
            throw new InvalidOperationException("Invalid Godot C# assembly name");
        var entry = "project/assembly_name=" + (char)34 + assemblyName + (char)34;
        var section = Regex.Match(settings, @"(?m)^\[dotnet\]\s*$");
        if (!section.Success)
            return settings.TrimEnd() + "\n\n[dotnet]\n" + entry + "\n";
        var next = Regex.Match(settings[(section.Index + section.Length)..], @"(?m)^\[[^\]]+\]\s*$");
        var end = next.Success ? section.Index + section.Length + next.Index : settings.Length;
        return settings[..end].TrimEnd() + "\n" + entry + "\n\n" + settings[end..];
    }

    private static string EnsureAutoload(string settings)
    {
        const string entry = "GodotMcpServer=\"*res://addons/godot_mcp/mcp_server.tscn\"";
        var section = Regex.Match(settings, @"(?m)^\[autoload\]\s*$");
        if (!section.Success)
            return settings.TrimEnd() + "\n\n[autoload]\n" + entry + "\n";

        var next = Regex.Match(settings[(section.Index + section.Length)..], @"(?m)^\[[^\]]+\]\s*$");
        var end = next.Success ? section.Index + section.Length + next.Index : settings.Length;
        var body = settings[(section.Index + section.Length)..end];
        var existing = Regex.Match(body, @"(?m)^\s*GodotMcpServer\s*=\s*(.+)$");
        if (existing.Success)
        {
            if (existing.Groups[1].Value.Trim() !=
                "\"*res://addons/godot_mcp/mcp_server.tscn\"")
                throw new InvalidOperationException("Conflicting GodotMcpServer autoload in project.godot");
            return settings;
        }
        return settings[..end].TrimEnd() + "\n" + entry + "\n\n" + settings[end..];
    }

    private static Dictionary<string, byte[]> LocalDirectoryFiles(string addon)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(addon, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(addon, file).Replace('\\', '/');
            if (relative.Split('/').Any(part => part == ".godot" || part.StartsWith(".git")) ||
                relative.EndsWith(".uid") || relative.EndsWith(".import"))
                continue;
            if (new FileInfo(file).LinkTarget != null)
                throw new InvalidOperationException("Addon contains a symlink: " + file);
            files[relative] = File.ReadAllBytes(file);
            if (files.Count > MaxFiles || files.Values.Sum(v => v.Length) > MaxUnpacked)
                throw new InvalidOperationException("Local addon exceeds size limit");
        }
        return files;
    }

    private static (Dictionary<string, byte[]> Files, string Origin) LocalFiles(string source)
    {
        var root = RuntimeRegistry.Normalize(source);
        if (File.Exists(root))
            return (ExtractAddon(File.ReadAllBytes(root)), "local:" + root);
        var candidates = new[] { Path.Combine(root, "addons", "godot_mcp"),
            Path.Combine(root, "godot_mcp"), root };
        foreach (var candidate in candidates)
            if (File.Exists(Path.Combine(candidate, "GodotMcpServer.cs")))
            {
                var result = LocalDirectoryFiles(candidate);
                Validate(result);
                return (result, "local:" + candidate);
            }
        throw new InvalidOperationException("Local --path must point to repository/addons/godot_mcp or addon ZIP");
    }

    private static void Validate(Dictionary<string, byte[]> files)
    {
        var missing = Required.Except(files.Keys).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException("Incomplete or unsupported backend version: " + string.Join(", ", missing));
    }

    private static (Dictionary<string, byte[]>, string) BundledAddon()
    {
        var assembly = typeof(BackendInstaller).Assembly;
        const string prefix = "godot_addon/";
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(prefix)))
        {
            using var content = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException("Missing embedded addon resource: " + name);
            using var buffer = new MemoryStream();
            content.CopyTo(buffer);
            files.Add(name[prefix.Length..], buffer.ToArray());
        }
        Validate(files);
        return (files, "embedded:C#-backend");
    }

    private static async Task<(byte[], string)> SourceArchiveAsync()
    {
        var url = $"https://codeload.github.com/{Repository}/zip/refs/heads/main";
        return (await FetchAsync(url), url);
    }

    private static async Task<(byte[], string)> ReleaseArchiveAsync(string version)
    {
        if (version != "latest" && !Regex.IsMatch(version, @"^[\w.\-]+$"))
            throw new ArgumentException("Invalid release tag");
        var url = version == "latest"
            ? $"https://api.github.com/repos/{Repository}/releases/latest"
            : $"https://api.github.com/repos/{Repository}/releases/tags/{version}";
        var data = await FetchAsync(url);
        using var doc = JsonDocument.Parse(data);
        var root = doc.RootElement;
        if (root.TryGetProperty("assets", out var assets))
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                    !(name.Contains("addon", StringComparison.OrdinalIgnoreCase) ||
                      name.Contains("plugin", StringComparison.OrdinalIgnoreCase)))
                    continue;
                var download = asset.GetProperty("browser_download_url").GetString()
                    ?? throw new InvalidOperationException("Missing Release asset URL");
                if (!download.StartsWith($"https://github.com/{Repository}/releases/download/", StringComparison.Ordinal))
                    throw new InvalidOperationException("Unexpected GitHub Release download URL");
                return (await FetchAsync(download), download);
            }
        var tag = root.GetProperty("tag_name").GetString();
        if (string.IsNullOrWhiteSpace(tag) || !Regex.IsMatch(tag, @"^[\w.\-]+$"))
            throw new InvalidOperationException("Release contains neither plugin ZIP nor valid source tag");
        var source = $"https://codeload.github.com/{Repository}/zip/refs/tags/{tag}";
        return (await FetchAsync(source), source);
    }

    private static async Task<byte[]> FetchAsync(string url)
    {
        if (!url.StartsWith("https://", StringComparison.Ordinal))
            throw new InvalidOperationException("HTTPS required for downloads");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("GodotRuntimeMcp/3.0");
        using var response = await DownloadClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxDownload)
            throw new InvalidOperationException("Downloaded archive is too large");
        await using var input = await response.Content.ReadAsStreamAsync();
        await using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int count;
        while ((count = await input.ReadAsync(buffer)) > 0)
        {
            if (output.Length + count > MaxDownload)
                throw new InvalidOperationException("Downloaded archive exceeds size limit");
            await output.WriteAsync(buffer.AsMemory(0, count));
        }
        return output.ToArray();
    }

    private static Dictionary<string, byte[]> ExtractAddon(byte[] bytes)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        long expanded = 0;
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            var parts = entry.FullName.Replace('\\', '/').Split('/');
            if (entry.FullName.StartsWith('/') || parts.Any(part => part is ".." or "." or "") ||
                ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidOperationException("Unsafe ZIP entry: " + entry.FullName);

            var i = Array.FindIndex(parts, p => p == "godot_mcp");
            string[] relative;
            if (i >= 1 && parts[i - 1] == "addons") relative = parts[(i + 1)..];
            else if (i >= 0) relative = parts[(i + 1)..];
            else if (parts.Length <= 2 && Required.Contains(parts[^1]))
                relative = [parts[^1]];
            else continue;
            if (relative.Length == 0 || relative.Any(p => p.Length == 0)) continue;
            var name = string.Join('/', relative);
            if (result.ContainsKey(name))
                throw new InvalidOperationException("Duplicate file in plugin archive: " + name);
            expanded += entry.Length;
            if (expanded > MaxUnpacked || result.Count >= MaxFiles)
                throw new InvalidOperationException("Plugin archive is too large");
            using var stream = entry.Open();
            using var output = new MemoryStream();
            stream.CopyTo(output);
            result.Add(name, output.ToArray());
        }
        Validate(result);
        return result;
    }
}
