using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using GodotRuntimeMcp;

return await Cli.RunAsync(args);

internal static class Cli
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    private static void Print(JsonNode? value) =>
        Console.Out.WriteLine(value?.ToJsonString(Pretty) ?? "null");

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            // Both release executables contain the same native host. The server-named
            // binary launches stdio MCP without requiring the user to pass "mcp".
            if (args.Length == 0 && Path.GetFileName(Environment.ProcessPath) == "godot-mcp-server")
                args = ["mcp"];
            if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
            {
                Help();
                return 0;
            }
            if (args[0] is "--version" or "version")
            {
                Console.WriteLine($"godot-runtime-mcp {HostVersion.Current} (C#/.NET 8)");
                return 0;
            }
            var command = args[0];
            var positional = new List<string>();
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 1; i < args.Length; i++)
            {
                var item = args[i];
                if (item == "--force") { options["force"] = "true"; continue; }
                if (!item.StartsWith("--", StringComparison.Ordinal))
                {
                    positional.Add(item);
                    continue;
                }
                if (i + 1 >= args.Length)
                    throw new ArgumentException("Missing value for " + item);
                options[item[2..]] = args[++i];
            }
            string? Opt(string key) => options.GetValueOrDefault(key);
            var project = Opt("project");
            var instance = Opt("instance");
            var tools = new ToolService(project, instance);
            var service = new McpProtocol(tools);

            switch (command)
            {
                case "mcp":
                case "stdio":
                    await service.RunStdioAsync();
                    return 0;
                case "serve":
                {
                    var port = int.TryParse(Opt("port"), out var parsed) ? parsed : 18777;
                    if (port < 1 || port > 65535)
                        throw new ArgumentException("Invalid --port");
                    var token = Opt("token") ?? Environment.GetEnvironmentVariable("GODOT_MCP_HTTP_TOKEN")
                        ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
                    await service.RunHttpAsync(port, token);
                    return 0;
                }
                case "status":
                    Print(await tools.RunAsync("godot_status", new JsonObject()));
                    return 0;
                case "instances":
                    Print(await tools.RunAsync("godot_list_instances", new JsonObject()));
                    return 0;
                case "projects":
                    Print(await tools.RunAsync("godot_list_projects", new JsonObject()));
                    return 0;
                case "tools":
                    Print(tools.Catalog());
                    return 0;
                case "guide":
                    Print(await tools.RunAsync("godot_setup_guide", new JsonObject()));
                    return 0;
                case "install":
                {
                    if (string.IsNullOrWhiteSpace(project))
                        throw new ArgumentException("--project PATH is required for installation");
                    Print(await BackendInstaller.InstallAsync(project,
                        Opt("source") ?? "auto",
                        Opt("version") ?? "latest",
                        Opt("path"),
                        Opt("force") == "true"));
                    return 0;
                }
                case "pack":
                    Print(await BackendInstaller.PackAsync(Opt("path") ?? ".",
                        Opt("out") ?? "godot-mcp-addon.zip"));
                    return 0;
                case "call":
                {
                    if (positional.Count == 0)
                        throw new ArgumentException("Usage: godot-mcp call TOOL [--args '{...}'] [--project PATH] [--instance ID]");
                    JsonObject operation;
                    try { operation = JsonNode.Parse(Opt("args") ?? "{}") as JsonObject
                        ?? throw new ArgumentException("--args must be a JSON object"); }
                    catch (JsonException e) { throw new ArgumentException("Invalid --args JSON: " + e.Message); }
                    Print(await tools.RunAsync(positional[0], operation));
                    return 0;
                }
                default:
                    throw new ArgumentException("Unknown command: " + command);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("godot-mcp: " + ex.Message);
            return 1;
        }
    }

    private static void Help() => Console.WriteLine("""
        Godot Runtime MCP v3 — C# / .NET, one backend for CLI, Skills, MCP and HTTP.

        Commands (Godot need not be installed or running for management):
          help | version
          status                 Projects and live game instances
          projects               Projects registered by installation
          instances              Verified live instances; each process has an ID
          tools                  MCP tool catalog (available offline)
          guide                  Offline setup and quick usage
          install --project PATH [--source auto|release|github|local]
                   [--version TAG] [--path LOCAL_REPO_OR_ZIP] [--force]
          pack [--path REPO] [--out plugin.zip]  Prepare GitHub Release ZIP
          call TOOL [--args JSON] [--project PATH] [--instance INSTANCE_ID]
          mcp [--project PATH] [--instance ID]   MCP stdio, standalone C# process
          serve [--port 18777] [--token SECRET]  Optional local Streamable HTTP MCP

        Install prefers GitHub Releases; the auto mode uses the embedded
        matching C# backend when a latest Release cannot be downloaded.
        If a project has multiple live game processes, --instance is mandatory.
        Neither Godot nor a running plugin is required to start MCP, list tools or install.
        """);
}
