using Godot;
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
// GodotMcpServer — Unified MCP HTTP+SSE Server for AI Agent Integration
//
// Merges the functionality of the .NET McpServer console app and the
// GodotPlugin into a single self-contained Godot addon.
//
// Transport: MCP over HTTP+SSE (Model Context Protocol spec)
//   GET  /sse                — SSE endpoint for MCP session establishment
//   POST /messages?session_id=X — MCP JSON-RPC message endpoint
//   POST /api/<endpoint>     — Legacy HTTP API (backward compatible)
//
// No external .NET process needed. Runs directly inside Godot.
// =========================================================================

public partial class GodotMcpServer : Node
{
    // ===================== Singleton =====================

    private static GodotMcpServer _instance;
    public static GodotMcpServer Instance => _instance;

    // ===================== Constants =====================

    private const string McpServerName = "godot-mcp";
    private const string McpServerVersion = "2.0.0";
    private const string ProtocolVersion = "2024-11-05";
    private const int HttpPort = 7777;
    private const string BaseUrl = "http://127.0.0.1:7777/";

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

    private HttpListener _httpListener;
    private bool _isRunning;

    // SSE connections: sessionId -> SseConnection
    private readonly Dictionary<string, SseConnection> _sseConnections = new();
    private readonly object _sseLock = new();

    // Thread-safe request queue (for main-thread Godot operations)
    private readonly Queue<PendingRequest> _requestQueue = new();
    private readonly object _queueLock = new();

    // Logging
    private readonly LinkedList<LogEntry> _logBuffer = new();
    private readonly List<LogEntry> _logs = new(); // backward compat

    // Signal monitoring
    private readonly List<SignalEvent> _signalEventsBuffer = new();
    private bool _isGlobalSignalMonitoring;
    private readonly HashSet<string> _monitoredSignals = new();

    // ===================== Tool Definitions =====================

    private static readonly List<McpToolDef> _tools = new()
    {
        // --- Scene Tree ---
        new McpToolDef
        {
            Name = "get_scene_tree",
            Description = "获取当前场景树完整结构",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["includeProperties"] = new Dictionary<string, object>
                    {
                        ["type"] = "boolean",
                        ["description"] = "是否包含节点属性"
                    }
                }
            }
        },
        new McpToolDef
        {
            Name = "get_scene_tree_simple",
            Description = "获取场景树简化结构（仅名称和类型，用于快速浏览）",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["rootPath"] = new Dictionary<string, object>
                    {
                        ["type"] = "string",
                        ["description"] = "根节点路径，默认 /root"
                    },
                    ["maxDepth"] = new Dictionary<string, object>
                    {
                        ["type"] = "integer",
                        ["description"] = "最大深度，默认 3"
                    }
                }
            }
        },
        new McpToolDef
        {
            Name = "get_node_info",
            Description = "获取指定节点的详细信息（属性、方法、信号、子节点）",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["nodePath"] = new Dictionary<string, object>
                    {
                        ["type"] = "string",
                        ["description"] = "节点路径，例如 /root/Main/Player"
                    }
                },
                ["required"] = new List<string> { "nodePath" }
            }
        },
        new McpToolDef
        {
            Name = "create_node",
            Description = "创建新节点",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["parentPath"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "父节点路径" },
                    ["nodeType"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "节点类型，如 Node2D, Sprite2D" },
                    ["nodeName"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "新节点名称" }
                },
                ["required"] = new List<string> { "parentPath", "nodeType", "nodeName" }
            }
        },
        new McpToolDef
        {
            Name = "delete_node",
            Description = "删除节点",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["nodePath"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "要删除的节点路径" }
                },
                ["required"] = new List<string> { "nodePath" }
            }
        },
        new McpToolDef
        {
            Name = "load_scene",
            Description = "加载场景文件",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["scenePath"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "场景文件路径，如 res://scenes/level1.tscn" }
                },
                ["required"] = new List<string> { "scenePath" }
            }
        },
        new McpToolDef
        {
            Name = "get_node_children",
            Description = "获取节点的直接子节点列表（不递归，轻量级）",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["nodePath"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "节点路径" }
                },
                ["required"] = new List<string> { "nodePath" }
            }
        },
        new McpToolDef
        {
            Name = "get_node_parent",
            Description = "获取节点的父节点路径",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["nodePath"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "节点路径" }
                },
                ["required"] = new List<string> { "nodePath" }
            }
        },
        new McpToolDef
        {
            Name = "find_nodes_by_type",
            Description = "查找指定类型的所有节点",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["nodeType"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "节点类型，如 Sprite2D, Camera2D" },
                    ["rootPath"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "搜索根路径，默认 /root" }
                },
                ["required"] = new List<string> { "nodeType" }
            }
        },
        new McpToolDef
        {
            Name = "find_nodes_by_name",
            Description = "按名称搜索节点（支持模糊匹配）",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["namePattern"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "节点名称或名称片段" },
                    ["rootPath"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "搜索根路径，默认 /root" },
                    ["caseSensitive"] = new Dictionary<string, object> { ["type"] = "boolean", ["description"] = "是否区分大小写" },
                    ["exactMatch"] = new Dictionary<string, object> { ["type"] = "boolean", ["description"] = "是否精确匹配" }
                },
                ["required"] = new List<string> { "namePattern" }
            }
        },
        new McpToolDef
        {
            Name = "find_nodes_by_group",
            Description = "按组名查找节点",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["groupName"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "组名" },
                    ["rootPath"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "搜索根路径，默认 /root" }
                },
                ["required"] = new List<string> { "groupName" }
            }
        },
        new McpToolDef
        {
            Name = "get_node_ancestors",
            Description = "获取节点的祖先路径（向上追溯多层）",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["nodePath"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "节点路径" },
                    ["levels"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "向上追溯层数，-1=追溯到根" },
                    ["includeSiblings"] = new Dictionary<string, object> { ["type"] = "boolean", ["description"] = "是否包含兄弟节点" }
                },
                ["required"] = new List<string> { "nodePath" }
            }
        },
        new McpToolDef
        {
            Name = "get_scene_tree_stats",
            Description = "获取场景树统计信息（节点数量、类型分布等）",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["rootPath"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "统计根路径，默认 /root" }
                }
            }
        },
        new McpToolDef
        {
            Name = "node_exists",
            Description = "检查节点是否存在",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["nodePath"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "节点路径" }
                },
                ["required"] = new List<string> { "nodePath" }
            }
        },
        new McpToolDef
        {
            Name = "get_node_subtree",
            Description = "获取节点的子树（指定深度，避免完整树太大）",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["nodePath"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "节点路径" },
                    ["maxDepth"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "递归深度，默认 2" },
                    ["includeProperties"] = new Dictionary<string, object> { ["type"] = "boolean", ["description"] = "是否包含属性" }
                },
                ["required"] = new List<string> { "nodePath" }
            }
        },
        new McpToolDef
        {
            Name = "search_nodes",
            Description = "智能搜索节点（组合多个条件）",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["namePattern"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "名称模糊匹配（可选）" },
                    ["nodeType"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "节点类型（可选）" },
                    ["groupName"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "组名（可选）" },
                    ["rootPath"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "搜索根路径" },
                    ["maxResults"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "最大返回结果数" }
                }
            }
        },
        new McpToolDef
        {
            Name = "get_node_context",
            Description = "获取节点的上下文信息（父节点、兄弟节点、子节点）",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["nodePath"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "节点路径" },
                    ["includeParent"] = new Dictionary<string, object> { ["type"] = "boolean", ["description"] = "是否包含父节点" },
                    ["includeSiblings"] = new Dictionary<string, object> { ["type"] = "boolean", ["description"] = "是否包含兄弟节点" },
                    ["includeChildren"] = new Dictionary<string, object> { ["type"] = "boolean", ["description"] = "是否包含子节点" }
                },
                ["required"] = new List<string> { "nodePath" }
            }
        },

        // --- Properties ---
        new McpToolDef
        {
            Name = "get_property",
            Description = "获取节点属性值",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["nodePath"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "节点路径" },
                    ["propertyName"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "属性名称，如 position, visible" }
                },
                ["required"] = new List<string> { "nodePath", "propertyName" }
            }
        },
        new McpToolDef
        {
            Name = "set_property",
            Description = "设置节点属性值",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["nodePath"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "节点路径" },
                    ["propertyName"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "属性名称" },
                    ["value"] = new Dictionary<string, object> { ["type"] = "object", ["description"] = "属性值" }
                },
                ["required"] = new List<string> { "nodePath", "propertyName", "value" }
            }
        },
        new McpToolDef
        {
            Name = "list_properties",
            Description = "列出节点所有属性",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["nodePath"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "节点路径" }
                },
                ["required"] = new List<string> { "nodePath" }
            }
        },

        // --- Methods ---
        new McpToolDef
        {
            Name = "call_method",
            Description = "调用节点方法",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["nodePath"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "节点路径" },
                    ["methodName"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "方法名称" },
                    ["args"] = new Dictionary<string, object> { ["type"] = "array", ["description"] = "方法参数列表" }
                },
                ["required"] = new List<string> { "nodePath", "methodName" }
            }
        },
        new McpToolDef
        {
            Name = "list_methods",
            Description = "列出节点所有方法",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["nodePath"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "节点路径" }
                },
                ["required"] = new List<string> { "nodePath" }
            }
        },

        // --- Resources ---
        new McpToolDef
        {
            Name = "list_resources",
            Description = "列出指定目录下的资源",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "资源目录路径，如 res://assets/" },
                    ["filter"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "资源过滤器（可选）" }
                }
            }
        },
        new McpToolDef
        {
            Name = "load_resource",
            Description = "加载资源",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["resourcePath"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "资源路径，如 res://icon.png" }
                },
                ["required"] = new List<string> { "resourcePath" }
            }
        },
        new McpToolDef
        {
            Name = "get_resource_info",
            Description = "获取资源信息",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["resourcePath"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "资源路径" }
                },
                ["required"] = new List<string> { "resourcePath" }
            }
        },

        // --- Script ---
        new McpToolDef
        {
            Name = "execute_csharp",
            Description = "执行 C# 代码片段",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["code"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "要执行的 C# 代码" }
                },
                ["required"] = new List<string> { "code" }
            }
        },
        new McpToolDef
        {
            Name = "get_global_variables",
            Description = "获取全局变量（场景根的子节点列表）",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>()
            }
        },

        // --- Signals ---
        new McpToolDef
        {
            Name = "get_node_signals",
            Description = "获取节点的所有信号",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["nodePath"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "节点路径" }
                },
                ["required"] = new List<string> { "nodePath" }
            }
        },
        new McpToolDef
        {
            Name = "get_signal_connections",
            Description = "获取信号的连接信息",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["sourceNodePath"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "源节点路径" },
                    ["signalName"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "信号名称" }
                },
                ["required"] = new List<string> { "sourceNodePath", "signalName" }
            }
        },
        new McpToolDef
        {
            Name = "connect_signal",
            Description = "连接信号到方法",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["sourceNodePath"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "源节点路径" },
                    ["signalName"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "信号名称" },
                    ["targetNodePath"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "目标节点路径" },
                    ["targetMethod"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "目标方法名" }
                },
                ["required"] = new List<string> { "sourceNodePath", "signalName", "targetNodePath", "targetMethod" }
            }
        },
        new McpToolDef
        {
            Name = "disconnect_signal",
            Description = "断开信号连接",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["sourceNodePath"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "源节点路径" },
                    ["signalName"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "信号名称" },
                    ["targetNodePath"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "目标节点路径" },
                    ["targetMethod"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "目标方法名" }
                },
                ["required"] = new List<string> { "sourceNodePath", "signalName", "targetNodePath", "targetMethod" }
            }
        },
        new McpToolDef
        {
            Name = "emit_signal",
            Description = "发射自定义信号",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["nodePath"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "节点路径" },
                    ["signalName"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "信号名称" },
                    ["args"] = new Dictionary<string, object> { ["type"] = "array", ["description"] = "信号参数（可选）" }
                },
                ["required"] = new List<string> { "nodePath", "signalName" }
            }
        },
        new McpToolDef
        {
            Name = "start_signal_monitoring",
            Description = "监听信号事件（设置过滤器）",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["signalName"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "要监听的信号名称（为空则监听所有）" },
                    ["maxEvents"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "最大记录数量" }
                }
            }
        },
        new McpToolDef
        {
            Name = "stop_signal_monitoring",
            Description = "停止监听信号事件（查看统计）",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>()
            }
        },
        new McpToolDef
        {
            Name = "get_signal_events",
            Description = "获取已记录的信号事件（支持时间范围查询）",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["count"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "获取最近 N 条事件" },
                    ["nodePath"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "按节点路径过滤（可选）" },
                    ["signalName"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "按信号名称过滤（可选）" },
                    ["startTime"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "开始时间 Unix 时间戳（可选）" },
                    ["endTime"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "结束时间 Unix 时间戳（可选）" }
                }
            }
        },
        new McpToolDef
        {
            Name = "clear_signal_events",
            Description = "清空信号事件记录",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>()
            }
        },

        // --- Debug ---
        new McpToolDef
        {
            Name = "get_logs",
            Description = "获取游戏日志输出",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["count"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "日志数量，默认 50" }
                }
            }
        },
        new McpToolDef
        {
            Name = "get_logs_filtered",
            Description = "获取过滤后的日志",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["level"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "日志级别: error/warning/info/debug" },
                    ["messagePattern"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "消息内容过滤" },
                    ["startTime"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "开始时间戳" },
                    ["endTime"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "结束时间戳" },
                    ["maxCount"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "最大返回数量" }
                }
            }
        },
        new McpToolDef
        {
            Name = "get_log_stats",
            Description = "获取日志统计信息",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>()
            }
        },
        new McpToolDef
        {
            Name = "export_logs",
            Description = "导出所有日志到文件",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["filePath"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "导出路径（可选）" }
                }
            }
        },
        new McpToolDef
        {
            Name = "clear_logs",
            Description = "清空日志缓冲区",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>()
            }
        },
        new McpToolDef
        {
            Name = "add_custom_log",
            Description = "添加自定义日志条目",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["message"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "日志消息" },
                    ["level"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "日志级别: info/warning/error/debug" }
                },
                ["required"] = new List<string> { "message" }
            }
        },
        new McpToolDef
        {
            Name = "get_performance_stats",
            Description = "获取性能统计信息（FPS、内存、节点数等）",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>()
            }
        },
        new McpToolDef
        {
            Name = "take_screenshot",
            Description = "截取游戏画面",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["savePath"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "保存路径（可选），如 user://screenshot.png" }
                }
            }
        },
        new McpToolDef
        {
            Name = "get_time",
            Description = "获取当前时间和引擎运行时间",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>()
            }
        }
    };

    // ===================== Godot Lifecycle =====================

    public override void _Ready()
    {
        // Singleton protection: if another instance exists, this is a duplicate
        if (_instance != null)
        {
            GD.Print("[GodotMcp] Duplicate instance detected, queuing free");
            QueueFree();
            return;
        }
        _instance = this;

        InitializeLogFile();
        InitializeSignalEventsFile();
        StartGlobalSignalMonitoring();

        GD.Print("=".PadRight(60, '='));
        GD.Print("[GodotMcp] Godot MCP v2.0 — Unified HTTP+SSE MCP Server");
        GD.Print("[GodotMcp] Merged McpServer + GodotPlugin into one addon");
        GD.Print("=".PadRight(60, '='));
        GD.Print($"[GodotMcp] {_tools.Count + 1} MCP tools loaded");
        GD.Print("[GodotMcp] ✓ Global signal monitoring started");
        GD.Print("[GodotMcp] ✓ Log auto-recording started");

        StartServer();
    }

    public override void _Process(double delta)
    {
        ProcessPendingRequests();
    }

    public override void _ExitTree()
    {
        if (_instance == this)
            _instance = null;

        _isRunning = false;

        // Close all SSE connections
        lock (_sseLock)
        {
            foreach (var kvp in _sseConnections)
            {
                try { kvp.Value.Response.OutputStream.Close(); } catch { }
                try { kvp.Value.Response.Close(); } catch { }
            }
            _sseConnections.Clear();
        }

        _httpListener?.Stop();
        _httpListener?.Close();
        GD.Print("[GodotMcp] Server stopped");
    }

    // ===================== HTTP Server =====================

    private void StartServer()
    {
        try
        {
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add(BaseUrl);
            _httpListener.Start();
            _isRunning = true;
            GD.Print($"[GodotMcp] ✓ MCP HTTP+SSE server: {BaseUrl}");
            GD.Print($"[GodotMcp]   SSE endpoint:  GET  /sse");
            GD.Print($"[GodotMcp]   MCP endpoint:  POST /messages?session_id=...");
            GD.Print($"[GodotMcp]   Legacy API:    POST /api/<endpoint>");
            GD.Print($"[GodotMcp]   HTTP API:    POST /<endpoint>");
            _ = Task.Run(HandleRequestsAsync);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GodotMcp] ✗ Failed to start server: {ex.Message}");
        }
    }

    private async Task HandleRequestsAsync()
    {
        while (_isRunning && _httpListener != null)
        {
            try
            {
                var context = await _httpListener.GetContextAsync();
                _ = Task.Run(() => HandleRequestAsync(context));
            }
            catch (ObjectDisposedException) { break; }
            catch { }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath ?? "/";
            var method = context.Request.HttpMethod;

            // Route requests
            if (path == "/sse" && method == "GET")
            {
                await HandleSseConnection(context);
            }
            else if (path == "/messages" && method == "POST")
            {
                await HandleMessagePost(context);
            }
            else if (method == "POST")
            {
                await HandleLegacyApi(context);
            }
            else
            {
                context.Response.StatusCode = 405;
                context.Response.Close();
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GodotMcp] Request error: {ex.Message}");
            try
            {
                context.Response.StatusCode = 500;
                context.Response.Close();
            }
            catch { }
        }
    }

    // ===================== SSE Transport =====================

    private async Task HandleSseConnection(HttpListenerContext context)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var response = context.Response;

        response.StatusCode = 200;
        response.ContentType = "text/event-stream";
        response.Headers["Cache-Control"] = "no-cache";
        response.Headers["Connection"] = "keep-alive";
        response.Headers["Access-Control-Allow-Origin"] = "*";

        var conn = new SseConnection
        {
            SessionId = sessionId,
            Response = response,
            Stream = response.OutputStream
        };

        lock (_sseLock)
        {
            _sseConnections[sessionId] = conn;
        }

        GD.Print($"[GodotMcp] SSE client connected: {sessionId}");

        try
        {
            // Send endpoint event (tells client where to POST messages)
            await SendSseEvent(conn.Stream, "endpoint", $"/messages?session_id={sessionId}");
            GD.Print($"[GodotMcp]   → Sent endpoint event for session {sessionId}");

            // Keep connection alive — send keepalive every 15s
            using var cts = new CancellationTokenSource();
            while (_isRunning && !cts.Token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(15000, cts.Token);
                    if (_isRunning)
                    {
                        await SendSseEvent(conn.Stream, null, ": keepalive");
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            GD.Print($"[GodotMcp] SSE client disconnected: {sessionId} ({ex.Message})");
        }
        finally
        {
            lock (_sseLock)
            {
                _sseConnections.Remove(sessionId);
            }
            try { response.OutputStream.Close(); } catch { }
            try { response.Close(); } catch { }
        }
    }

    private async Task SendSseEvent(Stream stream, string eventType, string data)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(eventType))
        {
            sb.Append($"event: {eventType}\n");
        }
        sb.Append($"data: {data}\n\n");
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        await stream.WriteAsync(bytes, 0, bytes.Length);
        await stream.FlushAsync();
    }

    private async Task SendSseMessage(Stream stream, string jsonMessage)
    {
        await SendSseEvent(stream, "message", jsonMessage);
    }

    /// <summary>
    /// Send a JSON-RPC response through the SSE stream for a given session.
    /// Falls back to the first available SSE connection if session not found.
    /// </summary>
    private async Task SendMcpResponse(string sessionId, string jsonResponse)
    {
        SseConnection conn = null;
        lock (_sseLock)
        {
            if (!string.IsNullOrEmpty(sessionId) && _sseConnections.TryGetValue(sessionId, out var c))
            {
                conn = c;
            }
            else if (_sseConnections.Count > 0)
            {
                conn = _sseConnections.Values.First();
            }
        }

        if (conn != null)
        {
            try
            {
                await SendSseMessage(conn.Stream, jsonResponse);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[GodotMcp] Failed to send SSE message: {ex.Message}");
            }
        }
    }

    // ===================== MCP Message Handler (POST /messages) =====================

    private async Task HandleMessagePost(HttpListenerContext context)
    {
        var sessionId = context.Request.QueryString["session_id"];
        using var reader = new StreamReader(context.Request.InputStream);
        var body = await reader.ReadToEndAsync();

        GD.Print($"[GodotMcp] MCP message from session {sessionId}");

        // Process JSON-RPC
        McpJsonRpcResponse response;

        try
        {
            var request = JsonSerializer.Deserialize<McpJsonRpcRequest>(body, JsonOptions);
            if (request == null)
            {
                response = MakeError(null, -32700, "Parse error: invalid JSON-RPC");
            }
            else
            {
                response = await ProcessMcpRequest(request, sessionId);
            }
        }
        catch (JsonException ex)
        {
            response = MakeError(null, -32700, $"Parse error: {ex.Message}");
        }

        // Send response
        var json = JsonSerializer.Serialize(response, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);

        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers["Access-Control-Allow-Origin"] = "*";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        context.Response.Close();
    }

    // ===================== MCP Protocol =====================

    private async Task<McpJsonRpcResponse> ProcessMcpRequest(McpJsonRpcRequest request, string sessionId)
    {
        var id = request.Id;

        try
        {
            switch (request.Method)
            {
                case "initialize":
                    return HandleInitialize(id, request.Params);

                case "ping":
                    return MakeResult(id, new Dictionary<string, object>());

                case "tools/list":
                    return HandleToolsList(id);

                case "tools/call":
                    return await HandleToolsCall(id, request.Params, sessionId);

                case "shutdown":
                    return MakeResult(id, null);

                case "notifications/initialized":
                    // Client confirms initialization — no response needed
                    return null; // Notification: no response

                case "notifications/cancelled":
                    return null; // Notification: no response

                default:
                    return MakeError(id, -32601, $"Method not found: {request.Method}");
            }
        }
        catch (Exception ex)
        {
            return MakeError(id, -32603, $"Internal error: {ex.Message}");
        }
    }

    private McpJsonRpcResponse HandleInitialize(string id, Dictionary<string, object> params_)
    {
        GD.Print("[GodotMcp] MCP initialized");
        return MakeResult(id, new Dictionary<string, object>
        {
            ["protocolVersion"] = ProtocolVersion,
            ["capabilities"] = new Dictionary<string, object>
            {
                ["tools"] = new Dictionary<string, object>()
            },
            ["serverInfo"] = new Dictionary<string, object>
            {
                ["name"] = McpServerName,
                ["version"] = McpServerVersion
            }
        });
    }

    private McpJsonRpcResponse HandleToolsList(string id)
    {
        var toolsList = _tools.Select(t => new Dictionary<string, object>
        {
            ["name"] = t.Name,
            ["description"] = t.Description,
            ["inputSchema"] = t.InputSchema
        }).ToList();

        return MakeResult(id, new Dictionary<string, object>
        {
            ["tools"] = toolsList
        });
    }

    private async Task<McpJsonRpcResponse> HandleToolsCall(string id, Dictionary<string, object> params_, string sessionId)
    {
        if (params_ == null || !params_.ContainsKey("name"))
        {
            return MakeError(id, -32602, "Missing required parameter: name");
        }

        var toolName = params_["name"]?.ToString() ?? "";
        var arguments = params_.ContainsKey("arguments") && params_["arguments"] is Dictionary<string, object> args
            ? args
            : new Dictionary<string, object>();

        // Queue the tool call to be executed on the main thread
        var pending = new PendingRequest
        {
            Type = "tool_call",
            ToolName = toolName,
            ToolArgs = arguments,
            SessionId = sessionId,
            CompletionSource = new TaskCompletionSource<ApiResponse>()
        };

        lock (_queueLock)
        {
            _requestQueue.Enqueue(pending);
        }

        // Wait for main thread to process
        var result = await pending.CompletionSource.Task;

        if (!result.Success)
        {
            return MakeError(id, -32603, result.Error ?? "Tool call failed");
        }

        var content = new List<object>
        {
            new Dictionary<string, object>
            {
                ["type"] = "text",
                ["text"] = JsonSerializer.Serialize(result.Data, JsonOptions)
            }
        };

        return MakeResult(id, new Dictionary<string, object>
        {
            ["content"] = content
        });
    }

    // ===================== Legacy HTTP API Handler (POST /<endpoint>) =====================

    private async Task HandleLegacyApi(HttpListenerContext context)
    {
        var path = context.Request.Url?.AbsolutePath ?? "/";
        using var reader = new StreamReader(context.Request.InputStream);
        var body = await reader.ReadToEndAsync();

        // Create pending request for main thread processing
        var pending = new PendingRequest
        {
            Type = "legacy_api",
            Path = path,
            Body = body,
            CompletionSource = new TaskCompletionSource<ApiResponse>()
        };

        lock (_queueLock)
        {
            _requestQueue.Enqueue(pending);
        }

        var response = await pending.CompletionSource.Task;
        var json = JsonSerializer.Serialize(response, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);

        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers["Access-Control-Allow-Origin"] = "*";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        context.Response.Close();
    }

    // ===================== Main Thread Request Processing =====================

    private void ProcessPendingRequests()
    {
        while (true)
        {
            PendingRequest request = null;
            lock (_queueLock)
            {
                if (_requestQueue.Count > 0)
                {
                    request = _requestQueue.Dequeue();
                }
            }

            if (request == null)
                break;

            try
            {
                if (request.Type == "tool_call")
                {
                    var result = HandleToolOperation(request.ToolName, request.ToolArgs);
                    request.CompletionSource.SetResult(result);
                }
                else
                {
                    var result = RouteLegacyApi(request.Path, request.Body);
                    request.CompletionSource.SetResult(result);
                }
            }
            catch (Exception ex)
            {
                request.CompletionSource.SetResult(ErrorResponse(ex.Message));
            }
        }
    }

    private ApiResponse RouteLegacyApi(string path, string body)
    {
        try
        {
            // Support both /api/<endpoint> and /<endpoint> formats
            var cleanPath = path;
            if (cleanPath.StartsWith("/api"))
                cleanPath = cleanPath[4..];
            if (string.IsNullOrEmpty(cleanPath))
                cleanPath = "/";

            return cleanPath switch
            {
                "/get_scene_tree" => HandleToolOperation("get_scene_tree", DeserializeArgs<SceneTreeRequest>(body)),
                "/get_scene_tree_simple" => HandleToolOperation("get_scene_tree_simple", DeserializeArgs(body)),
                "/get_node_info" => HandleToolOperation("get_node_info", DeserializeArgs<NodePathRequest>(body)),
                "/create_node" => HandleToolOperation("create_node", DeserializeArgs<CreateNodeRequest>(body)),
                "/delete_node" => HandleToolOperation("delete_node", DeserializeArgs<NodePathRequest>(body)),
                "/load_scene" => HandleToolOperation("load_scene", DeserializeArgs<ScenePathRequest>(body)),
                "/get_property" => HandleToolOperation("get_property", DeserializeArgs<PropertyRequest>(body)),
                "/set_property" => HandleToolOperation("set_property", DeserializeArgs<SetPropertyRequest>(body)),
                "/list_properties" => HandleToolOperation("list_properties", DeserializeArgs<NodePathRequest>(body)),
                "/call_method" => HandleToolOperation("call_method", DeserializeArgs<CallMethodRequest>(body)),
                "/list_methods" => HandleToolOperation("list_methods", DeserializeArgs<NodePathRequest>(body)),
                "/execute_csharp" => HandleToolOperation("execute_csharp", DeserializeArgs<CodeRequest>(body)),
                "/get_global_variables" => HandleToolOperation("get_global_variables", new Dictionary<string, object>()),
                "/get_resource_info" => HandleToolOperation("get_resource_info", DeserializeArgs<ResourcePathRequest>(body)),
                "/list_resources" => HandleToolOperation("list_resources", DeserializeArgs<ListResourcesRequest>(body)),
                "/load_resource" => HandleToolOperation("load_resource", DeserializeArgs<ResourcePathRequest>(body)),
                "/get_performance_stats" => HandleToolOperation("get_performance_stats", new Dictionary<string, object>()),
                "/get_logs" => HandleToolOperation("get_logs", DeserializeArgs<LogsRequest>(body)),
                "/take_screenshot" => HandleToolOperation("take_screenshot", DeserializeArgs<ScreenshotRequest>(body)),
                "/get_time" => HandleToolOperation("get_time", new Dictionary<string, object>()),
                "/get_node_children" => HandleToolOperation("get_node_children", DeserializeArgs<NodePathRequest>(body)),
                "/get_node_parent" => HandleToolOperation("get_node_parent", DeserializeArgs<NodePathRequest>(body)),
                "/find_nodes_by_type" => HandleToolOperation("find_nodes_by_type", DeserializeArgs<FindNodesRequest>(body)),
                "/find_nodes_by_name" => HandleToolOperation("find_nodes_by_name", DeserializeArgs<FindNodesRequest>(body)),
                "/find_nodes_by_group" => HandleToolOperation("find_nodes_by_group", DeserializeArgs<FindNodesRequest>(body)),
                "/get_node_ancestors" => HandleToolOperation("get_node_ancestors", DeserializeArgs<AncestorsRequest>(body)),
                "/get_scene_tree_stats" => HandleToolOperation("get_scene_tree_stats", DeserializeArgs<NodePathRequest>(body)),
                "/node_exists" => HandleToolOperation("node_exists", DeserializeArgs<NodePathRequest>(body)),
                "/get_node_subtree" => HandleToolOperation("get_node_subtree", DeserializeArgs<SubtreeRequest>(body)),
                "/search_nodes" => HandleToolOperation("search_nodes", DeserializeArgs<FindNodesRequest>(body)),
                "/get_node_context" => HandleToolOperation("get_node_context", DeserializeArgs<NodeContextRequest>(body)),
                "/get_node_signals" => HandleToolOperation("get_node_signals", DeserializeArgs<NodePathRequest>(body)),
                "/get_signal_connections" => HandleToolOperation("get_signal_connections", DeserializeArgs<SignalConnectionRequest>(body)),
                "/connect_signal" => HandleToolOperation("connect_signal", DeserializeArgs<SignalConnectionRequest>(body)),
                "/disconnect_signal" => HandleToolOperation("disconnect_signal", DeserializeArgs<SignalConnectionRequest>(body)),
                "/emit_signal" => HandleToolOperation("emit_signal", DeserializeArgs<SignalEmitRequest>(body)),
                "/start_signal_monitoring" => HandleToolOperation("start_signal_monitoring", DeserializeArgs<SignalMonitorRequest>(body)),
                "/stop_signal_monitoring" => HandleToolOperation("stop_signal_monitoring", new Dictionary<string, object>()),
                "/get_signal_events" => HandleToolOperation("get_signal_events", DeserializeArgs<SignalEventQueryRequest>(body)),
                "/clear_signal_events" => HandleToolOperation("clear_signal_events", new Dictionary<string, object>()),
                "/get_logs_filtered" => HandleToolOperation("get_logs_filtered", DeserializeArgs<LogFilterRequest>(body)),
                "/get_log_stats" => HandleToolOperation("get_log_stats", new Dictionary<string, object>()),
                "/export_logs" => HandleToolOperation("export_logs", DeserializeArgs<LogExportRequest>(body)),
                "/clear_logs" => HandleToolOperation("clear_logs", new Dictionary<string, object>()),
                "/add_custom_log" => HandleToolOperation("add_custom_log", DeserializeArgs<CustomLogRequest>(body)),
                _ => ErrorResponse($"Unknown endpoint: {path}")
            };
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GodotMcp] Route error: {ex.Message}");
            return ErrorResponse(ex.Message);
        }
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
                return je.EnumerateArray()
                    .Select(e => (object)(e.ValueKind == JsonValueKind.String ? e.GetString() :
                                         e.ValueKind == JsonValueKind.Number ? e.GetDouble() :
                                         e.ValueKind == JsonValueKind.True ? true :
                                         e.ValueKind == JsonValueKind.False ? false :
                                         e.ToString()))
                    .ToList();
            }
        }
        return null;
    }

    private static Dictionary<string, object> DeserializeArgs<T>(string body) where T : new()
    {
        try
        {
            var typed = JsonSerializer.Deserialize<T>(body, JsonOptions);
            if (typed == null) return new Dictionary<string, object>();

            // Convert typed object to dictionary using reflection
            var dict = new Dictionary<string, object>();
            foreach (var prop in typeof(T).GetProperties())
            {
                var val = prop.GetValue(typed);
                if (val != null)
                {
                    var jsonName = prop.GetCustomAttributes(typeof(JsonPropertyNameAttribute), false)
                        .FirstOrDefault() is JsonPropertyNameAttribute jna
                        ? jna.Name
                        : prop.Name.ToCamelCase();
                    dict[jsonName] = val;
                }
            }
            return dict;
        }
        catch
        {
            return new Dictionary<string, object>();
        }
    }

    private static Dictionary<string, object> DeserializeArgs(string body)
    {
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(body, JsonOptions);
            return dict ?? new Dictionary<string, object>();
        }
        catch
        {
            return new Dictionary<string, object>();
        }
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

        var context = new Dictionary<string, object>
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
        node.Set(propertyName, ToVar(value));
        return Ok($"Set: {propertyName}");
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

    private ApiResponse ExecuteCSharp(string code) => Ok("Requires Roslyn runtime. Use alternative approach.");

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
        GD.Print($"[GodotMcp] {message}");
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

    private Variant ToVar(object? o)
    {
        return o switch
        {
            null => default,
            bool b => b,
            int i => i,
            long l => l,
            float f => f,
            double d => d,
            string s => s,
            JsonElement je => je.ValueKind switch
            {
                JsonValueKind.Null => default,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => je.GetDouble(),
                JsonValueKind.String => je.GetString() ?? "",
                _ => je.ToString() ?? ""
            },
            _ => o.ToString() ?? ""
        };
    }

    private ApiResponse Ok(object? d) => new() { Success = true, Data = d };
    private ApiResponse Err(string msg) => new() { Success = false, Error = msg };
    private ApiResponse ErrorResponse(string msg) => new() { Success = false, Error = msg };

    private McpJsonRpcResponse MakeResult(string? id, object? result)
    {
        return new McpJsonRpcResponse
        {
            Jsonrpc = "2.0",
            Id = id,
            Result = result
        };
    }

    private McpJsonRpcResponse MakeError(string? id, int code, string message)
    {
        return new McpJsonRpcResponse
        {
            Jsonrpc = "2.0",
            Id = id,
            Error = new McpError { Code = code, Message = message }
        };
    }
}

// ===================== MCP Protocol Types =====================

internal class McpJsonRpcRequest
{
    [JsonPropertyName("jsonrpc")]
    public string Jsonrpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("method")]
    public string Method { get; set; } = "";

    [JsonPropertyName("params")]
    public Dictionary<string, object>? Params { get; set; }
}

internal class McpJsonRpcResponse
{
    [JsonPropertyName("jsonrpc")]
    public string Jsonrpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("result")]
    public object? Result { get; set; }

    [JsonPropertyName("error")]
    public McpError? Error { get; set; }
}

internal class McpError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

internal class McpToolDef
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public Dictionary<string, object> InputSchema { get; set; } = new();
}

internal class SseConnection
{
    public string SessionId { get; set; } = "";
    public HttpListenerResponse Response { get; set; } = null!;
    public Stream Stream { get; set; } = null!;
}

// ===================== Internal Request Types =====================

internal class PendingRequest
{
    public string Type { get; set; } = ""; // "tool_call" or "legacy_api"
    public string Path { get; set; } = "";
    public string Body { get; set; } = "";
    public string ToolName { get; set; } = "";
    public Dictionary<string, object> ToolArgs { get; set; } = new();
    public string SessionId { get; set; } = "";
    public TaskCompletionSource<ApiResponse> CompletionSource { get; set; } = null!;
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

// ===================== Legacy Request Models (for HTTP API backward compat) =====================

public class SceneTreeRequest
{
    [JsonPropertyName("includeProperties")]
    public bool IncludeProperties { get; set; }
}

public class NodePathRequest
{
    [JsonPropertyName("nodePath")]
    public string NodePath { get; set; } = "";
}

public class CreateNodeRequest
{
    [JsonPropertyName("parentPath")]
    public string ParentPath { get; set; } = "";
    [JsonPropertyName("nodeType")]
    public string NodeType { get; set; } = "";
    [JsonPropertyName("nodeName")]
    public string NodeName { get; set; } = "";
}

public class ScenePathRequest
{
    [JsonPropertyName("scenePath")]
    public string ScenePath { get; set; } = "";
}

public class PropertyRequest
{
    [JsonPropertyName("nodePath")]
    public string NodePath { get; set; } = "";
    [JsonPropertyName("propertyName")]
    public string PropertyName { get; set; } = "";
}

public class SetPropertyRequest
{
    [JsonPropertyName("nodePath")]
    public string NodePath { get; set; } = "";
    [JsonPropertyName("propertyName")]
    public string PropertyName { get; set; } = "";
    [JsonPropertyName("value")]
    public object? Value { get; set; }
}

public class CallMethodRequest
{
    [JsonPropertyName("nodePath")]
    public string NodePath { get; set; } = "";
    [JsonPropertyName("methodName")]
    public string MethodName { get; set; } = "";
    [JsonPropertyName("args")]
    public List<object>? Args { get; set; }
}

public class CodeRequest
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "";
}

public class ResourcePathRequest
{
    [JsonPropertyName("resourcePath")]
    public string ResourcePath { get; set; } = "";
}

public class ListResourcesRequest
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "res://";
    [JsonPropertyName("filter")]
    public string? Filter { get; set; }
}

public class LogsRequest
{
    [JsonPropertyName("count")]
    public int Count { get; set; } = 50;
}

public class ScreenshotRequest
{
    [JsonPropertyName("savePath")]
    public string? SavePath { get; set; }
}

public class FindNodesRequest
{
    [JsonPropertyName("nodeType")]
    public string? NodeType { get; set; }
    [JsonPropertyName("namePattern")]
    public string? NamePattern { get; set; }
    [JsonPropertyName("rootPath")]
    public string RootPath { get; set; } = "/root";
    [JsonPropertyName("caseSensitive")]
    public bool CaseSensitive { get; set; }
    [JsonPropertyName("exactMatch")]
    public bool ExactMatch { get; set; }
    [JsonPropertyName("groupName")]
    public string? GroupName { get; set; }
    [JsonPropertyName("maxResults")]
    public int MaxResults { get; set; } = 50;
}

public class SubtreeRequest
{
    [JsonPropertyName("nodePath")]
    public string NodePath { get; set; } = "";
    [JsonPropertyName("maxDepth")]
    public int MaxDepth { get; set; } = 2;
    [JsonPropertyName("includeProperties")]
    public bool IncludeProperties { get; set; }
}

public class AncestorsRequest
{
    [JsonPropertyName("nodePath")]
    public string NodePath { get; set; } = "";
    [JsonPropertyName("levels")]
    public int Levels { get; set; } = -1;
    [JsonPropertyName("includeSiblings")]
    public bool IncludeSiblings { get; set; }
}

public class NodeContextRequest
{
    [JsonPropertyName("nodePath")]
    public string NodePath { get; set; } = "";
    [JsonPropertyName("includeParent")]
    public bool IncludeParent { get; set; } = true;
    [JsonPropertyName("includeSiblings")]
    public bool IncludeSiblings { get; set; } = true;
    [JsonPropertyName("includeChildren")]
    public bool IncludeChildren { get; set; } = true;
}

public class SignalConnectionRequest
{
    [JsonPropertyName("sourceNodePath")]
    public string SourceNodePath { get; set; } = "";
    [JsonPropertyName("signalName")]
    public string SignalName { get; set; } = "";
    [JsonPropertyName("targetNodePath")]
    public string? TargetNodePath { get; set; }
    [JsonPropertyName("targetMethod")]
    public string? TargetMethod { get; set; }
}

public class SignalEmitRequest
{
    [JsonPropertyName("nodePath")]
    public string NodePath { get; set; } = "";
    [JsonPropertyName("signalName")]
    public string SignalName { get; set; } = "";
    [JsonPropertyName("args")]
    public List<object>? Args { get; set; }
}

public class SignalMonitorRequest
{
    [JsonPropertyName("nodePath")]
    public string? NodePath { get; set; }
    [JsonPropertyName("signalName")]
    public string? SignalName { get; set; }
    [JsonPropertyName("maxEvents")]
    public int MaxEvents { get; set; } = 5000;
}

public class SignalEventQueryRequest
{
    [JsonPropertyName("count")]
    public int Count { get; set; } = 50;
    [JsonPropertyName("nodePath")]
    public string? NodePath { get; set; }
    [JsonPropertyName("signalName")]
    public string? SignalName { get; set; }
    [JsonPropertyName("startTime")]
    public long? StartTime { get; set; }
    [JsonPropertyName("endTime")]
    public long? EndTime { get; set; }
}

public class LogFilterRequest
{
    [JsonPropertyName("level")]
    public string? Level { get; set; }
    [JsonPropertyName("messagePattern")]
    public string? MessagePattern { get; set; }
    [JsonPropertyName("startTime")]
    public long? StartTime { get; set; }
    [JsonPropertyName("endTime")]
    public long? EndTime { get; set; }
    [JsonPropertyName("maxCount")]
    public int MaxCount { get; set; } = 100;
}

public class LogExportRequest
{
    [JsonPropertyName("filePath")]
    public string? FilePath { get; set; }
}

public class CustomLogRequest
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
    [JsonPropertyName("level")]
    public string Level { get; set; } = "info";
}

// ===================== Extension Methods =====================

internal static class StringExtensions
{
    public static string ToCamelCase(this string str)
    {
        if (string.IsNullOrEmpty(str) || char.IsLower(str[0]))
            return str;
        return char.ToLower(str[0]) + str[1..];
    }
}
