using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;

namespace GodotRuntimeMcp.Recording;

public sealed record SceneBuildResult(Node Root, IReadOnlyList<SnapshotIssue> Issues);

/// <summary>Builds a distinct, serializable scene tree from a versioned checkpoint.</summary>
public static class SnapshotSceneBuilder
{
    public static SceneBuildResult Build(SceneCheckpoint snapshot, bool strict)
    {
        SnapshotSchema.Validate(snapshot);
        var issues = new List<SnapshotIssue>(snapshot.Issues);
        if (strict && issues.Count > 0)
            throw new InvalidDataException("snapshot_incomplete: checkpoint contains unresolved issues");
        var nodes = new Dictionary<string, Node>();
        var resources = new Dictionary<string, Resource>();
        Node? root = null;
        try
        {
            foreach (var item in snapshot.Resources)
            {
                try
                {
                    Resource? resource = null;
                    if (!string.IsNullOrEmpty(item.ScriptPath))
                        resource = GD.Load<CSharpScript>(item.ScriptPath)?.New().AsGodotObject() as Resource;
                    else if (!string.IsNullOrEmpty(item.ResourcePath))
                        resource = GD.Load<Resource>(item.ResourcePath);
                    resource ??= ClassDB.Instantiate(item.ClassName).AsGodotObject() as Resource;
                    if (resource is null) throw new InvalidOperationException("Cannot construct resource " + item.ClassName);
                    resources.Add(item.ResourceId, resource);
                }
                catch (Exception ex)
                {
                    issues.Add(new SnapshotIssue("resource_unavailable", null, item.ResourceId, ex.Message));
                }
            }

            foreach (var item in snapshot.Nodes)
            {
                if (item.IsInternal)
                {
                    issues.Add(new SnapshotIssue("non_exportable", item.NodeId, null,
                        "Internal node cannot be reconstructed as a normal scene node"));
                    continue;
                }
                try
                {
                    Node? node = null;
                    if (!string.IsNullOrEmpty(item.ScriptPath))
                        node = GD.Load<CSharpScript>(item.ScriptPath)?.New().AsGodotObject() as Node;
                    node ??= ClassDB.Instantiate(item.NativeClass).AsGodotObject() as Node;
                    if (node is null) throw new InvalidOperationException("Cannot construct node " + item.NativeClass);
                    node.Name = item.Name;
                    node.UniqueNameInOwner = item.UniqueNameInOwner;
                    nodes.Add(item.NodeId, node);
                }
                catch (Exception ex)
                {
                    issues.Add(new SnapshotIssue("node_unavailable", item.NodeId, null, ex.Message));
                }
            }

            if (!nodes.TryGetValue(snapshot.RootNodeId, out root))
                throw new InvalidDataException("snapshot_incomplete: root could not be reconstructed");
            foreach (var item in snapshot.Nodes)
            {
                if (item.NodeId == snapshot.RootNodeId || !nodes.TryGetValue(item.NodeId, out var node))
                    continue;
                if (item.ParentId is null || !nodes.TryGetValue(item.ParentId, out var parent))
                {
                    issues.Add(new SnapshotIssue("orphan_node", item.NodeId, null, "Parent was not reconstructed"));
                    continue;
                }
                parent.AddChild(node);
            }

            foreach (var item in snapshot.Resources)
            {
                if (!resources.TryGetValue(item.ResourceId, out var resource)) continue;
                resource.ResourceLocalToScene = item.LocalToScene;
                foreach (var (name, value) in item.Properties)
                    SetProperty(resource, name, value, item.ResourceId, nodes, resources, issues);
            }
            foreach (var item in snapshot.Nodes)
            {
                if (!nodes.TryGetValue(item.NodeId, out var node)) continue;
                foreach (var (name, value) in item.Properties)
                    SetProperty(node, name, value, item.NodeId, nodes, resources, issues);
                foreach (string group in item.Groups) node.AddToGroup(group, persistent: true);
            }
            // Packing follows the new ownership graph, not the live game Owner relationships.
            foreach (var item in snapshot.Nodes)
                if (item.NodeId != snapshot.RootNodeId && nodes.TryGetValue(item.NodeId, out var node) &&
                    node.IsAncestorOf(root) == false && node.GetParent() is not null)
                    node.Owner = root;

            if (strict && issues.Count > 0)
                throw new InvalidDataException("snapshot_incomplete: unsupported property or node: " +
                    string.Join(",", issues.Select(x => x.Code + ":" + x.Property)));
            return new SceneBuildResult(root, issues);
        }
        catch
        {
            if (root is not null && GodotObject.IsInstanceValid(root)) root.Free();
            foreach (var node in nodes.Values)
                if (GodotObject.IsInstanceValid(node)) node.Free();
            throw;
        }
    }

    private static void SetProperty(GodotObject obj, string name, SnapshotValue source,
        string ownerId, Dictionary<string, Node> nodes, Dictionary<string, Resource> resources,
        List<SnapshotIssue> issues)
    {
        try
        {
            var decoded = Decode(source, nodes, resources);
            obj.Set(name, decoded);
        }
        catch (Exception e)
        {
            issues.Add(new SnapshotIssue("property_restore_failed", ownerId, name, e.Message));
        }
    }

    private static Variant Decode(SnapshotValue source, Dictionary<string, Node> nodes,
        Dictionary<string, Resource> resources)
    {
        var d = source.Data;
        double N(string key) => ReadNumber(d?[key]);
        float F(string key) => (float)N(key);
        return source.Type switch
        {
            "null" => default,
            "bool" => d?.GetValue<bool>() ?? false,
            "int64" => d?.GetValue<long>() ?? 0,
            "float64" => ReadNumber(d),
            "string" => d?.GetValue<string>() ?? "",
            "string_name" => new StringName(d?.GetValue<string>() ?? ""),
            "node_path" => new NodePath(d?.GetValue<string>() ?? ""),
            "vector2" => new Vector2(F("x"), F("y")),
            "vector3" => new Vector3(F("x"), F("y"), F("z")),
            "vector4" => new Vector4(F("x"), F("y"), F("z"), F("w")),
            "rect2" => new Rect2(F("x"), F("y"), F("width"), F("height")),
            "color" => new Color(F("r"), F("g"), F("b"), F("a")),
            "quaternion" => new Quaternion(F("x"), F("y"), F("z"), F("w")),
            "basis" => new Basis(ReadV3(d?["x"]), ReadV3(d?["y"]), ReadV3(d?["z"])),
            "transform2d" => new Transform2D(ReadV2(d?["x"]), ReadV2(d?["y"]), ReadV2(d?["origin"])),
            "transform3d" => new Transform3D(
                new Basis(ReadV3(d?["basis"]?["x"]), ReadV3(d?["basis"]?["y"]), ReadV3(d?["basis"]?["z"])),
                ReadV3(d?["origin"])),
            "aabb" => new Aabb(ReadV3(d?["position"]), ReadV3(d?["size"])),
            "node_ref" => nodes.TryGetValue(source.RefId ?? "", out var node)
                ? Variant.From(node) : throw new InvalidDataException("unresolved_node_ref"),
            "resource_ref" => resources.TryGetValue(source.RefId ?? "", out var resource)
                ? Variant.From(resource) : throw new InvalidDataException("unresolved_resource_ref"),
            "array" => DecodeArray(d as JsonArray, nodes, resources),
            "dict" => DecodeDictionary(d as JsonArray, nodes, resources),
            "packed_array" => DecodePacked(d),
            _ => throw new NotSupportedException("unsupported_variant: " + source.Type)
        };
    }

    private static double ReadNumber(JsonNode? value) => value is null ? 0 :
        double.Parse(value.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture);

    private static Vector2 ReadV2(JsonNode? obj) =>
        new((float)ReadNumber(obj?["x"]), (float)ReadNumber(obj?["y"]));
    private static Vector3 ReadV3(JsonNode? obj) =>
        new((float)ReadNumber(obj?["x"]), (float)ReadNumber(obj?["y"]),
            (float)ReadNumber(obj?["z"]));

    private static SnapshotValue ReadValue(JsonNode? json) =>
        json?.Deserialize(SnapshotJsonContext.Default.SnapshotValue)
            ?? throw new InvalidDataException("Unsupported null in snapshot container");

    private static Variant DecodeArray(JsonArray? arr,
        Dictionary<string, Node> nodes, Dictionary<string, Resource> resources)
    {
        var target = new Godot.Collections.Array();
        if (arr is not null)
            foreach (var item in arr)
                target.Add(item is null ? default : Decode(ReadValue(item), nodes, resources));
        return Variant.From(target);
    }

    private static Variant DecodeDictionary(JsonArray? arr,
        Dictionary<string, Node> nodes, Dictionary<string, Resource> resources)
    {
        var target = new Godot.Collections.Dictionary();
        if (arr is not null)
            foreach (var pair in arr.OfType<JsonArray>())
                target[Decode(ReadValue(pair[0]), nodes, resources)] =
                    Decode(ReadValue(pair[1]), nodes, resources);
        return Variant.From(target);
    }

    private static Variant DecodePacked(JsonNode? node)
    {
        string elementType = node?["elementType"]?.GetValue<string>() ?? "";
        var items = node?["items"]?.AsArray() ?? throw new InvalidDataException("packed_array missing items");
        return elementType switch
        {
            "byte" => Variant.From(items.Select(v => (byte)(v?.GetValue<int>() ?? 0)).ToArray()),
            "int32" => Variant.From(items.Select(v => v?.GetValue<int>() ?? 0).ToArray()),
            "int64" => Variant.From(items.Select(v => v?.GetValue<long>() ?? 0).ToArray()),
            "float32" => Variant.From(items.Select(v => (float)(ReadNumber(v))).ToArray()),
            "float64" => Variant.From(items.Select(v => ReadNumber(v)).ToArray()),
            "string" => Variant.From(items.Select(v => v?.GetValue<string>() ?? "").ToArray()),
            "vector2" => Variant.From(items.Select(ReadV2).ToArray()),
            "vector3" => Variant.From(items.Select(ReadV3).ToArray()),
            "color" => Variant.From(items.Select(v => new Color(
                (float)ReadNumber(v?["r"]),
                (float)ReadNumber(v?["g"]),
                (float)ReadNumber(v?["b"]),
                (float)(v?["a"] is null ? 1 : ReadNumber(v?["a"])))).ToArray()),
            _ => throw new NotSupportedException("unsupported_packed_array: " + elementType)
        };
    }
}
