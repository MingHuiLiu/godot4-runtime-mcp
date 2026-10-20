using System.CommandLine;
using System.CommandLine.Parsing;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GodotRuntimeMcp;

/// <summary>
/// System.CommandLine owns option parsing, types, missing values, help and subcommands.
/// The dynamic schema's JSON/Variant types are handled only at the tool boundary.
/// </summary>
public static class CommandLineApp
{
    private static string Kebab(string name)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var c in name)
        {
            if (c == '_') builder.Append('-');
            else if (char.IsUpper(c))
            {
                if (builder.Length > 0) builder.Append('-');
                builder.Append(char.ToLowerInvariant(c));
            }
            else builder.Append(c);
        }
        return builder.ToString();
    }

    private static void Print(JsonNode? value) =>
        Console.WriteLine(value?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null");

    private static void Print(InstallResult result) =>
        Console.WriteLine(JsonSerializer.Serialize(result, RuntimeJsonContext.Default.InstallResult));
    private static void Print(PackResult result) =>
        Console.WriteLine(JsonSerializer.Serialize(result, RuntimeJsonContext.Default.PackResult));

    private static async Task<int> Safely(Func<Task> action)
    {
        try
        {
            await action();
            return 0;
        }
        catch (OperationCanceledException) { return 130; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or
            HttpRequestException or JsonException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("godot-runtime-cli: " + ex.Message);
            return 1;
        }
    }

    private static Option<string?> StringOption(string name, string description = "") =>
        new($"--{name}") { Description = description };
    private static Option<bool> BoolOption(string name, string description = "") =>
        new($"--{name}") { Description = description };

    private static void AddSimple(RootCommand root, string name, string description, Func<Task<JsonNode?>> work)
    {
        var command = new Command(name, description);
        command.SetAction(async (_, _) => await Safely(async () =>
            Print(await work())));
        root.Subcommands.Add(command);
    }

    private sealed record FlagBinding(string Name, Func<ParseResult, string?> Read);

    private static void AddTool(RootCommand root, ToolService service, ToolDefinition entry)
    {
        var toolName = entry.Name;
        var command = new Command(Kebab(toolName), entry.Description);
        var props = entry.InputSchema.Properties;
        var bindings = new List<FlagBinding>();
        foreach (var (key, definition) in props)
        {
            var name = Kebab(key);
            var description = definition.Description ?? "";
            var type = definition.Type;
            if (type == "integer")
            {
                var option = new Option<long?>($"--{name}") { Description = description };
                command.Options.Add(option);
                bindings.Add(new FlagBinding(key, p => p.GetResult(option) is null
                    ? null : p.GetValue(option)?.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
            else if (type == "boolean")
            {
                var option = new Option<string?>($"--{name}") { Description = description,
                    Arity = ArgumentArity.ZeroOrOne };
                var negation = new Option<bool>($"--no-{name}")
                { Description = "Set " + name + " to false" };
                command.Options.Add(option);
                command.Options.Add(negation);
                bindings.Add(new FlagBinding(key, p =>
                {
                    if (p.GetValue(negation)) return "false";
                    if (p.GetResult(option) is null) return null;
                    return p.GetValue(option) ?? "true";
                }));
            }
            else
            {
                var option = StringOption(name, description);
                command.Options.Add(option);
                bindings.Add(new FlagBinding(key, p => p.GetValue(option)));
            }
        }
        var json = StringOption("json", "Optional JSON object; explicit flags override it");
        command.Options.Add(json);
        command.SetAction(async (parsed, _) => await Safely(async () =>
        {
            var tokens = new List<string>();
            if (parsed.GetValue(json) is { } raw) { tokens.Add("--json"); tokens.Add(raw); }
            foreach (var binding in bindings)
            {
                if (binding.Read(parsed) is { } value)
                {
                    tokens.Add("--" + Kebab(binding.Name));
                    tokens.Add(value);
                }
            }
            var args = ToolCommandLine.Parse(service, toolName, tokens);
            Print(await service.RunAsync(toolName, args));
        }));
        root.Subcommands.Add(command);
    }

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 && Path.GetFileName(Environment.ProcessPath) == "godot-mcp-server")
            args = ["mcp"];
        if (args is ["--version"] or ["version"])
        {
            Console.WriteLine($"godot-runtime-cli {HostVersion.Current} (C#/.NET 8)");
            return 0;
        }
        var service = new ToolService();
        var root = new RootCommand("Godot 4 .NET C# runtime CLI, MCP stdio and HTTP server.");
        root.SetAction((_, _) =>
        {
            Console.WriteLine("Use 'godot-runtime-cli --help' or 'tools --search NAME' to discover commands.");
            return Task.FromResult(0);
        });

        foreach (var entry in service.CatalogModels)
            AddTool(root, service, entry);

        AddSimple(root, "status", "Projects and verified running Godot processes",
            () => service.RunAsync("godot_status", new JsonObject()));
        AddSimple(root, "instances", "List verified running Godot processes",
            () => service.RunAsync("godot_list_instances", new JsonObject()));
        AddSimple(root, "projects", "List registered Godot projects",
            () => service.RunAsync("godot_list_projects", new JsonObject()));
        AddSimple(root, "guide", "Offline installation and usage help",
            () => service.RunAsync("godot_setup_guide", new JsonObject()));

        var toolsCmd = new Command("tools", "Search or print the 51 tools");
        var search = StringOption("search", "Filter tool names/descriptions");
        var asJson = BoolOption("json", "Print the raw MCP tool catalog as JSON");
        toolsCmd.Options.Add(search);
        toolsCmd.Options.Add(asJson);
        toolsCmd.SetAction(p =>
        {
            if (p.GetValue(asJson)) Print(service.Catalog());
            else Console.Write(ToolCommandLine.CatalogText(service, p.GetValue(search)));
        });
        root.Subcommands.Add(toolsCmd);

        var help = new Command("help", "Show CLI or tool-specific usage");
        var target = new Argument<string?>("tool") { Arity = ArgumentArity.ZeroOrOne };
        help.Arguments.Add(target);
        help.SetAction(p =>
        {
            var name = p.GetValue(target);
            if (name == null)
                Console.WriteLine("Use 'godot-runtime-cli --help' to see all commands.");
            else if (ToolCommandLine.Find(service, name) is { } entry)
                Console.Write(ToolCommandLine.Describe(entry));
            else
                Console.Write(ToolCommandLine.CatalogText(service, name));
        });
        root.Subcommands.Add(help);

        var install = new Command("install", "Install or upgrade the Godot C# runtime backend");
        var projectOpt = StringOption("project", "Absolute path to a Godot 4 .NET game project");
        var sourceOpt = StringOption("source", "auto|release|github|local");
        var versionOpt = StringOption("version", "Release version/tag (latest by default)");
        var pathOpt = StringOption("path", "Local addon ZIP or repository");
        var forceOpt = BoolOption("force", "Back up and replace differing existing addon");
        projectOpt.Required = true;
        install.Options.Add(projectOpt);
        install.Options.Add(sourceOpt);
        install.Options.Add(versionOpt);
        install.Options.Add(pathOpt);
        install.Options.Add(forceOpt);
        install.SetAction(async (p, _) => await Safely(async () =>
        {
            Print(await BackendInstaller.InstallAsync(p.GetValue(projectOpt)!,
                p.GetValue(sourceOpt) ?? "auto", p.GetValue(versionOpt) ?? "latest",
                p.GetValue(pathOpt), p.GetValue(forceOpt)));
        }));
        root.Subcommands.Add(install);

        var pack = new Command("pack", "Package the Godot C# addon into a ZIP");
        var packPath = StringOption("path", "Repository or addon directory");
        var outPath = StringOption("out", "Output ZIP path");
        pack.Options.Add(packPath);
        pack.Options.Add(outPath);
        pack.SetAction(async (p, _) => await Safely(async () =>
        {
            Print(await BackendInstaller.PackAsync(p.GetValue(packPath) ?? ".",
                p.GetValue(outPath) ?? "godot-runtime-addon.zip"));
        }));
        root.Subcommands.Add(pack);

        var mcp = new Command("mcp", "Start official MCP stdio transport");
        var mcpProject = StringOption("project");
        var mcpInstance = StringOption("instance");
        mcp.Options.Add(mcpProject);
        mcp.Options.Add(mcpInstance);
        mcp.SetAction(async (p, ct) =>
        {
            await McpHost.RunStdioAsync(new ToolService(p.GetValue(mcpProject), p.GetValue(mcpInstance)), ct);
            return 0;
        });
        root.Subcommands.Add(mcp);

        var serve = new Command("serve", "Start official MCP Streamable HTTP on localhost");
        var port = new Option<int>("--port") { Description = "Loopback TCP port" };
        var token = StringOption("token", "Bearer token; auto-generated if absent");
        var srvProject = StringOption("project");
        var srvInstance = StringOption("instance");
        serve.Options.Add(port);
        serve.Options.Add(token);
        serve.Options.Add(srvProject);
        serve.Options.Add(srvInstance);
        serve.SetAction(async (p, ct) =>
        {
            var portValue = p.GetResult(port) is null ? 18777 : p.GetValue(port);
            if (portValue is < 1 or > 65535)
                throw new ArgumentOutOfRangeException(nameof(port), "Expected TCP port 1-65535");
            var supplied = p.GetValue(token) ?? Environment.GetEnvironmentVariable("GODOT_MCP_HTTP_TOKEN");
            var bearer = supplied ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
            if (supplied is null) Console.Error.WriteLine("Generated local MCP bearer token: " + bearer);
            Console.Error.WriteLine($"MCP HTTP listening on http://127.0.0.1:{portValue}/mcp");
            await McpHost.RunHttpAsync(new ToolService(p.GetValue(srvProject), p.GetValue(srvInstance)),
                portValue, bearer, ct);
            return 0;
        });
        root.Subcommands.Add(serve);

        // Compatibility; direct tool subcommands are preferred.
        var legacyCall = new Command("call", "Legacy wrapper: call TOOL --args '{...}'");
        var calledTool = new Argument<string>("tool");
        var legacyJson = StringOption("args", "JSON object with tool arguments");
        var legacyProject = StringOption("project");
        var legacyInstance = StringOption("instance");
        legacyCall.Arguments.Add(calledTool);
        legacyCall.Options.Add(legacyJson);
        legacyCall.Options.Add(legacyProject);
        legacyCall.Options.Add(legacyInstance);
        legacyCall.SetAction(async (p, _) => await Safely(async () =>
        {
            var name = p.GetValue(calledTool);
            var node = JsonNode.Parse(p.GetValue(legacyJson) ?? "{}") as JsonObject
                ?? throw new ArgumentException("--args must be a JSON object");
            if (p.GetValue(legacyProject) is { } selectedProject) node["project"] = selectedProject;
            if (p.GetValue(legacyInstance) is { } selectedInstance) node["instance"] = selectedInstance;
            Print(await service.RunAsync(ToolCommandLine.Resolve(service, name!) ?? name!, node));
        }));
        root.Subcommands.Add(legacyCall);

        try { return await root.Parse(args).InvokeAsync(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
            or JsonException or IOException or HttpRequestException)
        {
            Console.Error.WriteLine("godot-runtime-cli: " + ex.Message);
            return 1;
        }
    }
}
