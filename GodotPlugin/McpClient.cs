using Godot;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Linq;

/// <summary>
/// MCP HTTP API 服务器 v4.1 - 线程安全版本,所有场景树操作在主线程执行
/// </summary>
public partial class McpClient : Node
{
    private HttpListener? _httpListener;
    private bool _isRunning = false;
    private const string ApiUrl = "http://127.0.0.1:7777/";
    private readonly List<LogEntry> _logs = new();
    
    // 线程安全的请求队列
    private readonly Queue<PendingRequest> _requestQueue = new();
    private readonly object _queueLock = new();
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public override void _Ready()
    {
        GD.Print("=".PadRight(60, '='));
        GD.Print("[MCP] Godot MCP v4.1 - 线程安全独立HTTP端点");
        GD.Print("=".PadRight(60, '='));
        StartServer();
    }

    public override void _Process(double delta)
    {
        // 在主线程处理所有待处理的请求
        ProcessPendingRequests();
    }

    private async void StartServer()
    {
        try
        {
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add(ApiUrl);
            _httpListener.Start();
            _isRunning = true;
            GD.Print($"[MCP] ✓ 监听: {ApiUrl}");
            GD.Print("[MCP] ✓ 19个独立HTTP端点已就绪");
            _ = Task.Run(HandleRequestsAsync);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MCP] ✗ 启动失败: {ex.Message}");
        }
    }

    private async Task HandleRequestsAsync()
    {
        while (_isRunning && _httpListener != null)
        {
            try
            {
                var context = await _httpListener.GetContextAsync();
                _ = Task.Run(() => ProcessRequestAsync(context));
            }
            catch { }
        }
    }

    private async Task ProcessRequestAsync(HttpListenerContext context)
    {
        try
        {
            if (context.Request.HttpMethod != "POST")
            {
                context.Response.StatusCode = 405;
                context.Response.Close();
                return;
            }

            var path = context.Request.Url?.AbsolutePath ?? "/";
            GD.Print($"[MCP] {path}");

            using var reader = new System.IO.StreamReader(context.Request.InputStream);
            var body = await reader.ReadToEndAsync();

            // 创建待处理请求并等待主线程处理
            var pendingRequest = new PendingRequest
            {
                Path = path,
                Body = body,
                CompletionSource = new TaskCompletionSource<ApiResponse>()
            };

            lock (_queueLock)
            {
                _requestQueue.Enqueue(pendingRequest);
            }

            // 等待主线程处理完成
            var response = await pendingRequest.CompletionSource.Task;
            
            var json = JsonSerializer.Serialize(response, JsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);

            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            context.Response.Close();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MCP] Error: {ex.Message}");
            context.Response.StatusCode = 500;
            context.Response.Close();
        }
    }

    /// <summary>
    /// 在主线程处理所有待处理的请求
    /// </summary>
    private void ProcessPendingRequests()
    {
        while (true)
        {
            PendingRequest? request = null;
            
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
                var response = RouteRequest(request.Path, request.Body);
                request.CompletionSource.SetResult(response);
            }
            catch (Exception ex)
            {
                request.CompletionSource.SetResult(ErrorResponse(ex.Message));
            }
        }
    }

    /// <summary>
    /// 根据 HTTP 路径路由到对应方法
    /// </summary>
    private ApiResponse RouteRequest(string path, string body)
    {
        try
        {
            return path switch
            {
                "/get_scene_tree" => GetSceneTree(Deserialize<SceneTreeRequest>(body)),
                "/get_node_info" => GetNodeInfo(Deserialize<NodePathRequest>(body)),
                "/create_node" => CreateNode(Deserialize<CreateNodeRequest>(body)),
                "/delete_node" => DeleteNode(Deserialize<NodePathRequest>(body)),
                "/load_scene" => LoadScene(Deserialize<ScenePathRequest>(body)),
                "/get_property" => GetProperty(Deserialize<PropertyRequest>(body)),
                "/set_property" => SetProperty(Deserialize<SetPropertyRequest>(body)),
                "/list_properties" => ListProperties(Deserialize<NodePathRequest>(body)),
                "/call_method" => CallMethod(Deserialize<CallMethodRequest>(body)),
                "/list_methods" => ListMethods(Deserialize<NodePathRequest>(body)),
                "/execute_csharp" => ExecuteCSharp(Deserialize<CodeRequest>(body)),
                "/get_global_variables" => GetGlobalVariables(),
                "/get_resource_info" => GetResourceInfo(Deserialize<ResourcePathRequest>(body)),
                "/list_resources" => ListResources(Deserialize<ListResourcesRequest>(body)),
                "/load_resource" => LoadResource(Deserialize<ResourcePathRequest>(body)),
                "/get_performance_stats" => GetPerformanceStats(),
                "/get_logs" => GetLogs(Deserialize<LogsRequest>(body)),
                "/take_screenshot" => TakeScreenshot(Deserialize<ScreenshotRequest>(body)),
                "/get_time" => GetTime(),
                "/get_node_children" => GetNodeChildren(Deserialize<NodePathRequest>(body)),
                "/get_node_parent" => GetNodeParent(Deserialize<NodePathRequest>(body)),
                "/find_nodes_by_type" => FindNodesByType(Deserialize<FindNodesRequest>(body)),
                "/find_nodes_by_name" => FindNodesByName(Deserialize<FindNodesRequest>(body)),
                "/find_nodes_by_group" => FindNodesByGroup(Deserialize<FindNodesRequest>(body)),
                "/get_node_ancestors" => GetNodeAncestors(Deserialize<AncestorsRequest>(body)),
                "/get_scene_tree_stats" => GetSceneTreeStats(Deserialize<NodePathRequest>(body)),
                "/node_exists" => NodeExists(Deserialize<NodePathRequest>(body)),
                "/get_node_subtree" => GetNodeSubtree(Deserialize<SubtreeRequest>(body)),
                "/search_nodes" => SearchNodes(Deserialize<FindNodesRequest>(body)),
                "/get_node_context" => GetNodeContext(Deserialize<NodeContextRequest>(body)),
                _ => ErrorResponse($"未知端点: {path}")
            };
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MCP] 路由错误: {ex.Message}");
            return ErrorResponse(ex.Message);
        }
    }

    private T Deserialize<T>(string json) where T : new()
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? new T();
        }
        catch
        {
            return new T();
        }
    }

    // ========== 场景树方法 (获取游戏运行时信息) ==========

    private ApiResponse GetSceneTree(SceneTreeRequest req)
    {
        var root = GetTree().Root;  // 现在安全了,在主线程执行
        var tree = BuildTree(root, req.IncludeProperties);
        return Ok(tree);
    }

    private ApiResponse GetNodeInfo(NodePathRequest req)
    {
        var node = GetNodeOrNull(req.NodePath);
        if (node == null) return Err($"找不到节点: {req.NodePath}");

        return Ok(new NodeInfo
        {
            Name = node.Name,
            Type = node.GetType().Name,
            Path = node.GetPath().ToString(),
            Children = node.GetChildren().Select(c => c.Name.ToString()).ToList(),
            Properties = GetProps(node),
            Methods = GetMethods(node),
            Signals = GetSignals(node)
        });
    }

    private ApiResponse CreateNode(CreateNodeRequest req)
    {
        var parent = GetNodeOrNull(req.ParentPath);
        if (parent == null) return Err($"父节点不存在: {req.ParentPath}");

        var node = CreateByType(req.NodeType);
        if (node == null) return Err($"无法创建类型: {req.NodeType}");

        node.Name = req.NodeName;
        parent.AddChild(node);
        return Ok(new { path = node.GetPath().ToString() });
    }

    private ApiResponse DeleteNode(NodePathRequest req)
    {
        var node = GetNodeOrNull(req.NodePath);
        if (node == null) return Err($"节点不存在: {req.NodePath}");

        node.QueueFree();
        return Ok("已删除");
    }

    private ApiResponse LoadScene(ScenePathRequest req)
    {
        GetTree().CallDeferred("change_scene_to_file", req.ScenePath);
        return Ok($"加载中: {req.ScenePath}");
    }

    // ========== 属性方法 (游戏运行时属性) ==========

    private ApiResponse GetProperty(PropertyRequest req)
    {
        var node = GetNodeOrNull(req.NodePath);
        if (node == null) return Err($"节点不存在: {req.NodePath}");

        var value = node.Get(req.PropertyName);  // 获取运行时属性值
        return Ok(new { value = Conv(value) });
    }

    private ApiResponse SetProperty(SetPropertyRequest req)
    {
        var node = GetNodeOrNull(req.NodePath);
        if (node == null) return Err($"节点不存在: {req.NodePath}");

        node.Set(req.PropertyName, ToVar(req.Value));  // 设置运行时属性
        return Ok($"已设置: {req.PropertyName}");
    }

    private ApiResponse ListProperties(NodePathRequest req)
    {
        var node = GetNodeOrNull(req.NodePath);
        if (node == null) return Err($"节点不存在: {req.NodePath}");

        return Ok(GetProps(node));
    }

    // ========== 方法调用 (运行时方法) ==========

    private ApiResponse CallMethod(CallMethodRequest req)
    {
        var node = GetNodeOrNull(req.NodePath);
        if (node == null) return Err($"节点不存在: {req.NodePath}");

        var args = new Godot.Collections.Array();
        req.Args?.ForEach(a => args.Add(ToVar(a)));
        
        var result = node.Call(req.MethodName, args);  // 运行时调用
        return Ok(Conv(result));
    }

    private ApiResponse ListMethods(NodePathRequest req)
    {
        var node = GetNodeOrNull(req.NodePath);
        if (node == null) return Err($"节点不存在: {req.NodePath}");

        return Ok(GetMethods(node));
    }

    // ========== 脚本和资源 ==========

    private ApiResponse ExecuteCSharp(CodeRequest req) => Ok("需要 Roslyn");

    private ApiResponse GetGlobalVariables()
    {
        return Ok(GetTree().Root.GetChildren().ToDictionary(
            c => c.Name.ToString(),
            c => (object)new { type = c.GetType().Name, path = c.GetPath().ToString() }
        ));
    }

    private ApiResponse GetResourceInfo(ResourcePathRequest req)
    {
        var r = GD.Load(req.ResourcePath);
        return r == null ? Err("资源不存在") : Ok(new { type = r.GetType().Name, path = r.ResourcePath });
    }

    private ApiResponse ListResources(ListResourcesRequest req)
    {
        var list = new List<string>();
        var dir = DirAccess.Open(req.Path);
        if (dir != null)
        {
            dir.ListDirBegin();
            for (var f = dir.GetNext(); f != ""; f = dir.GetNext())
                if (!dir.CurrentIsDir()) list.Add(f);
            dir.ListDirEnd();
        }
        return Ok(list);
    }

    private ApiResponse LoadResource(ResourcePathRequest req)
    {
        var r = GD.Load(req.ResourcePath);
        return r == null ? Err("加载失败") : Ok(new { type = r.GetType().Name });
    }

    // ========== 调试方法 (运行时统计) ==========

    private ApiResponse GetPerformanceStats()
    {
        return Ok(new
        {
            fps = Engine.GetFramesPerSecond(),  // 实时 FPS
            processTime = Performance.GetMonitor(Performance.Monitor.TimeProcess),
            physicsTime = Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess),
            memoryUsage = Performance.GetMonitor(Performance.Monitor.MemoryStatic),  // 实时内存
            nodeCount = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount),  // 实时节点数
            drawCalls = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)
        });
    }

    private ApiResponse GetLogs(LogsRequest req) => Ok(_logs.TakeLast(req.Count));

    private ApiResponse TakeScreenshot(ScreenshotRequest req)
    {
        var path = req.SavePath ?? "user://screenshot.png";
        var img = GetViewport().GetTexture().GetImage();  // 当前画面截图
        img.SavePng(path);
        return Ok(new { path });
    }

    private ApiResponse GetTime() => Ok(new
    {
        unix = Time.GetUnixTimeFromSystem(),
        datetime = DateTime.Now.ToString("s"),
        ticks = Time.GetTicksMsec()
    });

    // ========== 扩展场景树查询方法 ==========

    private ApiResponse GetNodeChildren(NodePathRequest req)
    {
        var node = GetNodeOrNull(req.NodePath);
        if (node == null) return Err($"节点不存在: {req.NodePath}");

        var children = node.GetChildren().Select(c => new
        {
            name = c.Name.ToString(),
            type = c.GetType().Name,
            path = c.GetPath().ToString()
        }).ToList();

        return Ok(children);
    }

    private ApiResponse GetNodeParent(NodePathRequest req)
    {
        var node = GetNodeOrNull(req.NodePath);
        if (node == null) return Err($"节点不存在: {req.NodePath}");

        var parent = node.GetParent();
        if (parent == null) return Ok(null);

        return Ok(new
        {
            name = parent.Name.ToString(),
            type = parent.GetType().Name,
            path = parent.GetPath().ToString()
        });
    }

    private ApiResponse FindNodesByType(FindNodesRequest req)
    {
        var root = GetNodeOrNull(req.RootPath);
        if (root == null) return Err($"根节点不存在: {req.RootPath}");

        var results = new List<object>();
        FindNodesByTypeRecursive(root, req.NodeType ?? "", results);
        
        return Ok(new { count = results.Count, nodes = results });
    }

    private void FindNodesByTypeRecursive(Node node, string targetType, List<object> results)
    {
        if (node.GetType().Name == targetType)
        {
            results.Add(new
            {
                name = node.Name.ToString(),
                type = node.GetType().Name,
                path = node.GetPath().ToString()
            });
        }

        foreach (Node child in node.GetChildren())
        {
            FindNodesByTypeRecursive(child, targetType, results);
        }
    }

    private ApiResponse FindNodesByName(FindNodesRequest req)
    {
        var root = GetNodeOrNull(req.RootPath);
        if (root == null) return Err($"根节点不存在: {req.RootPath}");

        var pattern = req.NamePattern ?? "";
        var results = new List<object>();
        var comparison = req.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        
        FindNodesByNameRecursive(root, pattern, results, req.ExactMatch, comparison, req.MaxResults);
        
        return Ok(new { count = results.Count, nodes = results, truncated = results.Count >= req.MaxResults });
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
            results.Add(new
            {
                name = nodeName,
                type = node.GetType().Name,
                path = node.GetPath().ToString()
            });
        }

        foreach (Node child in node.GetChildren())
        {
            if (results.Count >= maxResults) break;
            FindNodesByNameRecursive(child, pattern, results, exactMatch, comparison, maxResults);
        }
    }

    private ApiResponse FindNodesByGroup(FindNodesRequest req)
    {
        var root = GetNodeOrNull(req.RootPath);
        if (root == null) return Err($"根节点不存在: {req.RootPath}");

        var groupName = req.GroupName ?? "";
        var nodes = GetTree().GetNodesInGroup(groupName);
        
        var results = nodes.Select(n => new
        {
            name = n.Name.ToString(),
            type = n.GetType().Name,
            path = n.GetPath().ToString(),
            groups = n.GetGroups().Select(g => g.ToString()).ToList()
        }).Take(req.MaxResults).ToList();

        return Ok(new { count = results.Count, nodes = results, groupName });
    }

    private ApiResponse GetNodeAncestors(AncestorsRequest req)
    {
        var node = GetNodeOrNull(req.NodePath);
        if (node == null) return Err($"节点不存在: {req.NodePath}");

        var ancestors = new List<object>();
        var current = node.GetParent();
        var level = 0;

        while (current != null && (req.Levels < 0 || level < req.Levels))
        {
            var ancestorInfo = new Dictionary<string, object>
            {
                ["level"] = level + 1,
                ["name"] = current.Name.ToString(),
                ["type"] = current.GetType().Name,
                ["path"] = current.GetPath().ToString()
            };

            if (req.IncludeSiblings)
            {
                var parent = current.GetParent();
                if (parent != null)
                {
                    var siblings = parent.GetChildren()
                        .Where(c => c.GetPath() != current.GetPath())
                        .Select(c => new
                        {
                            name = c.Name.ToString(),
                            type = c.GetType().Name,
                            path = c.GetPath().ToString()
                        }).ToList();
                    
                    ancestorInfo["siblings"] = siblings;
                    ancestorInfo["siblingCount"] = siblings.Count;
                }
            }

            ancestors.Add(ancestorInfo);
            current = current.GetParent();
            level++;
        }

        return Ok(new 
        { 
            nodePath = req.NodePath,
            ancestorCount = ancestors.Count,
            ancestors 
        });
    }

    private ApiResponse GetSceneTreeStats(NodePathRequest req)
    {
        var root = GetNodeOrNull(req.NodePath);
        if (root == null) return Err($"节点不存在: {req.NodePath}");

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
            rootPath = req.NodePath
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
            var groupName = group.ToString();
            groups[groupName] = groups.GetValueOrDefault(groupName, 0) + 1;
        }

        foreach (Node child in node.GetChildren())
        {
            CollectStatsRecursive(child, stats, groups, ref total, ref maxDepth, currentDepth + 1);
        }
    }

    private ApiResponse NodeExists(NodePathRequest req)
    {
        var node = GetNodeOrNull(req.NodePath);
        return Ok(new { exists = node != null, path = req.NodePath });
    }

    private ApiResponse GetNodeSubtree(SubtreeRequest req)
    {
        var root = GetNodeOrNull(req.NodePath);
        if (root == null) return Err($"节点不存在: {req.NodePath}");

        var tree = BuildTreeWithDepth(root, req.MaxDepth, 0, req.IncludeProperties);
        return Ok(tree);
    }

    private Dictionary<string, object> BuildTreeWithDepth(Node n, int maxDepth, int currentDepth, bool includeProperties)
    {
        var d = new Dictionary<string, object>
        {
            ["name"] = n.Name,
            ["type"] = n.GetType().Name,
            ["path"] = n.GetPath().ToString(),
            ["depth"] = currentDepth
        };

        if (includeProperties)
        {
            d["properties"] = GetProps(n);
        }

        if (maxDepth < 0 || currentDepth < maxDepth)
        {
            d["children"] = n.GetChildren()
                .Select(c => BuildTreeWithDepth(c, maxDepth, currentDepth + 1, includeProperties))
                .ToList();
        }
        else
        {
            d["children"] = new List<object>();
            d["hasChildren"] = n.GetChildCount() > 0;
            d["childCount"] = n.GetChildCount();
        }

        return d;
    }

    private ApiResponse SearchNodes(FindNodesRequest req)
    {
        var root = GetNodeOrNull(req.RootPath);
        if (root == null) return Err($"根节点不存在: {req.RootPath}");

        var results = new List<object>();
        SearchNodesRecursive(root, req, results);

        return Ok(new 
        { 
            count = results.Count, 
            nodes = results,
            truncated = results.Count >= req.MaxResults,
            criteria = new
            {
                namePattern = req.NamePattern,
                nodeType = req.NodeType,
                groupName = req.GroupName
            }
        });
    }

    private void SearchNodesRecursive(Node node, FindNodesRequest req, List<object> results)
    {
        if (results.Count >= req.MaxResults) return;

        bool matches = true;

        // 检查名称匹配
        if (!string.IsNullOrEmpty(req.NamePattern))
        {
            var comparison = req.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var nodeName = node.Name.ToString();
            matches = req.ExactMatch 
                ? nodeName.Equals(req.NamePattern, comparison)
                : nodeName.Contains(req.NamePattern, comparison);
        }

        // 检查类型匹配
        if (matches && !string.IsNullOrEmpty(req.NodeType))
        {
            matches = node.GetType().Name == req.NodeType;
        }

        // 检查组匹配
        if (matches && !string.IsNullOrEmpty(req.GroupName))
        {
            matches = node.IsInGroup(req.GroupName);
        }

        if (matches)
        {
            results.Add(new
            {
                name = node.Name.ToString(),
                type = node.GetType().Name,
                path = node.GetPath().ToString(),
                groups = node.GetGroups().Select(g => g.ToString()).ToList()
            });
        }

        foreach (Node child in node.GetChildren())
        {
            if (results.Count >= req.MaxResults) break;
            SearchNodesRecursive(child, req, results);
        }
    }

    private ApiResponse GetNodeContext(NodeContextRequest req)
    {
        var node = GetNodeOrNull(req.NodePath);
        if (node == null) return Err($"节点不存在: {req.NodePath}");

        var context = new Dictionary<string, object>
        {
            ["node"] = new
            {
                name = node.Name.ToString(),
                type = node.GetType().Name,
                path = node.GetPath().ToString(),
                groups = node.GetGroups().Select(g => g.ToString()).ToList()
            }
        };

        if (req.IncludeParent)
        {
            var parent = node.GetParent();
            context["parent"] = parent != null ? new
            {
                name = parent.Name.ToString(),
                type = parent.GetType().Name,
                path = parent.GetPath().ToString()
            } : null;
        }

        if (req.IncludeSiblings)
        {
            var parent = node.GetParent();
            if (parent != null)
            {
                var siblings = parent.GetChildren()
                    .Where(c => c.GetPath() != node.GetPath())
                    .Select(c => new
                    {
                        name = c.Name.ToString(),
                        type = c.GetType().Name,
                        path = c.GetPath().ToString()
                    }).ToList();
                
                context["siblings"] = siblings;
                context["siblingCount"] = siblings.Count;
            }
        }

        if (req.IncludeChildren)
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

    // ========== 辅助方法 ==========

    private Dictionary<string, object> BuildTree(Node n, bool props)
    {
        var d = new Dictionary<string, object>
        {
            ["name"] = n.Name,
            ["type"] = n.GetType().Name,
            ["path"] = n.GetPath().ToString(),
            ["children"] = n.GetChildren().Select(c => BuildTree(c, props)).ToList()
        };
        if (props) d["properties"] = GetProps(n);
        return d;
    }

    private Dictionary<string, object?> GetProps(Node n) =>
        n.GetPropertyList()
            .Select(p => (Godot.Collections.Dictionary)p)
            .Where(p => !p["name"].AsString().StartsWith("_"))
            .ToDictionary(p => p["name"].AsString(), p =>
            {
                try { return Conv(n.Get(p["name"].AsString())); }
                catch { return null; }
            });

    private List<string> GetMethods(Node n) =>
        n.GetMethodList()
            .Select(m => ((Godot.Collections.Dictionary)m)["name"].AsString())
            .Where(m => !m.StartsWith("_"))
            .ToList();

    private List<string> GetSignals(Node n) =>
        n.GetSignalList()
            .Select(s => ((Godot.Collections.Dictionary)s)["name"].AsString())
            .ToList();

    private Node? CreateByType(string t)
    {
        var type = Type.GetType($"Godot.{t}") ?? Type.GetType(t);
        return type != null && typeof(Node).IsAssignableFrom(type)
            ? (Node?)Activator.CreateInstance(type)
            : null;
    }

    private object? Conv(Variant v) => v.VariantType switch
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

    private Variant ToVar(object? o) => o switch
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
            _ => je.ToString()
        },
        _ => o.ToString() ?? ""
    };

    private ApiResponse Ok(object? d) => new() { Success = true, Data = d };
    private ApiResponse Err(string msg) => new() { Success = false, Error = msg };
    private ApiResponse ErrorResponse(string msg) => new() { Success = false, Error = msg };

    public override void _ExitTree()
    {
        _isRunning = false;
        _httpListener?.Stop();
        _httpListener?.Close();
        GD.Print("[MCP] 已停止");
    }
}

// ========== 强类型请求模型 ==========

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
    public bool CaseSensitive { get; set; } = false;
    
    [JsonPropertyName("exactMatch")]
    public bool ExactMatch { get; set; } = false;
    
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
    public bool IncludeProperties { get; set; } = false;
}

public class AncestorsRequest
{
    [JsonPropertyName("nodePath")]
    public string NodePath { get; set; } = "";
    
    [JsonPropertyName("levels")]
    public int Levels { get; set; } = -1;
    
    [JsonPropertyName("includeSiblings")]
    public bool IncludeSiblings { get; set; } = false;
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

// ========== 响应模型 ==========

public class ApiResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    
    [JsonPropertyName("data")]
    public object? Data { get; set; }
    
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public class NodeInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";
    
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";
    
    [JsonPropertyName("children")]
    public List<string> Children { get; set; } = new();
    
    [JsonPropertyName("properties")]
    public Dictionary<string, object?> Properties { get; set; } = new();
    
    [JsonPropertyName("methods")]
    public List<string> Methods { get; set; } = new();
    
    [JsonPropertyName("signals")]
    public List<string> Signals { get; set; } = new();
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

/// <summary>
/// 待处理的请求 (用于线程间通信)
/// </summary>
internal class PendingRequest
{
    public string Path { get; set; } = "";
    public string Body { get; set; } = "";
    public TaskCompletionSource<ApiResponse> CompletionSource { get; set; } = null!;
}
