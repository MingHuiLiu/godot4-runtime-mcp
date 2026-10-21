using System.Runtime.CompilerServices;
using Godot;
using System.Linq;

namespace GodotRuntimeMcp.Recording;

/// <summary>Reads the whole observable topology without changing live Node ownership.</summary>
public static class SceneCheckpointCapture
{
    private sealed class Identity
    {
        public string Id { get; } = "n_" + Guid.NewGuid().ToString("N");
    }

    private static readonly ConditionalWeakTable<Node, Identity> Ids = new();

    public static SceneCheckpoint Capture(
        Node root, string snapshotId, string sessionId,
        string projectId, string instanceId, bool strict)
    {
        if (!GodotObject.IsInstanceValid(root))
            throw new ArgumentException("scene_unavailable: root node is invalid", nameof(root));

        long physicsStart = (long)Engine.GetPhysicsFrames();
        long processStart = (long)Engine.GetProcessFrames();
        var rows = new List<RecordedNode>();
        var ids = new Dictionary<ulong, string>();
        var all = new List<(Node Node, Node? Parent, int Index, bool Internal)>();
        var todo = new Stack<(Node Node, Node? Parent, int Index, bool Internal)>();
        todo.Push((root, null, 0, false));
        while (todo.Count > 0)
        {
            var item = todo.Pop();
            if (all.Count >= 2000)
                throw new InvalidDataException("capture_limit_exceeded: node count exceeds 2000");
            all.Add(item);
            ids[item.Node.GetInstanceId()] = Ids.GetValue(item.Node, _ => new Identity()).Id;
            var children = item.Node.GetChildren(includeInternal: true);
            var publicChildren = item.Node.GetChildren(includeInternal: false);
            for (int i = children.Count - 1; i >= 0; i--)
                todo.Push((children[i], item.Node, i, !publicChildren.Contains(children[i])));
        }

        var context = new SnapshotEncodeContext();
        foreach (var (nativeId, recordedId) in ids) context.ObjectIds[nativeId] = recordedId;
        foreach (var (node, parent, sibling, internalNode) in all)
        {
            string nodeId = ids[node.GetInstanceId()];
            string? parentId = parent is null ? null : ids[parent.GetInstanceId()];
            string? ownerId = node.Owner is { } owner &&
                ids.TryGetValue(owner.GetInstanceId(), out string? id) ? id : null;
            var issues = new List<SnapshotIssue>();
            if (internalNode)
                issues.Add(new SnapshotIssue("non_exportable", nodeId, null,
                    "Internal engine nodes may not be serializable in an independent scene."));
            var script = node.GetScript().As<Script>();
            var properties = new Dictionary<string, SnapshotValue>();
            foreach (var item in node.GetPropertyList())
            {
                string name = item["name"].AsString();
                long usage = item["usage"].AsInt64();
                if ((usage & (long)PropertyUsageFlags.Storage) == 0 ||
                    name is "script" || node is LineEdit or TextEdit && name is "text" ||
                    name.Contains("password", StringComparison.OrdinalIgnoreCase))
                    continue;
                var encoded = VariantSnapshotCodec.Encode(node.Get(name), context, nodeId, name);
                if (encoded is not null) properties[name] = encoded;
            }
            // Explicit, bounded diagnostic state that is not necessarily STORAGE.
            // These live measurements remain in checkpoint metadata, not .tscn.
            var diagnostic = new Dictionary<string, SnapshotValue>();
            void AddDiagnostic(string key, Variant value)
            {
                using (value)
                {
                    var snapshotValue = VariantSnapshotCodec.Encode(value, context, nodeId, key);
                    if (snapshotValue is not null) diagnostic[key] = snapshotValue;
                }
            }
            if (node is Node2D node2d)
            {
                AddDiagnostic("global_position", Variant.From(node2d.GlobalPosition));
                AddDiagnostic("global_transform", Variant.From(node2d.GlobalTransform));
            }
            if (node is Node3D node3d)
                AddDiagnostic("global_transform", Variant.From(node3d.GlobalTransform));
            if (node is CharacterBody2D body2d)
                AddDiagnostic("velocity", Variant.From(body2d.Velocity));
            if (node is CharacterBody3D body3d)
                AddDiagnostic("velocity", Variant.From(body3d.Velocity));
            if (node is Godot.Timer timer)
                AddDiagnostic("time_left", Variant.From(timer.TimeLeft));
            rows.Add(new RecordedNode(
                nodeId, parentId, sibling, node.Name.ToString(), node.GetClass().ToString(),
                script?.ResourcePath, string.IsNullOrEmpty(node.SceneFilePath) ? null : node.SceneFilePath,
                node.GetPath().ToString(), ownerId, internalNode, node.UniqueNameInOwner,
                node.GetGroups().Select(x => x.ToString()).ToList(),
                properties,
                diagnostic, issues));
        }

        var result = new SceneCheckpoint(SnapshotSchema.CurrentVersion, snapshotId,
            sessionId, projectId, instanceId, ids[root.GetInstanceId()],
            physicsStart, (long)Engine.GetPhysicsFrames(),
            processStart, (long)Engine.GetProcessFrames(),
            rows, context.Resources,
            new Dictionary<string, SnapshotValue>(),
            rows.SelectMany(n => n.Issues).Concat(context.Issues).ToList());
        if (strict && result.Issues.Count > 0)
            throw new InvalidDataException("capture_limit_exceeded: strict snapshot had unsupported or over-limit fields: " +
                string.Join(",", result.Issues.Select(i => i.Code)));
        long encodedBytes = System.Text.Encoding.UTF8.GetByteCount(
            System.Text.Json.JsonSerializer.Serialize(result, SnapshotJsonContext.Default.SceneCheckpoint));
        if (encodedBytes > 64L * 1024 * 1024)
            throw new InvalidDataException("capture_limit_exceeded: checkpoint exceeds 64MiB");
        return result;
    }
}
