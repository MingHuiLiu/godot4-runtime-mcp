using System.Text.Json.Nodes;
using Godot;

namespace GodotRuntimeMcp.Recording;

/// <summary>Checks native .tscn round-trip, including saved properties and resource identity.</summary>
public static class SnapshotSceneVerifier
{
    public static List<SnapshotIssue> Verify(SceneCheckpoint original, Node sceneRoot)
    {
        var issues = new List<SnapshotIssue>();
        var snapshotNodes = original.Nodes.Where(n => !n.IsInternal).ToList();
        var models = snapshotNodes.GroupBy(n => n.ParentId ?? "")
            .ToDictionary(x => x.Key, x => x.OrderBy(n => n.SiblingIndex).ToList());
        var live = new Dictionary<string, Node>();

        void Walk(RecordedNode expected, Node actual)
        {
            if (actual.Name.ToString() != expected.Name ||
                actual.GetClass().ToString() != expected.NativeClass)
                issues.Add(new SnapshotIssue("node_mismatch", expected.NodeId, null,
                    $"Expected {expected.Name}/{expected.NativeClass}; got {actual.Name}/{actual.GetClass()}"));
            live[expected.NodeId] = actual;
            var children = models.GetValueOrDefault(expected.NodeId) ?? [];
            var actualChildren = actual.GetChildren();
            if (children.Count != actualChildren.Count)
                issues.Add(new SnapshotIssue("children_mismatch", expected.NodeId, null,
                    $"Expected {children.Count} children, got {actualChildren.Count}"));
            for (int i = 0; i < Math.Min(children.Count, actualChildren.Count); i++)
                Walk(children[i], actualChildren[i]);
        }

        var root = snapshotNodes.Single(n => n.NodeId == original.RootNodeId);
        Walk(root, sceneRoot);
        var context = new SnapshotEncodeContext();
        foreach (var (id, node) in live)
            context.ObjectIds[node.GetInstanceId()] = id;

        static bool Equal(SnapshotValue a, SnapshotValue? b) =>
            b is not null && a.Type == b.Type && a.RefId == b.RefId &&
            JsonNode.DeepEquals(a.Data, b.Data);

        foreach (var row in snapshotNodes)
        {
            if (!live.TryGetValue(row.NodeId, out var node)) continue;
            foreach (var (name, wanted) in row.Properties)
            {
                try
                {
                    var got = VariantSnapshotCodec.Encode(node.Get(name), context, row.NodeId, name);
                    if (!Equal(wanted, got))
                        issues.Add(new SnapshotIssue("property_mismatch", row.NodeId, name,
                            $"Native scene reload changed {name}"));
                }
                catch (Exception ex)
                {
                    issues.Add(new SnapshotIssue("property_validation_failed", row.NodeId,
                        name, ex.Message));
                }
            }
        }

        issues.AddRange(context.Issues);
        if (original.Resources.Count != context.Resources.Count)
            issues.Add(new SnapshotIssue("resource_count_mismatch", null, null,
                $"Expected {original.Resources.Count}, got {context.Resources.Count}"));
        foreach (var expected in original.Resources)
        {
            var actual = context.Resources.FirstOrDefault(r => r.ResourceId == expected.ResourceId);
            if (actual is null)
                continue; // resource_count_mismatch already captures it
            if (actual.ClassName != expected.ClassName ||
                actual.ScriptPath != expected.ScriptPath)
                issues.Add(new SnapshotIssue("resource_type_mismatch", null, expected.ResourceId,
                    $"Resource class/script changed: {expected.ClassName}"));
            foreach (var (property, value) in expected.Properties)
                if (!actual.Properties.TryGetValue(property, out var current) ||
                    !Equal(value, current))
                    issues.Add(new SnapshotIssue("resource_property_mismatch",
                        null, expected.ResourceId + "." + property,
                        $"Resource property changed during scene serialization"));
        }

        return issues;
    }
}
