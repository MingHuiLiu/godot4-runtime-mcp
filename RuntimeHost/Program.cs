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

    private static Dictionary<string, string> Options(IReadOnlyList<string> args, params string[] booleanOptions)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Count; i++)
        {
            var token = args[i];
            if (!token.StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Unexpected argument '{token}'. Try '--help'.");
            var body = token[2..];
            var equals = body.IndexOf('=');
            var name = equals < 0 ? body : body[..equals];
            if (!result.TryAdd(name, ""))
                throw new ArgumentException("Duplicate option --" + name);
            if (booleanOptions.Contains(name))
                result[name] = equals >= 0 ? body[(equals + 1)..] : "true";
            else if (equals >= 0)
                result[name] = body[(equals + 1)..];
            else if (++i < args.Count)
                result[name] = args[i];
            else
                throw new ArgumentException("Missing value for --" + name);
        }
        return result;
    }

    private static void RejectUnknown(Dictionary<string, string> options, params string[] allowed)
    {
        foreach (var name in options.Keys)
            if (!allowed.Contains(name))
                throw new ArgumentException($"Unknown option --{name}. Run 'godot-runtime-cli help'.");
    }

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 0 && Path.GetFileName(Environment.ProcessPath) == "godot-mcp-server")
                args = ["mcp"];
            if (args.Length == 0 || args[0] is "--help" or "-h")
            {
                Help();
                return 0;
            }
            if (args[0] is "--version" or "version")
            {
                Console.WriteLine($"godot-runtime-cli {HostVersion.Current} (C#/.NET 8)");
                return 0;
            }

            var tools = new ToolService();
            var command = args[0];
            if (command == "help")
            {
                if (args.Length == 1) Help();
                else if (args.Length == 2)
                {
                    var tool = ToolCommandLine.Find(tools, args[1]);
                    if (tool != null) Console.Write(ToolCommandLine.Describe(tool));
                    else if (args[1] == "all") Console.Write(ToolCommandLine.CatalogText(tools));
                    else if (BuiltinHelp(args[1])) { }
                    else throw new ArgumentException($"Unknown tool '{args[1]}'. Run 'godot-runtime-cli tools --search NAME'.");
                }
                else throw new ArgumentException("Usage: godot-runtime-cli help [TOOL]");
                return 0;
            }
            if (command == "tools")
            {
                if (args.Length == 2 && args[1] is "--help" or "-h")
                {
                    Console.WriteLine("Usage: godot-runtime-cli tools [--search TERM] [--json]");
                    return 0;
                }
                var opt = Options(args[1..], "json");
                RejectUnknown(opt, "search", "json");
                if (opt.ContainsKey("json"))
                    Print(tools.Catalog());
                else
                    Console.Write(ToolCommandLine.CatalogText(tools, opt.GetValueOrDefault("search")));
                return 0;
            }

            // New flat commands map 1:1 to all 51 MCP tools; both snake_case and kebab-case work.
            // Legacy "call TOOL --args '{...}'" remains supported without a second execution engine.
            if (command == "call")
            {
                if (args.Length < 2) throw new ArgumentException("Usage: godot-runtime-cli call TOOL [--parameter VALUE]");
                command = args[1];
                var legacy = args[2..].Select(a => a.StartsWith("--args=", StringComparison.Ordinal)
                    ? "--json=" + a[7..]
                    : a == "--args" ? "--json" : a).ToArray();
                args = [command, .. legacy];
            }
            var toolName = ToolCommandLine.Resolve(tools, command);
            if (toolName != null)
            {
                if (args.Skip(1).Any(a => a is "--help" or "-h"))
                {
                    Console.Write(ToolCommandLine.Describe(ToolCommandLine.Find(tools, command)!));
                    return 0;
                }
                var values = ToolCommandLine.Parse(tools, command, args[1..]);
                Print(await tools.RunAsync(toolName, values));
                return 0;
            }

            if (args.Skip(1).Any(a => a is "--help" or "-h"))
            {
                if (!BuiltinHelp(command)) Help();
                return 0;
            }

            var opts = Options(args[1..], "force", "json");
            string? Opt(string key) => opts.GetValueOrDefault(key);
            var project = Opt("project");
            var instance = Opt("instance");
            switch (command)
            {
                case "mcp":
                case "stdio":
                    RejectUnknown(opts, "project", "instance");
                    await new McpProtocol(new ToolService(project, instance)).RunStdioAsync();
                    return 0;
                case "serve":
                {
                    RejectUnknown(opts, "port", "token", "project", "instance");
                    var port = int.TryParse(Opt("port"), out var parsed) ? parsed : 18777;
                    if (port < 1 || port > 65535)
                        throw new ArgumentException("Invalid --port");
                    var token = Opt("token") ?? Environment.GetEnvironmentVariable("GODOT_MCP_HTTP_TOKEN")
                        ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
                    await new McpProtocol(new ToolService(project, instance)).RunHttpAsync(port, token);
                    return 0;
                }
                case "status":
                    RejectUnknown(opts);
                    Print(await tools.RunAsync("godot_status", new JsonObject()));
                    return 0;
                case "instances":
                    RejectUnknown(opts);
                    Print(await tools.RunAsync("godot_list_instances", new JsonObject()));
                    return 0;
                case "projects":
                    RejectUnknown(opts);
                    Print(await tools.RunAsync("godot_list_projects", new JsonObject()));
                    return 0;
                case "guide":
                    RejectUnknown(opts);
                    Print(await tools.RunAsync("godot_setup_guide", new JsonObject()));
                    return 0;
                case "install":
                {
                    RejectUnknown(opts, "project", "source", "version", "path", "force");
                    if (string.IsNullOrWhiteSpace(project))
                        throw new ArgumentException("Missing --project PATH. Example: godot-runtime-cli install --project /path/to/game");
                    Print(await BackendInstaller.InstallAsync(project,
                        Opt("source") ?? "auto", Opt("version") ?? "latest",
                        Opt("path"), Opt("force") == "true"));
                    return 0;
                }
                case "pack":
                    RejectUnknown(opts, "path", "out");
                    Print(await BackendInstaller.PackAsync(Opt("path") ?? ".",
                        Opt("out") ?? "godot-runtime-addon.zip"));
                    return 0;
                default:
                    throw new ArgumentException($"Unknown command '{command}'. Run 'godot-runtime-cli tools --search TERM' or 'help'.");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("godot-runtime-cli: " + ex.Message);
            return 1;
        }
    }

    private static bool BuiltinHelp(string command)
    {
        var detail = command switch
        {
            "install" => """
                Install or upgrade the C# backend in a Godot 4 .NET project.
                Usage: godot-runtime-cli install --project PATH [--source SOURCE] [--version TAG] [--path ZIP] [--force]
                  --project PATH   Required existing Godot project directory
                  --source        auto (default), release, github, local
                  --version TAG   Release tag (default: latest)
                  --path ZIP      Local ZIP/repository when source=local
                  --force         Back up and replace an existing different addon
                Example: godot-runtime-cli install --project /Games/MyGame
                """,
            "pack" => """
                Pack canonical Godot addon into a ZIP suitable for a Release.
                Usage: godot-runtime-cli pack [--path REPO_OR_ADDON] [--out ZIP]
                Example: godot-runtime-cli pack --path . --out godot-runtime-addon.zip
                """,
            "mcp" or "stdio" => """
                Start independent MCP JSON-RPC stdio server. Stdout is protocol-only.
                Usage: godot-runtime-cli mcp [--project PATH] [--instance ID]
                For the released binary, use godot-mcp-server with no arguments.
                """,
            "serve" => """
                Start local authenticated MCP HTTP JSON-RPC POST service.
                Usage: godot-runtime-cli serve [--port 18777] [--token SECRET] [--project PATH] [--instance ID]
                Binds only http://127.0.0.1:PORT/mcp; not for public Internet.
                """,
            "status" => "Usage: godot-runtime-cli status — list known Godot projects and live processes.",
            "projects" => "Usage: godot-runtime-cli projects — list registered Godot projects.",
            "instances" => "Usage: godot-runtime-cli instances — list verified running Godot game processes.",
            "guide" => "Usage: godot-runtime-cli guide — output offline setup guidance.",
            "version" => "Usage: godot-runtime-cli version — print executable version.",
            _ => null
        };
        if (detail == null) return false;
        Console.WriteLine(detail);
        return true;
    }

    private static void Help() => Console.WriteLine("""
        godot-runtime-cli — Godot 4 .NET runtime tools (standalone C# Native AOT).

        Getting started:
          godot-runtime-cli status
          godot-runtime-cli install --project /path/to/game
          godot-runtime-cli instances
          godot-runtime-cli get-scene-tree-simple --max-depth 2 --project /path/to/game
          godot-runtime-cli get-node-info --node-path /root/Main/Player --instance ID
          godot-runtime-cli set-property --node-path /root/Main/Player --property-name visible --value false
          godot-runtime-cli get-signal-events --count 20 --instance ID

        Discover all 51 tool commands:
          tools                      Human-readable command list (offline)
          tools --search scene       Search command names and descriptions
          tools --json               Complete MCP tool catalog as JSON
          help get-node-info         Required options, types and examples
          get-node-info --help       Same per-command help

        Management and service:
          status | projects | instances | guide | version
          install --project PATH [--source auto|release|github|local] [--path ZIP] [--force]
          pack [--path REPO] [--out ZIP]
          mcp [--project PATH] [--instance ID]      MCP stdio (godot-mcp-server runs this by default)
          serve [--port 18777] [--token SECRET]     Local authenticated MCP HTTP
          call TOOL [--args JSON]                   Older syntax, still supported

        Tool names accept both kebab-case and snake_case; typed --parameter flags
        are generated from the shared tool manifest. Boolean flags accept --flag,
        --no-flag or --flag false. Arrays/objects accept JSON literals.
        --json '{"parameter":"value"}' merges an optional raw JSON object with flags.
        Tool execution selects a unique live game instance, or returns a clear error.
        Godot need not be running for management, discovery or installation.
        """);
}
