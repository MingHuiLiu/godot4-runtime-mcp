using Godot;
using GodotRuntimeMcp.Recording;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FileAccess = Godot.FileAccess;

// =========================================================================
// GodotRuntimeBackend — Godot operations exposed through per-process runtime transport
//
// MCP and CLI live outside the engine; all Godot access runs on the main thread.
// =========================================================================

public partial class GodotRuntimeBackend : Node
{
    // ===================== Singleton =====================

    private static GodotRuntimeBackend? _instance;
    public static GodotRuntimeBackend? Instance => _instance;

    // ===================== Constants =====================


    // Logging constants
    private const int MaxLogBufferSize = 1000;
    private const string LogFilePath = "user://mcp_logs.txt";

    // Signal monitoring constants
    private const int MaxSignalEventsBufferSize = 5000;
    private const string SignalEventsFilePath = "user://mcp_signal_events.txt";

    // JSON options
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    // ===================== State =====================

    // Logging
    private readonly LinkedList<LogEntry> _logBuffer = new();
    private readonly List<LogEntry> _logs = new(); // backward compat

    // Signal monitoring
    private readonly List<SignalEvent> _signalEventsBuffer = new();
    private bool _isGlobalSignalMonitoring;
    private readonly HashSet<string> _monitoredSignals = new();

    // Single canonical tool catalog shared with the C# MCP / CLI service.
    private static object LoadToolManifest()
    {
        var json = FileAccess.GetFileAsString("res://addons/godot_mcp/tool_manifest.json");
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    // The backend owns Godot operations, not the MCP protocol or stdio transport.
    // Every running game registers an independent, authenticated loopback endpoint.
    private GodotRuntimeTransport? _transport;
    private CaptureJobController? _capture;

    public override void _Ready()
    {
        if (System.Environment.GetEnvironmentVariable("GODOT_MCP_EXPORT_WORKER") == "1")
        {
            SetProcess(false);
            return;
        }
        if (_instance != null) { QueueFree(); return; }
        _instance = this;
        ProcessMode = ProcessModeEnum.Always;
        _capture = new CaptureJobController(this);
        InitializeLogFile();
        InitializeSignalEventsFile();
        StartGlobalSignalMonitoring();
        try
        {
            _transport = new GodotRuntimeTransport(
                ProjectSettings.GlobalizePath("res://"),
                HandleToolOperation,
                LoadToolManifest);
            _transport.Start();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GodotRuntime] Cannot start runtime backend: {ex}");
            _transport?.Dispose();
            _transport = null;
        }
    }

    public override void _Process(double delta)
    {
        _transport?.Pump(delta);
        _capture?.Pump();
    }

    public override void _ExitTree()
    {
        _transport?.Dispose();
        _transport = null;
        if (_instance == this) _instance = null;
    }

    // ===================== Tool Operation Router =====================
    private ApiResponse HandleToolOperation(string toolName, Dictionary<string, object> args)
    {
        try
        {
            return toolName switch
            {
                // Scene tree
                "get_scene_tree" => GetSceneTree(GetBool(args, "includeProperties")),
                "get_scene_tree_simple" => GetSceneTreeSimple(
                    GetString(args, "rootPath", "/root"),
                    GetInt(args, "maxDepth", 3)),
                "get_node_info" => GetNodeInfo(GetString(args, "nodePath")),
                "create_node" => CreateNode(
                    GetString(args, "parentPath"),
                    GetString(args, "nodeType"),
                    GetString(args, "nodeName")),
                "delete_node" => DeleteNode(GetString(args, "nodePath")),
                "load_scene" => LoadScene(GetString(args, "scenePath")),
                "get_node_children" => GetNodeChildren(GetString(args, "nodePath")),
                "get_node_parent" => GetNodeParent(GetString(args, "nodePath")),
                "find_nodes_by_type" => FindNodesByType(
                    GetString(args, "nodeType"),
                    GetString(args, "rootPath", "/root")),
                "find_nodes_by_name" => FindNodesByName(
                    GetString(args, "namePattern"),
                    GetString(args, "rootPath", "/root"),
                    GetBool(args, "caseSensitive"),
                    GetBool(args, "exactMatch")),
                "find_nodes_by_group" => FindNodesByGroup(
                    GetString(args, "groupName"),
                    GetString(args, "rootPath", "/root")),
                "get_node_ancestors" => GetNodeAncestors(
                    GetString(args, "nodePath"),
                    GetInt(args, "levels", -1),
                    GetBool(args, "includeSiblings")),
                "get_scene_tree_stats" => GetSceneTreeStats(GetString(args, "rootPath", "/root")),
                "node_exists" => NodeExists(GetString(args, "nodePath")),
                "get_node_subtree" => GetNodeSubtree(
                    GetString(args, "nodePath"),
                    GetInt(args, "maxDepth", 2),
                    GetBool(args, "includeProperties")),
                "search_nodes" => SearchNodes(
                    GetStringOrNull(args, "namePattern"),
                    GetStringOrNull(args, "nodeType"),
                    GetStringOrNull(args, "groupName"),
                    GetString(args, "rootPath", "/root"),
                    GetInt(args, "maxResults", 50)),
                "get_node_context" => GetNodeContext(
                    GetString(args, "nodePath"),
                    GetBool(args, "includeParent", true),
                    GetBool(args, "includeSiblings", true),
                    GetBool(args, "includeChildren", true)),

                // Properties
                "get_property" => GetProperty(GetString(args, "nodePath"), GetString(args, "propertyName")),
                "set_property" => SetProperty(GetString(args, "nodePath"), GetString(args, "propertyName"), GetValue(args, "value")),
                "list_properties" => ListProperties(GetString(args, "nodePath")),

                // Methods
                "call_method" => CallMethod(GetString(args, "nodePath"), GetString(args, "methodName"), GetList(args, "args")),
                "list_methods" => ListMethods(GetString(args, "nodePath")),

                // Resources
                "list_resources" => ListResources(GetString(args, "path", "res://"), GetStringOrNull(args, "filter")),
                "load_resource" => LoadResource(GetString(args, "resourcePath")),
                "get_resource_info" => GetResourceInfo(GetString(args, "resourcePath")),

                // Script
                "execute_csharp" => ExecuteCSharp(GetString(args, "code")),
                "get_global_variables" => GetGlobalVariables(),

                // Signals
                "get_node_signals" => GetNodeSignals(GetString(args, "nodePath")),
                "get_signal_connections" => GetSignalConnections(GetString(args, "sourceNodePath"), GetString(args, "signalName")),
                "connect_signal" => ConnectSignal(GetString(args, "sourceNodePath"), GetString(args, "signalName"), GetString(args, "targetNodePath"), GetString(args, "targetMethod")),
                "disconnect_signal" => DisconnectSignal(GetString(args, "sourceNodePath"), GetString(args, "signalName"), GetString(args, "targetNodePath"), GetString(args, "targetMethod")),
                "emit_signal" => EmitSignal(GetString(args, "nodePath"), GetString(args, "signalName"), GetList(args, "args")),
                "start_signal_monitoring" => StartSignalMonitoring(GetStringOrNull(args, "signalName"), GetInt(args, "maxEvents", 5000)),
                "stop_signal_monitoring" => StopSignalMonitoring(),
                "get_signal_events" => GetSignalEvents(
                    GetInt(args, "count", 50),
                    GetStringOrNull(args, "nodePath"),
                    GetStringOrNull(args, "signalName"),
                    GetLongOrNull(args, "startTime"),
                    GetLongOrNull(args, "endTime")),
                "clear_signal_events" => ClearSignalEvents(),

                // Runtime scene checkpoints
                "godot_snapshot_capture" => Ok(_capture!.Start(
                    GetStringOrNull(args, "rootPath"), GetBool(args, "strict", true),
                    GetInt(args, "maxNodes", 2000))),
                "godot_snapshot_job_status" => Ok(_capture!.GetStatus(GetString(args, "jobId"))),

                // Debug
                "get_logs" => GetLogs(GetInt(args, "count", 50)),
                "get_logs_filtered" => GetLogsFiltered(
                    GetStringOrNull(args, "level"),
                    GetStringOrNull(args, "messagePattern"),
                    GetLongOrNull(args, "startTime"),
                    GetLongOrNull(args, "endTime"),
                    GetInt(args, "maxCount", 100)),
                "get_log_stats" => GetLogStats(),
                "export_logs" => ExportLogs(GetStringOrNull(args, "filePath")),
                "clear_logs" => ClearLogs(),
                "add_custom_log" => AddCustomLog(GetString(args, "message"), GetString(args, "level", "info")),
                "get_performance_stats" => GetPerformanceStats(),
                "take_screenshot" => TakeScreenshot(GetStringOrNull(args, "savePath")),
                "get_time" => GetTime(),
                "save_node_as_scene" => SaveNodeAsScene(
                    GetString(args, "nodePath"),
                    GetBool(args, "includeChildren", true),
                    GetStringOrNull(args, "savePath"),
                    GetBool(args, "returnContent", true)),

                _ => ErrorResponse($"Unknown tool: {toolName}")
            };
        }
        catch (Exception ex)
        {
            return ErrorResponse($"{toolName} error: {ex.Message}");
        }
    }

    // ===================== Argument Helpers =====================

    private static bool GetBool(Dictionary<string, object> args, string key, bool defaultValue = false)
    {
        if (args.TryGetValue(key, out var val) && val != null)
        {
            if (val is bool b) return b;
            if (val is JsonElement je && je.ValueKind == JsonValueKind.True) return true;
            if (val is JsonElement je2 && je2.ValueKind == JsonValueKind.False) return false;
        }
        return defaultValue;
    }

    private static int GetInt(Dictionary<string, object> args, string key, int defaultValue = 0)
    {
        if (args.TryGetValue(key, out var val) && val != null)
        {
            if (val is int i) return i;
            if (val is long l) return (int)l;
            if (val is double d) return (int)d;
            if (val is JsonElement je && je.ValueKind == JsonValueKind.Number)
                return je.GetInt32();
        }
        return defaultValue;
    }

    private static long? GetLongOrNull(Dictionary<string, object> args, string key)
    {
        if (args.TryGetValue(key, out var val) && val != null)
        {
            if (val is long l) return l;
            if (val is int i) return i;
            if (val is JsonElement je && je.ValueKind == JsonValueKind.Number)
                return je.GetInt64();
        }
        return null;
    }

    private static string GetString(Dictionary<string, object> args, string key, string defaultValue = "")
    {
        if (args.TryGetValue(key, out var val) && val != null)
        {
            if (val is string s) return s;
            if (val is JsonElement je && je.ValueKind == JsonValueKind.String)
                return je.GetString() ?? defaultValue;
            return val.ToString() ?? defaultValue;
        }
        return defaultValue;
    }

    private static string? GetStringOrNull(Dictionary<string, object> args, string key)
    {
        if (args.TryGetValue(key, out var val) && val != null)
        {
            if (val is string s) return s;
            if (val is JsonElement je && je.ValueKind == JsonValueKind.String)
                return je.GetString();
            return val.ToString();
        }
        return null;
    }

    private static object? GetValue(Dictionary<string, object> args, string key)
    {
        args.TryGetValue(key, out var val);
        return val;
    }

    private static List<object>? GetList(Dictionary<string, object> args, string key)
    {
        if (args.TryGetValue(key, out var val) && val != null)
        {
            if (val is List<object> list) return list;
            if (val is JsonElement je && je.ValueKind == JsonValueKind.Array)
            {
                return je.EnumerateArray().Select(e => (object)e.Clone()).ToList();
            }
        }
        return null;
    }

    // ===================== Scene Tree Tools =====================

    private ApiResponse GetSceneTree(bool includeProperties = false)
    {
        var root = GetTree().Root;
        var tree = BuildTree(root, includeProperties);
        return Ok(tree);
    }

    private ApiResponse GetSceneTreeSimple(string rootPath = "/root", int maxDepth = 3)
    {
        var root = GetNodeOrNull(rootPath);
        if (root == null) return Err($"Root not found: {rootPath}");
        var tree = BuildSimpleTree(root, maxDepth, 0);
        return Ok(tree);
    }

    private ApiResponse GetNodeInfo(string nodePath)
    {
        var node = GetNodeOrNull(nodePath);
        if (node == null) return Err($"Node not found: {nodePath}");

        return Ok(new Dictionary<string, object>
        {
            ["name"] = node.Name.ToString(),
            ["type"] = node.GetType().Name,
            ["path"] = node.GetPath().ToString(),
            ["children"] = node.GetChildren().Select(c => c.Name.ToString()).ToList(),
            ["properties"] = GetProps(node),
            ["methods"] = GetMethods(node),
            ["signals"] = GetSignals(node)
        });
    }

    private ApiResponse CreateNode(string parentPath, string nodeType, string nodeName)
    {
        var parent = GetNodeOrNull(parentPath);
        if (parent == null) return Err($"Parent not found: {parentPath}");

        // Use ClassDB to instantiate Godot built-in types (more reliable than Type.GetType)
        if (!ClassDB.ClassExists(nodeType))
            return Err($"Unknown node type: {nodeType}");

        var node = (Node?)ClassDB.Instantiate(nodeType);
        if (node == null) return Err($"Failed to create: {nodeType}");

        node.Name = nodeName;
        parent.AddChild(node);
        return Ok(new { path = node.GetPath().ToString() });
    }

    private ApiResponse DeleteNode(string nodePath)
    {
        var node = GetNodeOrNull(nodePath);
        if (node == null) return Err($"Node not found: {nodePath}");
        node.QueueFree();
        return Ok("deleted");
    }

    private ApiResponse LoadScene(string scenePath)
    {
        GetTree().CallDeferred("change_scene_to_file", scenePath);
        return Ok($"Loading: {scenePath}");
    }

    private ApiResponse GetNodeChildren(string nodePath)
    {
        var node = GetNodeOrNull(nodePath);
        if (node == null) return Err($"Node not found: {nodePath}");

        var children = node.GetChildren().Select(c => new Dictionary<string, object>
        {
            ["name"] = c.Name.ToString(),
            ["type"] = c.GetType().Name,
            ["path"] = c.GetPath().ToString()
        }).ToList();

        return Ok(children);
    }

    private ApiResponse GetNodeParent(string nodePath)
    {
        var node = GetNodeOrNull(nodePath);
        if (node == null) return Err($"Node not found: {nodePath}");

        var parent = node.GetParent();
        if (parent == null) return Ok(null);

        return Ok(new Dictionary<string, object>
        {
            ["name"] = parent.Name.ToString(),
            ["type"] = parent.GetType().Name,
            ["path"] = parent.GetPath().ToString()
        });
    }

    private ApiResponse FindNodesByType(string nodeType, string rootPath = "/root")
    {
        var root = GetNodeOrNull(rootPath);
        if (root == null) return Err($"Root not found: {rootPath}");

        var results = new List<object>();
        FindNodesByTypeRecursive(root, nodeType, results);

        return Ok(new { count = results.Count, nodes = results });
    }

    private void FindNodesByTypeRecursive(Node node, string targetType, List<object> results)
    {
        if (node.GetType().Name == targetType)
        {
            results.Add(new Dictionary<string, object>
            {
                ["name"] = node.Name.ToString(),
                ["type"] = node.GetType().Name,
                ["path"] = node.GetPath().ToString()
            });
        }
        foreach (Node child in node.GetChildren())
            FindNodesByTypeRecursive(child, targetType, results);
    }

    private ApiResponse FindNodesByName(string namePattern, string rootPath = "/root", bool caseSensitive = false, bool exactMatch = false)
    {
        var root = GetNodeOrNull(rootPath);
        if (root == null) return Err($"Root not found: {rootPath}");

        var results = new List<object>();
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        FindNodesByNameRecursive(root, namePattern, results, exactMatch, comparison, 50);

        return Ok(new { count = results.Count, nodes = results });
    }

    private void FindNodesByNameRecursive(Node node, string pattern, List<object> results, bool exactMatch, StringComparison comparison, int maxResults)
    {
        if (results.Count >= maxResults) return;
        var nodeName = node.Name.ToString();
        bool matches = exactMatch
            ? nodeName.Equals(pattern, comparison)
            : nodeName.Contains(pattern, comparison);
        if (matches)
        {
            results.Add(new Dictionary<string, object>
            {
                ["name"] = nodeName,
                ["type"] = node.GetType().Name,
                ["path"] = node.GetPath().ToString()
            });
        }
        foreach (Node child in node.GetChildren())
        {
            if (results.Count >= maxResults) break;
            FindNodesByNameRecursive(child, pattern, results, exactMatch, comparison, maxResults);
        }
    }

    private ApiResponse FindNodesByGroup(string groupName, string rootPath = "/root")
    {
        var nodes = GetTree().GetNodesInGroup(groupName);
        var results = nodes.Select(n => new Dictionary<string, object>
        {
            ["name"] = n.Name.ToString(),
            ["type"] = n.GetType().Name,
            ["path"] = n.GetPath().ToString(),
            ["groups"] = n.GetGroups().Select(g => g.ToString()).ToList()
        }).Take(50).ToList();

        return Ok(new { count = results.Count, nodes = results, groupName });
    }

    private ApiResponse GetNodeAncestors(string nodePath, int levels = -1, bool includeSiblings = false)
    {
        var node = GetNodeOrNull(nodePath);
        if (node == null) return Err($"Node not found: {nodePath}");

        var ancestors = new List<object>();
        var current = node.GetParent();
        var level = 0;

        while (current != null && (levels < 0 || level < levels))
        {
            var info = new Dictionary<string, object>
            {
                ["level"] = level + 1,
                ["name"] = current.Name.ToString(),
                ["type"] = current.GetType().Name,
                ["path"] = current.GetPath().ToString()
            };

            if (includeSiblings)
            {
                var parent = current.GetParent();
                if (parent != null)
                {
                    var siblings = parent.GetChildren()
                        .Where(c => c.GetPath() != current.GetPath())
                        .Select(c => new { name = c.Name.ToString(), type = c.GetType().Name, path = c.GetPath().ToString() })
                        .ToList();
                    info["siblings"] = siblings;
                    info["siblingCount"] = siblings.Count;
                }
            }

            ancestors.Add(info);
            current = current.GetParent();
            level++;
        }

        return Ok(new { nodePath, ancestorCount = ancestors.Count, ancestors });
    }

    private ApiResponse GetSceneTreeStats(string rootPath = "/root")
    {
        var root = GetNodeOrNull(rootPath);
        if (root == null) return Err($"Node not found: {rootPath}");

        var stats = new Dictionary<string, int>();
        var groups = new Dictionary<string, int>();
        var totalNodes = 0;
        var maxDepth = 0;
        CollectStatsRecursive(root, stats, groups, ref totalNodes, ref maxDepth, 0);

        return Ok(new
        {
            totalNodes,
            maxDepth,
            nodesByType = stats.OrderByDescending(x => x.Value).ToDictionary(x => x.Key, x => x.Value),
            nodesByGroup = groups.OrderByDescending(x => x.Value).ToDictionary(x => x.Key, x => x.Value),
            rootPath
        });
    }

    private void CollectStatsRecursive(Node node, Dictionary<string, int> stats, Dictionary<string, int> groups, ref int total, ref int maxDepth, int currentDepth)
    {
        total++;
        if (currentDepth > maxDepth) maxDepth = currentDepth;
        var type = node.GetType().Name;
        stats[type] = stats.GetValueOrDefault(type, 0) + 1;
        foreach (var group in node.GetGroups())
        {
            var g = group.ToString();
            groups[g] = groups.GetValueOrDefault(g, 0) + 1;
        }
        foreach (Node child in node.GetChildren())
            CollectStatsRecursive(child, stats, groups, ref total, ref maxDepth, currentDepth + 1);
    }

    private ApiResponse NodeExists(string nodePath)
    {
        var node = GetNodeOrNull(nodePath);
        return Ok(new { exists = node != null, path = nodePath });
    }

    private ApiResponse GetNodeSubtree(string nodePath, int maxDepth = 2, bool includeProperties = false)
    {
        var root = GetNodeOrNull(nodePath);
        if (root == null) return Err($"Node not found: {nodePath}");
        var tree = BuildTreeWithDepth(root, maxDepth, 0, includeProperties);
        return Ok(tree);
    }

    private Dictionary<string, object> BuildTreeWithDepth(Node n, int maxDepth, int currentDepth, bool includeProperties)
    {
        var d = new Dictionary<string, object>
        {
            ["name"] = n.Name.ToString(),
            ["type"] = n.GetType().Name,
            ["path"] = n.GetPath().ToString(),
            ["depth"] = currentDepth
        };
        if (includeProperties) d["properties"] = GetProps(n);
        if (maxDepth < 0 || currentDepth < maxDepth)
        {
            d["children"] = n.GetChildren()
                .Select(c => BuildTreeWithDepth(c, maxDepth, currentDepth + 1, includeProperties))
                .ToList();
        }
        else
        {
            d["children"] = new List<object>();
            d["childCount"] = n.GetChildCount();
        }
        return d;
    }

    private ApiResponse SearchNodes(string? namePattern, string? nodeType, string? groupName, string rootPath = "/root", int maxResults = 50)
    {
        var root = GetNodeOrNull(rootPath);
        if (root == null) return Err($"Root not found: {rootPath}");

        var results = new List<object>();
        SearchNodesRecursive(root, namePattern, nodeType, groupName, results, maxResults);

        return Ok(new
        {
            count = results.Count,
            nodes = results,
            truncated = results.Count >= maxResults
        });
    }

    private void SearchNodesRecursive(Node node, string? namePattern, string? nodeType, string? groupName, List<object> results, int maxResults)
    {
        if (results.Count >= maxResults) return;
        bool matches = true;
        if (!string.IsNullOrEmpty(namePattern))
        {
            var nodeName = node.Name.ToString();
            matches = nodeName.Contains(namePattern, StringComparison.OrdinalIgnoreCase);
        }
        if (matches && !string.IsNullOrEmpty(nodeType))
            matches = node.GetType().Name == nodeType;
        if (matches && !string.IsNullOrEmpty(groupName))
            matches = node.IsInGroup(groupName);
        if (matches)
        {
            results.Add(new Dictionary<string, object>
            {
                ["name"] = node.Name.ToString(),
                ["type"] = node.GetType().Name,
                ["path"] = node.GetPath().ToString(),
                ["groups"] = node.GetGroups().Select(g => g.ToString()).ToList()
            });
        }
        foreach (Node child in node.GetChildren())
        {
            if (results.Count >= maxResults) break;
            SearchNodesRecursive(child, namePattern, nodeType, groupName, results, maxResults);
        }
    }

    private ApiResponse GetNodeContext(string nodePath, bool includeParent = true, bool includeSiblings = true, bool includeChildren = true)
    {
        var node = GetNodeOrNull(nodePath);
        if (node == null) return Err($"Node not found: {nodePath}");

        var context = new Dictionary<string, object?>
        {
            ["node"] = new Dictionary<string, object>
            {
                ["name"] = node.Name.ToString(),
                ["type"] = node.GetType().Name,
                ["path"] = node.GetPath().ToString(),
                ["groups"] = node.GetGroups().Select(g => g.ToString()).ToList()
            }
        };

        if (includeParent)
        {
            var parent = node.GetParent();
            context["parent"] = parent != null ? new Dictionary<string, object>
            {
                ["name"] = parent.Name.ToString(),
                ["type"] = parent.GetType().Name,
                ["path"] = parent.GetPath().ToString()
            } : null;
        }

        if (includeSiblings && node.GetParent() != null)
        {
            var siblings = node.GetParent().GetChildren()
                .Where(c => c.GetPath() != node.GetPath())
                .Select(c => new { name = c.Name.ToString(), type = c.GetType().Name, path = c.GetPath().ToString() })
                .ToList();
            context["siblings"] = siblings;
            context["siblingCount"] = siblings.Count;
        }

        if (includeChildren)
        {
            var children = node.GetChildren().Select(c => new
            {
                name = c.Name.ToString(),
                type = c.GetType().Name,
                path = c.GetPath().ToString(),
                childCount = c.GetChildCount()
            }).ToList();
            context["children"] = children;
            context["childCount"] = children.Count;
        }

        return Ok(context);
    }

    // ===================== Property Tools =====================

    private ApiResponse GetProperty(string nodePath, string propertyName)
    {
        var node = GetNodeOrNull(nodePath);
        if (node == null) return Err($"Node not found: {nodePath}");
        var value = node.Get(propertyName);
        return Ok(new { value = Conv(value) });
    }

    private ApiResponse SetProperty(string nodePath, string propertyName, object? value)
    {
        var node = GetNodeOrNull(nodePath);
        if (node == null) return Err($"Node not found: {nodePath}");
        if (!node.GetPropertyList().Any(p => p["name"].AsString() == propertyName))
            return Err($"Unknown node property: {propertyName}");
        var existing = node.Get(propertyName).VariantType;
        // Godot does not coerce JSON maps into engine vector variants automatically.
        Variant converted;
        if (value is JsonElement je && je.ValueKind == JsonValueKind.Object &&
            existing == Variant.Type.Vector2)
            converted = new Vector2(
                (float)je.GetProperty("x").GetDouble(),
                (float)je.GetProperty("y").GetDouble());
        else if (value is JsonElement j3 && j3.ValueKind == JsonValueKind.Object &&
            existing == Variant.Type.Vector3)
            converted = new Vector3(
                (float)j3.GetProperty("x").GetDouble(),
                (float)j3.GetProperty("y").GetDouble(),
                (float)j3.GetProperty("z").GetDouble());
        else
            converted = ToVar(value);
        node.Set(propertyName, converted);
        return Ok(new { propertyName, value = Conv(node.Get(propertyName)) });
    }

    private ApiResponse ListProperties(string nodePath)
    {
        var node = GetNodeOrNull(nodePath);
        if (node == null) return Err($"Node not found: {nodePath}");
        return Ok(GetProps(node));
    }

    // ===================== Method Tools =====================

    private ApiResponse CallMethod(string nodePath, string methodName, List<object>? args = null)
    {
        var node = GetNodeOrNull(nodePath);
        if (node == null) return Err($"Node not found: {nodePath}");

        var godotArgs = new Godot.Collections.Array();
        args?.ForEach(a => godotArgs.Add(ToVar(a)));
        var result = node.Call(methodName, godotArgs);
        return Ok(Conv(result));
    }

    private ApiResponse ListMethods(string nodePath)
    {
        var node = GetNodeOrNull(nodePath);
        if (node == null) return Err($"Node not found: {nodePath}");
        return Ok(GetMethods(node));
    }

    // ===================== Resource Tools =====================

    private ApiResponse ListResources(string path = "res://", string? filter = null)
    {
        var list = new List<string>();
        var dir = DirAccess.Open(path);
        if (dir != null)
        {
            dir.ListDirBegin();
            for (var f = dir.GetNext(); f != ""; f = dir.GetNext())
            {
                if (!dir.CurrentIsDir())
                {
                    if (string.IsNullOrEmpty(filter) || f.Contains(filter, StringComparison.OrdinalIgnoreCase))
                        list.Add(f);
                }
            }
            dir.ListDirEnd();
        }
        return Ok(list);
    }

    private ApiResponse LoadResource(string resourcePath)
    {
        var r = GD.Load(resourcePath);
        return r == null ? Err("Load failed") : Ok(new { type = r.GetType().Name });
    }

    private ApiResponse GetResourceInfo(string resourcePath)
    {
        var r = GD.Load(resourcePath);
        return r == null ? Err("Resource not found") : Ok(new { type = r.GetType().Name, path = r.ResourcePath });
    }

    // ===================== Script Tools =====================

    private ApiResponse ExecuteCSharp(string code) => Err("execute_csharp is unavailable: Roslyn runtime is not bundled. No code was executed.");

    private ApiResponse GetGlobalVariables()
    {
        return Ok(GetTree().Root.GetChildren().ToDictionary(
            c => c.Name.ToString(),
            c => (object)new { type = c.GetType().Name, path = c.GetPath().ToString() }
        ));
    }

    // ===================== Signal Tools =====================

    private ApiResponse GetNodeSignals(string nodePath)
    {
        var node = GetNodeOrNull(nodePath);
        if (node == null) return Err($"Node not found: {nodePath}");

        var signals = node.GetSignalList()
            .Select(s => (Godot.Collections.Dictionary)s)
            .Select(s => new
            {
                name = s["name"].AsString(),
                args = ((Godot.Collections.Array)s["args"])
                    .Select(a => ((Godot.Collections.Dictionary)a)["name"].AsString())
                    .ToList()
            })
            .ToList();

        return Ok(new { nodePath, signals });
    }

    private ApiResponse GetSignalConnections(string sourceNodePath, string signalName)
    {
        var node = GetNodeOrNull(sourceNodePath);
        if (node == null) return Err($"Node not found: {sourceNodePath}");

        var connections = node.GetSignalConnectionList(signalName)
            .Select(c => (Godot.Collections.Dictionary)c)
            .Select(c => new
            {
                signal = c["signal"].AsString(),
                callable = c["callable"].ToString(),
                target = c.ContainsKey("target") ? c["target"].ToString() : null
            })
            .ToList();

        return Ok(new { signal = signalName, connections });
    }

    private ApiResponse ConnectSignal(string sourceNodePath, string signalName, string targetNodePath, string targetMethod)
    {
        var source = GetNodeOrNull(sourceNodePath);
        if (source == null) return Err($"Source not found: {sourceNodePath}");

        if (string.IsNullOrEmpty(targetNodePath) || string.IsNullOrEmpty(targetMethod))
            return Err("Need target node path and method");

        var target = GetNodeOrNull(targetNodePath);
        if (target == null) return Err($"Target not found: {targetNodePath}");

        try
        {
            var callable = new Callable(target, targetMethod);
            source.Connect(signalName, callable);
            LogInfo($"Signal connected: {sourceNodePath}.{signalName} → {targetNodePath}.{targetMethod}");
            return Ok(new { connected = true, source = sourceNodePath, signal = signalName, target = targetNodePath, method = targetMethod });
        }
        catch (Exception ex)
        {
            return Err($"Connect failed: {ex.Message}");
        }
    }

    private ApiResponse DisconnectSignal(string sourceNodePath, string signalName, string targetNodePath, string targetMethod)
    {
        var source = GetNodeOrNull(sourceNodePath);
        if (source == null) return Err($"Source not found: {sourceNodePath}");

        if (string.IsNullOrEmpty(targetNodePath) || string.IsNullOrEmpty(targetMethod))
            return Err("Need target node path and method");

        var target = GetNodeOrNull(targetNodePath);
        if (target == null) return Err($"Target not found: {targetNodePath}");

        try
        {
            var callable = new Callable(target, targetMethod);
            source.Disconnect(signalName, callable);
            LogInfo($"Signal disconnected: {sourceNodePath}.{signalName}");
            return Ok(new { disconnected = true, source = sourceNodePath, signal = signalName });
        }
        catch (Exception ex)
        {
            return Err($"Disconnect failed: {ex.Message}");
        }
    }

    private ApiResponse EmitSignal(string nodePath, string signalName, List<object>? args = null)
    {
        var node = GetNodeOrNull(nodePath);
        if (node == null) return Err($"Node not found: {nodePath}");

        try
        {
            if (args == null || args.Count == 0)
                node.EmitSignal(signalName);
            else
                node.EmitSignal(signalName, args.Select(a => ToVar(a)).ToArray());

            LogInfo($"Signal emitted: {nodePath}.{signalName}");
            return Ok(new { emitted = true, nodePath, signal = signalName, argCount = args?.Count ?? 0 });
        }
        catch (Exception ex)
        {
            return Err($"Emit failed: {ex.Message}");
        }
    }

    private ApiResponse StartSignalMonitoring(string? signalName, int maxEvents = 5000)
    {
        if (!string.IsNullOrEmpty(signalName))
            _monitoredSignals.Add(signalName);
        else
            _monitoredSignals.Clear();

        LogInfo($"Signal monitoring filter updated - signal: {(signalName ?? "all")}");

        return Ok(new
        {
            monitoring = _isGlobalSignalMonitoring,
            monitoredSignals = _monitoredSignals.Count > 0
                ? _monitoredSignals.ToList()
                : new List<string> { "all signals" },
            currentEventsCount = _signalEventsBuffer.Count
        });
    }

    private ApiResponse StopSignalMonitoring()
    {
        var eventCount = _signalEventsBuffer.Count;
        LogInfo($"Signal monitoring stats - {eventCount} events recorded");
        return Ok(new { monitoring = _isGlobalSignalMonitoring, note = "Global monitoring keeps running", totalEvents = eventCount });
    }

    private ApiResponse GetSignalEvents(int count = 50, string? nodePathFilter = null, string? signalNameFilter = null, long? startTime = null, long? endTime = null)
    {
        var allEvents = ReadAllSignalEvents();
        var query = allEvents.AsEnumerable();

        if (!string.IsNullOrEmpty(nodePathFilter))
            query = query.Where(e => e.NodePath.Contains(nodePathFilter, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(signalNameFilter))
            query = query.Where(e => e.SignalName.Equals(signalNameFilter, StringComparison.OrdinalIgnoreCase));

        if (startTime.HasValue)
        {
            var start = DateTimeOffset.FromUnixTimeSeconds(startTime.Value).DateTime;
            query = query.Where(e => e.Timestamp >= start);
        }

        if (endTime.HasValue)
        {
            var end = DateTimeOffset.FromUnixTimeSeconds(endTime.Value).DateTime;
            query = query.Where(e => e.Timestamp <= end);
        }

        var events = query.TakeLast(count).ToList();

        return Ok(new
        {
            totalEvents = allEvents.Count,
            matchedEvents = query.Count(),
            returnedEvents = events.Count,
            events = events.Select(e => new
            {
                timestamp = e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                unixTimestamp = new DateTimeOffset(e.Timestamp).ToUnixTimeSeconds(),
                nodePath = e.NodePath,
                nodeType = e.NodeType,
                signalName = e.SignalName,
                args = e.Args
            }).ToList()
        });
    }

    private List<SignalEvent> ReadAllSignalEvents()
    {
        var events = new List<SignalEvent>(_signalEventsBuffer);

        try
        {
            using var file = FileAccess.Open(SignalEventsFilePath, FileAccess.ModeFlags.Read);
            if (file != null)
            {
                while (!file.EofReached())
                {
                    var line = file.GetLine();
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("===")) continue;

                    var match = Regex.Match(line,
                        @"\[([\d\-: .]+)\] (.+?) \((\w+)\) :: (\w+)(?: \[(.+?)\])?");

                    if (match.Success)
                    {
                        var argsStr = match.Groups[5].Value;
                        events.Add(new SignalEvent
                        {
                            Timestamp = DateTime.Parse(match.Groups[1].Value),
                            NodePath = match.Groups[2].Value,
                            NodeType = match.Groups[3].Value,
                            SignalName = match.Groups[4].Value,
                            Args = string.IsNullOrEmpty(argsStr) ? new List<string>() : argsStr.Split(", ").ToList()
                        });
                    }
                }
            }
        }
        catch { }

        return events.OrderBy(e => e.Timestamp).ToList();
    }

    private ApiResponse ClearSignalEvents()
    {
        var bufferCount = _signalEventsBuffer.Count;
        _signalEventsBuffer.Clear();
        InitializeSignalEventsFile();
        LogInfo($"Signal events cleared - buffer: {bufferCount}");
        return Ok(new { cleared = true, bufferCleared = bufferCount });
    }

    // ===================== Debug Tools =====================

    private ApiResponse GetLogs(int count = 50) => Ok(_logs.TakeLast(count));

    private ApiResponse GetLogsFiltered(string? level = null, string? messagePattern = null, long? startTime = null, long? endTime = null, int maxCount = 100)
    {
        var allLogs = _logBuffer.Concat(ReadLogsFromFile()).ToList();
        var query = allLogs.AsEnumerable();

        if (!string.IsNullOrEmpty(level))
            query = query.Where(log => log.Level.Equals(level, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(messagePattern))
            query = query.Where(log => log.Message.Contains(messagePattern, StringComparison.OrdinalIgnoreCase));

        if (startTime.HasValue)
        {
            var s = DateTimeOffset.FromUnixTimeSeconds(startTime.Value).DateTime;
            query = query.Where(log => log.Timestamp >= s);
        }

        if (endTime.HasValue)
        {
            var e = DateTimeOffset.FromUnixTimeSeconds(endTime.Value).DateTime;
            query = query.Where(log => log.Timestamp <= e);
        }

        var logs = query.TakeLast(maxCount).ToList();
        return Ok(new { totalMatched = logs.Count, logs });
    }

    private List<LogEntry> ReadLogsFromFile()
    {
        var logs = new List<LogEntry>();
        try
        {
            using var file = FileAccess.Open(LogFilePath, FileAccess.ModeFlags.Read);
            if (file != null)
            {
                while (!file.EofReached())
                {
                    var line = file.GetLine();
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("===")) continue;
                    var match = Regex.Match(line, @"\[([\d\-: ]+)\] \[(\w+)\] (.+)");
                    if (match.Success)
                    {
                        logs.Add(new LogEntry
                        {
                            Timestamp = DateTime.Parse(match.Groups[1].Value),
                            Level = match.Groups[2].Value.ToLower(),
                            Message = match.Groups[3].Value
                        });
                    }
                }
            }
        }
        catch { }
        return logs;
    }

    private ApiResponse GetLogStats()
    {
        var allLogs = _logBuffer.Concat(ReadLogsFromFile()).ToList();
        return Ok(new
        {
            totalLogs = allLogs.Count,
            inBuffer = _logBuffer.Count,
            inFile = allLogs.Count - _logBuffer.Count,
            byLevel = allLogs.GroupBy(l => l.Level).ToDictionary(g => g.Key, g => g.Count()),
            oldestLog = allLogs.Count > 0 ? allLogs.First().Timestamp : (DateTime?)null,
            newestLog = allLogs.Count > 0 ? allLogs.Last().Timestamp : (DateTime?)null
        });
    }

    private ApiResponse ExportLogs(string? filePath = null)
    {
        var path = filePath ?? "user://logs_export.txt";
        try
        {
            var allLogs = _logBuffer.Concat(ReadLogsFromFile()).ToList();
            using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
            if (file != null)
            {
                file.StoreString($"=== MCP Log Export - {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n");
                file.StoreString($"Total: {allLogs.Count} entries\n\n");
                foreach (var log in allLogs)
                    file.StoreString($"[{log.Timestamp:yyyy-MM-dd HH:mm:ss}] [{log.Level.ToUpper()}] {log.Message}\n");
                LogInfo($"Logs exported to: {path}");
                return Ok(new { exported = true, filePath = path, logCount = allLogs.Count });
            }
            return Err("Cannot open export file");
        }
        catch (Exception ex)
        {
            return Err($"Export failed: {ex.Message}");
        }
    }

    private ApiResponse ClearLogs()
    {
        var bufferCount = _logBuffer.Count;
        _logBuffer.Clear();
        _logs.Clear();
        InitializeLogFile();
        LogInfo($"Logs cleared - buffer: {bufferCount}");
        return Ok(new { cleared = true, bufferCleared = bufferCount });
    }

    private ApiResponse AddCustomLog(string message, string level = "info")
    {
        AddLog(level, message);
        return Ok(new { logged = true, level, message });
    }

    private ApiResponse GetPerformanceStats()
    {
        return Ok(new
        {
            fps = Engine.GetFramesPerSecond(),
            processTime = Performance.GetMonitor(Performance.Monitor.TimeProcess),
            physicsTime = Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess),
            memoryUsage = Performance.GetMonitor(Performance.Monitor.MemoryStatic),
            nodeCount = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount),
            drawCalls = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)
        });
    }

    private ApiResponse TakeScreenshot(string? savePath = null)
    {
        var path = savePath ?? "user://screenshot.png";
        var img = GetViewport().GetTexture().GetImage();
        img.SavePng(path);
        return Ok(new { path });
    }

    private ApiResponse GetTime()
    {
        return Ok(new
        {
            unix = Time.GetUnixTimeFromSystem(),
            datetime = DateTime.Now.ToString("s"),
            ticks = Time.GetTicksMsec()
        });
    }

    // ===================== Node Save Tool =====================

    private ApiResponse SaveNodeAsScene(string nodePath, bool includeChildren, string? savePath, bool returnContent)
    {
        var node = GetNodeOrNull(nodePath);
        if (node == null)
            return Err($"节点不存在: {nodePath}");

        try
        {
            // For Pack() to capture the full subtree, all nodes must have proper Owner set.
            var ownedNodes = new List<Node>();
            Node[]? detachedChildren = null;

            if (includeChildren)
            {
                // Ensure ownership chain so Pack() captures all descendants
                EnsureOwnership(node, node, ownedNodes);
            }
            else
            {
                // Detach children temporarily so Pack() only captures the node itself
                detachedChildren = node.GetChildren().Cast<Node>().ToArray();
                foreach (Node child in detachedChildren)
                    node.RemoveChild(child);
            }

            // Pack the node
            var packedScene = new PackedScene();
            var packResult = packedScene.Pack(node);

            // Restore children if we detached them
            if (!includeChildren && detachedChildren != null)
            {
                foreach (Node child in detachedChildren)
                    node.AddChild(child);
                node.Owner = null;
            }

            // Restore owners (if we changed them)
            if (includeChildren)
            {
                foreach (var n in ownedNodes)
                    n.Owner = null;
            }

            if (packResult != Godot.Error.Ok)
                return Err($"打包节点失败，错误码: {packResult}");

            // Determine save path
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var nodeName = node.Name.ToString().Replace(":", "_").Replace("/", "_");
            var finalPath = savePath ?? $"user://mcp_saved_nodes/{nodeName}_{timestamp}.tscn";

            // Ensure directory exists
            var dirGlobal = ProjectSettings.GlobalizePath("user://mcp_saved_nodes/");
            if (!DirAccess.DirExistsAbsolute(dirGlobal))
                DirAccess.MakeDirRecursiveAbsolute(dirGlobal);

            // Save as .tscn (text format) — no bundle to keep it readable
            var saveResult = ResourceSaver.Save(packedScene, finalPath);
            if (saveResult != Godot.Error.Ok)
                return Err($"保存场景文件失败，错误码: {saveResult}");

            // Read file info
            var absPath = ProjectSettings.GlobalizePath(finalPath);
            var fileInfo = new System.IO.FileInfo(absPath);

            // Build result
            var result = new Dictionary<string, object>
            {
                ["nodePath"] = nodePath,
                ["nodeName"] = node.Name.ToString(),
                ["nodeType"] = node.GetType().Name,
                ["includedChildren"] = includeChildren,
                ["savePath"] = finalPath,
                ["absolutePath"] = absPath,
                ["fileSize"] = fileInfo.Length,
                ["childCount"] = node.GetChildCount()
            };

            // Optionally return the text content for analysis
            if (returnContent)
            {
                using var file = FileAccess.Open(finalPath, FileAccess.ModeFlags.Read);
                if (file != null)
                {
                    var content = file.GetAsText();
                    result["content"] = content;
                    result["contentLength"] = content.Length;

                    var lines = content.Split('\n');
                    var nodeCount = 0;
                    var resourceCount = 0;
                    var extResourceCount = 0;
                    var subResourceCount = 0;
                    foreach (var line in lines)
                    {
                        var t = line.TrimStart();
                        if (t.StartsWith("[node")) nodeCount++;
                        else if (t.StartsWith("[resource")) resourceCount++;
                        else if (t.StartsWith("[ext_resource")) extResourceCount++;
                        else if (t.StartsWith("[sub_resource")) subResourceCount++;
                    }
                    result["summary"] = new Dictionary<string, object>
                    {
                        ["totalLines"] = lines.Length,
                        ["nodeDefinitions"] = nodeCount,
                        ["resourceDefinitions"] = resourceCount,
                        ["extResourceDefinitions"] = extResourceCount,
                        ["subResourceDefinitions"] = subResourceCount
                    };
                }
            }

            LogInfo($"节点已保存为场景文件: {nodePath} → {finalPath}");
            return Ok(result);
        }
        catch (Exception ex)
        {
            return Err($"保存节点为场景文件失败: {ex.Message}");
        }
    }

    /// <summary>
    /// Recursively ensure all nodes in the subtree have proper owner set for Pack().
    /// </summary>
    private void EnsureOwnership(Node root, Node owner, List<Node> touched)
    {
        // Don't set owner to itself
        if (root != owner && root.Owner == null)
        {
            root.Owner = owner;
            touched.Add(root);
        }
        foreach (Node child in root.GetChildren())
            EnsureOwnership(child, owner, touched);
    }

    // ===================== Signal Monitoring System =====================

    private void StartGlobalSignalMonitoring()
    {
        _isGlobalSignalMonitoring = true;
        GetTree().NodeAdded += OnNodeAddedToTree;
        MonitorExistingNodes(GetTree().Root);
    }

    private void OnNodeAddedToTree(Node node)
    {
        if (!_isGlobalSignalMonitoring) return;

        var signals = node.GetSignalList();
        foreach (var signalDict in signals)
        {
            var dict = (Godot.Collections.Dictionary)signalDict;
            var signalName = dict["name"].AsString();

            if (_monitoredSignals.Count == 0 || _monitoredSignals.Contains(signalName))
            {
                var signalArgs = (Godot.Collections.Array)dict["args"];
                var argCount = signalArgs.Count;

                // Only auto-connect 0-arg signals to avoid flooding with param errors
                if (argCount == 0)
                {
                    var capturedNode = node;
                    var capturedName = signalName;
                    try
                    {
                        capturedNode.Connect(capturedName, Callable.From(() =>
                        {
                            // Ensure node is still in the scene tree before accessing path
                            if (_isGlobalSignalMonitoring && capturedNode != null && capturedNode.IsInsideTree())
                                RecordSignalEvent(capturedNode, capturedName, null);
                        }));
                    }
                    catch { }
                }
            }
        }
    }

    private void MonitorExistingNodes(Node root)
    {
        OnNodeAddedToTree(root);
        foreach (Node child in root.GetChildren())
            MonitorExistingNodes(child);
    }

    private void RecordSignalEvent(Node node, string signalName, object[]? args)
    {
        var evt = new SignalEvent
        {
            Timestamp = DateTime.Now,
            NodePath = node.GetPath().ToString(),
            NodeType = node.GetType().Name,
            SignalName = signalName,
            Args = args?.Select(a => a?.ToString() ?? "null").ToList() ?? new List<string>()
        };

        _signalEventsBuffer.Add(evt);

        if (_signalEventsBuffer.Count > MaxSignalEventsBufferSize)
        {
            var oldEvent = _signalEventsBuffer[0];
            _signalEventsBuffer.RemoveAt(0);
            WriteSignalEventToFile(oldEvent);
        }
    }

    private void WriteSignalEventToFile(SignalEvent evt)
    {
        try
        {
            using var file = FileAccess.Open(SignalEventsFilePath, FileAccess.ModeFlags.ReadWrite);
            if (file != null)
            {
                file.SeekEnd();
                var argsStr = evt.Args.Count > 0 ? $" [{string.Join(", ", evt.Args)}]" : "";
                file.StoreString($"[{evt.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] {evt.NodePath} ({evt.NodeType}) :: {evt.SignalName}{argsStr}\n");
            }
        }
        catch { }
    }

    // ===================== Logging System =====================

    private void InitializeLogFile()
    {
        try
        {
            using var file = FileAccess.Open(LogFilePath, FileAccess.ModeFlags.Write);
            if (file != null)
                file.StoreString($"=== MCP Logs - Start: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n");
        }
        catch { }
    }

    private void InitializeSignalEventsFile()
    {
        try
        {
            using var file = FileAccess.Open(SignalEventsFilePath, FileAccess.ModeFlags.Write);
            if (file != null)
                file.StoreString($"=== MCP Signal Events - Start: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n");
        }
        catch { }
    }

    private void LogInfo(string message)
    {
        AddLog("info", message);
        GD.Print($"[GodotRuntime] {message}");
    }

    private void AddLog(string level, string message)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Message = message
        };

        _logBuffer.AddLast(entry);
        if (_logBuffer.Count > MaxLogBufferSize)
        {
            _logBuffer.RemoveFirst();
        }
        _logs.Add(entry);
    }

    // ===================== Helper Methods =====================

    private Node? GetNodeOrNull(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        return GetTree().Root.GetNodeOrNull(path);
    }

    private Dictionary<string, object> BuildTree(Node n, bool props)
    {
        var d = new Dictionary<string, object>
        {
            ["name"] = n.Name.ToString(),
            ["type"] = n.GetType().Name,
            ["path"] = n.GetPath().ToString(),
            ["children"] = n.GetChildren().Select(c => BuildTree(c, props)).ToList()
        };
        if (props) d["properties"] = GetProps(n);
        return d;
    }

    private Dictionary<string, object> BuildSimpleTree(Node node, int maxDepth, int currentDepth)
    {
        var result = new Dictionary<string, object>
        {
            ["name"] = node.Name.ToString(),
            ["type"] = node.GetType().Name
        };
        if (currentDepth < maxDepth)
        {
            result["children"] = node.GetChildren()
                .Select(c => BuildSimpleTree(c, maxDepth, currentDepth + 1))
                .ToList();
        }
        return result;
    }

    private Dictionary<string, object?> GetProps(Node n)
    {
        return n.GetPropertyList()
            .Select(p => (Godot.Collections.Dictionary)p)
            .Where(p => !p["name"].AsString().StartsWith("_"))
            .GroupBy(p => p["name"].AsString())
            .Select(g => g.First())
            .ToDictionary(p => p["name"].AsString(), p =>
            {
                try { return Conv(n.Get(p["name"].AsString())); }
                catch { return null; }
            });
    }

    private List<string> GetMethods(Node n)
    {
        return n.GetMethodList()
            .Select(m => ((Godot.Collections.Dictionary)m)["name"].AsString())
            .Where(m => !m.StartsWith("_"))
            .ToList();
    }

    private List<string> GetSignals(Node n)
    {
        return n.GetSignalList()
            .Select(s => ((Godot.Collections.Dictionary)s)["name"].AsString())
            .ToList();
    }

    private object? Conv(Variant v)
    {
        return v.VariantType switch
        {
            Variant.Type.Nil => null,
            Variant.Type.Bool => v.AsBool(),
            Variant.Type.Int => v.AsInt64(),
            Variant.Type.Float => v.AsDouble(),
            Variant.Type.String => v.AsString(),
            Variant.Type.Vector2 => new { x = v.AsVector2().X, y = v.AsVector2().Y },
            Variant.Type.Vector3 => new { x = v.AsVector3().X, y = v.AsVector3().Y, z = v.AsVector3().Z },
            _ => v.ToString()
        };
    }

    private Variant ToVar(object? value)
    {
        if (value is JsonElement json)
        {
            switch (json.ValueKind)
            {
                case JsonValueKind.Null:
                case JsonValueKind.Undefined: return default;
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                case JsonValueKind.String: return json.GetString() ?? "";
                case JsonValueKind.Number:
                    return json.TryGetInt64(out var number) ? (Variant)number : (Variant)json.GetDouble();
                case JsonValueKind.Array:
                    var array = new Godot.Collections.Array();
                    foreach (var element in json.EnumerateArray()) array.Add(ToVar(element));
                    return array;
                case JsonValueKind.Object:
                    // Explicit types are useful for call_method and emit_signal.
                    if (json.TryGetProperty("$type", out var kind))
                    {
                        if (kind.GetString() == "Vector2")
                            return new Vector2((float)json.GetProperty("x").GetDouble(),
                                               (float)json.GetProperty("y").GetDouble());
                        if (kind.GetString() == "Vector3")
                            return new Vector3((float)json.GetProperty("x").GetDouble(),
                                               (float)json.GetProperty("y").GetDouble(),
                                               (float)json.GetProperty("z").GetDouble());
                    }
                    var dictionary = new Godot.Collections.Dictionary();
                    foreach (var prop in json.EnumerateObject())
                        dictionary[prop.Name] = ToVar(prop.Value);
                    return dictionary;
            }
        }
        return value switch
        {
            null => default, bool b => b, int i => i, long l => l,
            float f => f, double d => d, string s => s,
            _ => value.ToString() ?? ""
        };
    }

    private ApiResponse Ok(object? d) => new() { Success = true, Data = d };
    private ApiResponse Err(string msg) => new() { Success = false, Error = msg };
    private ApiResponse ErrorResponse(string msg) => new() { Success = false, Error = msg };

}

// ===================== Data Models =====================

public class ApiResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("data")]
    public object? Data { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public class LogEntry
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("level")]
    public string Level { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

public class SignalEvent
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("nodePath")]
    public string NodePath { get; set; } = "";

    [JsonPropertyName("nodeType")]
    public string NodeType { get; set; } = "";

    [JsonPropertyName("signalName")]
    public string SignalName { get; set; } = "";

    [JsonPropertyName("args")]
    public List<string> Args { get; set; } = new();
}
