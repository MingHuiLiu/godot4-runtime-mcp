using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GodotRuntimeMcp;

/// <summary>One implementation of every tool for all transports (CLI/MCP stdio/HTTP).</summary>
public sealed class ToolService
{
    private static readonly JsonSerializerOptions Pretty = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string? _project;
    private readonly string? _instance;
    private readonly JsonArray _tools;
    private readonly HashSet<string> _runtimeNames;

    public ToolService(string? project = null, string? instance = null)
    {
        _project = project;
        _instance = instance;
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceNames()
            .Where(n => n.EndsWith("tool_manifest.json", StringComparison.Ordinal))
            .Select(n => Assembly.GetExecutingAssembly().GetManifestResourceStream(n))
            .FirstOrDefault() ?? throw new InvalidOperationException("Embedded Godot tool manifest missing");
        using var reader = new StreamReader(stream);
        var runtimeTools = JsonNode.Parse(reader.ReadToEnd())!.AsArray();
        _runtimeNames = runtimeTools.Select(t => t!["name"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        _tools = new JsonArray();
        foreach (var source in runtimeTools)
        {
            var tool = source!.DeepClone();
            var properties = tool["inputSchema"]!["properties"]!.AsObject();
            properties["project"] = new JsonObject
            {
                ["type"] = "string", ["description"] = "Absolute Godot project path (selects specific project)"
            };
            properties["instance"] = new JsonObject
            {
                ["type"] = "string", ["description"] = "Running game instance ID or unique prefix"
            };
            _tools.Add(tool);
        }
        foreach (var def in new[]
        {
            ("godot_status", "List registered projects and verified live Godot processes, even when backend is offline"),
            ("godot_list_instances", "List all active Godot game instances with IDs and project paths"),
            ("godot_list_projects", "List all registered projects, regardless of running games"),
            ("godot_setup_guide", "Read offline installation, MCP and CLI instructions"),
            ("godot_install_backend", "Download and install Godot addon to an existing project; Release-first"),
        })
        {
            var props = new JsonObject();
            if (def.Item1 == "godot_install_backend")
            {
                props["project"] = new JsonObject { ["type"] = "string", ["description"] = "Project path (required)" };
                props["source"] = new JsonObject
                {
                    ["type"] = "string", ["enum"] = new JsonArray(JsonValue.Create("auto"), JsonValue.Create("release"), JsonValue.Create("github"), JsonValue.Create("local"))
                };
                props["version"] = new JsonObject { ["type"] = "string" };
                props["path"] = new JsonObject { ["type"] = "string", ["description"] = "Local addon/repo/ZIP path" };
                props["force"] = new JsonObject { ["type"] = "boolean" };
            }
            var schema = new JsonObject { ["type"] = "object", ["properties"] = props };
            if (def.Item1 == "godot_install_backend")
                schema["required"] = new JsonArray("project");
            _tools.Add((JsonNode)new JsonObject
            {
                ["name"] = def.Item1,
                ["description"] = def.Item2,
                ["inputSchema"] = schema
            });
        }
    }

    public JsonArray Catalog() => (JsonArray)_tools.DeepClone();

    private static string? Str(JsonObject args, string key) =>
        args[key] is JsonValue v && v.TryGetValue<string>(out var str) ? str : null;

    private static bool Bool(JsonObject args, string key) =>
        args[key] is JsonValue v && v.TryGetValue<bool>(out var val) && val;

    public async Task<JsonNode?> RunAsync(string name, JsonObject args)
    {
        if (name == "godot_status")
        {
            var live = await RuntimeRegistry.LiveAsync();
            var projects = new JsonArray();
            foreach (var path in RuntimeRegistry.Projects())
                projects.Add((JsonNode)new JsonObject
                {
                    ["path"] = path,
                    ["backendInstalled"] = File.Exists(Path.Combine(path, "addons", "godot_mcp", "GodotRuntimeTransport.cs"))
                });
            var instances = new JsonArray();
            foreach (var entry in live) instances.Add(entry.Public);
            return new JsonObject { ["projects"] = projects, ["instances"] = instances };
        }
        if (name == "godot_list_instances")
        {
            var instances = new JsonArray();
            foreach (var entry in await RuntimeRegistry.LiveAsync()) instances.Add(entry.Public);
            return instances;
        }
        if (name == "godot_list_projects")
        {
            var projects = new JsonArray();
            foreach (var path in RuntimeRegistry.Projects()) projects.Add((JsonNode?)JsonValue.Create(path));
            return projects;
        }
        if (name == "godot_setup_guide")
            return new JsonObject
            {
                ["offline"] = "CLI and MCP tool discovery/installation do not require any running Godot game.",
                ["install"] = "godot-mcp install --project /path/to/game",
                ["localInstall"] = "godot-mcp install --project /path/to/game --source local --path .",
                ["build"] = "Use Godot 4 .NET and build the game's C# project with dotnet build.",
                ["runtime"] = "Launch one or more games. Each binds a unique loopback port; use instances to select.",
                ["mcp"] = "godot-mcp-server (native release binary), or godot-mcp mcp",
                ["cli"] = "godot-mcp call get_scene_tree_simple --project /path/to/game --instance ID",
                ["releases"] = "https://github.com/MingHuiLiu/godot4-runtime-mcp/releases"
            };
        if (name == "godot_install_backend")
        {
            var path = Str(args, "project") ?? _project ??
                throw new ArgumentException("godot_install_backend requires project");
            return await BackendInstaller.InstallAsync(path, Str(args, "source") ?? "auto",
                Str(args, "version") ?? "latest", Str(args, "path"), Bool(args, "force"));
        }
        if (!_runtimeNames.Contains(name))
            throw new ArgumentException("Unknown MCP tool: " + name);

        var project = Str(args, "project") ?? _project;
        var instance = Str(args, "instance") ?? _instance;
        var operationArgs = (JsonObject)args.DeepClone();
        operationArgs.Remove("project");
        operationArgs.Remove("instance");
        using var serialized = JsonDocument.Parse(operationArgs.ToJsonString());
        var result = await RuntimeRegistry.CallAsync(name, serialized.RootElement.Clone(), project, instance);
        return JsonNode.Parse(result.GetRawText());
    }

    public bool Exists(string name) => _tools.Any(t => t!["name"]!.GetValue<string>() == name);
    public static string Format(JsonNode? value) => value?.ToJsonString(Pretty) ?? "null";
}

public sealed class McpProtocol(ToolService tools)
{
    private const string DefaultProtocol = "2025-06-18";
    private static readonly HashSet<string> Supported = ["2024-11-05", "2025-03-26", DefaultProtocol];

    private static JsonObject Error(JsonNode? id, int code, string message) =>
        new()
        {
            ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
        };

    private static JsonObject Result(JsonNode? id, JsonNode? result) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["result"] = result };

    public async Task<JsonObject?> DispatchAsync(string payload)
    {
        JsonNode? parsed;
        try { parsed = JsonNode.Parse(payload); }
        catch (JsonException) { return Error(null, -32700, "Invalid JSON"); }

        if (parsed is not JsonObject request || request["jsonrpc"]?.ToString() != "2.0" ||
            request["method"] is not JsonValue methodNode ||
            !methodNode.TryGetValue<string>(out var method))
            return Error(null, -32600, "Invalid JSON-RPC request");
        var id = request["id"];
        // Notifications never receive responses (even when the Godot backend is offline).
        if (id == null) return null;
        if (request["params"] != null && request["params"] is not JsonObject)
            return Error(id, -32602, "params must be a JSON object");
        var args = request["params"] as JsonObject ?? new JsonObject();
        try
        {
            switch (method)
            {
                case "initialize":
                {
                    var requested = args["protocolVersion"]?.ToString();
                    var version = requested != null && Supported.Contains(requested)
                        ? requested : DefaultProtocol;
                    return Result(id, new JsonObject
                    {
                        ["protocolVersion"] = version,
                        ["capabilities"] = new JsonObject
                        {
                            ["tools"] = new JsonObject { ["listChanged"] = false }
                        },
                        ["serverInfo"] = new JsonObject
                        {
                            ["name"] = "godot-runtime-mcp", ["version"] = "3.0.0"
                        }
                    });
                }
                case "ping": return Result(id, new JsonObject());
                case "tools/list": return Result(id, new JsonObject { ["tools"] = tools.Catalog() });
                case "tools/call":
                {
                    var name = args["name"]?.ToString();
                    if (string.IsNullOrWhiteSpace(name))
                        return Error(id, -32602, "Missing tool name");
                    if (!tools.Exists(name))
                        return Error(id, -32602, "Unknown tool: " + name);
                    if (args["arguments"] != null && args["arguments"] is not JsonObject)
                        return Error(id, -32602, "Tool arguments must be a JSON object");
                    try
                    {
                        var value = await tools.RunAsync(name, args["arguments"] as JsonObject ?? new JsonObject());
                        return Result(id, new JsonObject
                        {
                            ["content"] = new JsonArray(new JsonObject
                            {
                                ["type"] = "text", ["text"] = ToolService.Format(value)
                            }),
                            ["isError"] = false
                        });
                    }
                    catch (Exception ex)
                    {
                        return Result(id, new JsonObject
                        {
                            ["content"] = new JsonArray(new JsonObject
                            {
                                ["type"] = "text", ["text"] = ex.Message
                            }),
                            ["isError"] = true
                        });
                    }
                }
                default: return Error(id, -32601, "Method not found: " + method);
            }
        }
        catch (Exception e)
        {
            return Error(id, -32603, e.Message);
        }
    }

    public async Task RunStdioAsync()
    {
        // Do not write banners/progress to stdout: it is exclusively JSON-RPC.
        while (await Console.In.ReadLineAsync() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var response = await DispatchAsync(line);
            if (response != null)
            {
                await Console.Out.WriteLineAsync(response.ToJsonString());
                await Console.Out.FlushAsync();
            }
        }
    }

    /// <summary>Optional stateless Streamable HTTP MCP endpoint for clients preferring a local service.</summary>
    public async Task RunHttpAsync(int port, string token, CancellationToken stopping = default)
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        Console.Error.WriteLine($"[GodotRuntimeMcp] http://127.0.0.1:{port}/mcp ; Authorization: Bearer {token}");
        while (listener.IsListening && !stopping.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync().WaitAsync(stopping); }
            catch (OperationCanceledException) { break; }
            catch (HttpListenerException) { break; }
            _ = HandleHttpAsync(ctx, token);
        }
    }

    private async Task HandleHttpAsync(HttpListenerContext ctx, string token)
    {
        try
        {
            var req = ctx.Request;
            if (req.RemoteEndPoint?.Address.Equals(IPAddress.Loopback) != true ||
                req.Url?.AbsolutePath != "/mcp")
            {
                await ReplyAsync(ctx.Response, 404, "{\"error\":\"Not found\"}");
                return;
            }
            // Protect against cross-origin browser requests to localhost.
            if (req.Headers["Origin"] is { } origin &&
                !origin.StartsWith("http://localhost:", StringComparison.OrdinalIgnoreCase) &&
                !origin.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase))
            {
                await ReplyAsync(ctx.Response, 403, "{\"error\":\"Forbidden Origin\"}");
                return;
            }
            if (req.Headers["Authorization"] != "Bearer " + token)
            {
                await ReplyAsync(ctx.Response, 401, "{\"error\":\"Unauthorized\"}");
                return;
            }
            if (req.HttpMethod != "POST")
            {
                ctx.Response.Headers["Allow"] = "POST";
                await ReplyAsync(ctx.Response, 405, "{\"error\":\"Use POST /mcp\"}");
                return;
            }
            if (req.ContentLength64 > 1024 * 1024)
            {
                await ReplyAsync(ctx.Response, 413, "{\"error\":\"Request too large\"}");
                return;
            }
            using var reader = new StreamReader(req.InputStream, Encoding.UTF8);
            var buffer = new char[4096];
            var body = new StringBuilder();
            int read;
            while ((read = await reader.ReadAsync(buffer)) > 0)
            {
                body.Append(buffer, 0, read);
                if (body.Length > 1024 * 1024)
                {
                    await ReplyAsync(ctx.Response, 413, "{\"error\":\"Request too large\"}");
                    return;
                }
            }
            var result = await DispatchAsync(body.ToString());
            if (result == null)
                await ReplyAsync(ctx.Response, 202, "");
            else
                await ReplyAsync(ctx.Response, 200, result.ToJsonString());
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("[GodotRuntimeMcp] HTTP request failed: " + e.Message);
            try { await ReplyAsync(ctx.Response, 500, "{\"error\":\"Internal server error\"}"); } catch { }
        }
    }

    private static async Task ReplyAsync(HttpListenerResponse resp, int code, string data)
    {
        var bytes = Encoding.UTF8.GetBytes(data);
        resp.StatusCode = code;
        resp.ContentType = "application/json; charset=utf-8";
        resp.ContentLength64 = bytes.Length;
        await resp.OutputStream.WriteAsync(bytes);
        resp.Close();
    }
}
