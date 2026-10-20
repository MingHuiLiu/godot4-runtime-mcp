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
            rows.Add(new RecordedNode(
                nodeId, parentId, sibling, node.Name.ToString(), node.GetClass().ToString(),
                script?.ResourcePath, string.IsNullOrEmpty(node.SceneFilePath) ? null : node.SceneFilePath,
                node.GetPath().ToString(), ownerId, internalNode, node.UniqueNameInOwner,
                node.GetGroups().Select(x => x.ToString()).ToList(),
                new Dictionary<string, SnapshotValue>(),
                new Dictionary<string, SnapshotValue>(), issues));
        }

        return new SceneCheckpoint(SnapshotSchema.CurrentVersion, snapshotId,
            sessionId, projectId, instanceId, ids[root.GetInstanceId()],
            physicsStart, (long)Engine.GetPhysicsFrames(),
            processStart, (long)Engine.GetProcessFrames(),
            rows, new List<RecordedResource>(),
            new Dictionary<string, SnapshotValue>(),
            rows.SelectMany(n => n.Issues).ToList());
    }
}
