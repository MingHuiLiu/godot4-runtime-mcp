using System.Text.Json.Nodes;
using Godot;
using System.Linq;

namespace GodotRuntimeMcp.Recording;

/// <summary>Per-checkpoint object IDs and bounded typed Variant encoding.</summary>
public sealed class SnapshotEncodeContext
{
    public Dictionary<ulong, string> ObjectIds { get; } = new();
    public List<RecordedResource> Resources { get; } = new();
    public List<SnapshotIssue> Issues { get; } = new();
    public int MaxDepth { get; init; } = 32;
    public int MaxResources { get; init; } = 4096;
    public int MaxFieldBytes { get; init; } = 256 * 1024;
}

public static class VariantSnapshotCodec
{
    public static SnapshotValue? Encode(
        Variant value, SnapshotEncodeContext context,
        string nodeId, string propertyName, int depth = 0)
    {
        if (depth > context.MaxDepth)
        {
            context.Issues.Add(new SnapshotIssue("capture_limit_exceeded", nodeId,
                propertyName, "Variant depth exceeds the configured maximum"));
            return null;
        }

        SnapshotValue? encoded = value.VariantType.ToString() switch
        {
            "Nil" => new("null"),
            "Bool" => new("bool", JsonValue.Create(value.AsBool())),
            "Int" => new("int64", JsonValue.Create(value.AsInt64())),
            "Float" => new("float64", JsonValue.Create(value.AsDouble())),
            "String" => new("string", JsonValue.Create(value.AsString())),
            "StringName" => new("string_name", JsonValue.Create(value.AsStringName().ToString())),
            "NodePath" => new("node_path", JsonValue.Create(value.AsNodePath().ToString())),
            "Vector2" => EncodeVector2(value.AsVector2()),
            "Vector3" => EncodeVector3(value.AsVector3()),
            "Color" => EncodeColor(value.AsColor()),
            "Rect2" => EncodeRect2(value.AsRect2()),
            "Transform2D" => EncodeTransform2D(value.AsTransform2D()),
            "Transform3D" => EncodeTransform3D(value.AsTransform3D()),
            "Quaternion" => EncodeQuaternion(value.AsQuaternion()),
            "Basis" => EncodeBasis(value.AsBasis()),
            "Vector4" => EncodeVector4(value.AsVector4()),
            "Aabb" => EncodeAabb(value.AsAabb()),
            "Array" => EncodeArray(value.AsGodotArray(), context, nodeId, propertyName, depth),
            "Dictionary" => EncodeDictionary(value.AsGodotDictionary(), context, nodeId, propertyName, depth),
            "PackedByteArray" => Pack("byte", value.AsByteArray().Select(x => (JsonNode?)JsonValue.Create(x))),
            "PackedInt32Array" => Pack("int32", value.AsInt32Array().Select(x => (JsonNode?)JsonValue.Create(x))),
            "PackedInt64Array" => Pack("int64", value.AsInt64Array().Select(x => (JsonNode?)JsonValue.Create(x))),
            "PackedFloat32Array" => Pack("float32", value.AsFloat32Array().Select(x => (JsonNode?)JsonValue.Create(x))),
            "PackedFloat64Array" => Pack("float64", value.AsFloat64Array().Select(x => (JsonNode?)JsonValue.Create(x))),
            "PackedStringArray" => Pack("string", value.AsStringArray().Select(x => (JsonNode?)JsonValue.Create(x))),
            "PackedVector2Array" => Pack("vector2", value.AsVector2Array().Select(x => EncodeVector2(x).Data)),
            "PackedVector3Array" => Pack("vector3", value.AsVector3Array().Select(x => EncodeVector3(x).Data)),
            "PackedColorArray" => Pack("color", value.AsColorArray().Select(x => EncodeColor(x).Data)),
            "Object" => EncodeObject(value.AsGodotObject(), context, nodeId, propertyName, depth),
            _ => null
        };

        if (encoded is null)
        {
            context.Issues.Add(new SnapshotIssue("unsupported_variant", nodeId,
                propertyName, "Unsupported Variant type: " + value.VariantType));
            return null;
        }
        if (encoded.Data is { } data && System.Text.Encoding.UTF8.GetByteCount(data.ToJsonString()) > context.MaxFieldBytes)
        {
            context.Issues.Add(new SnapshotIssue("capture_limit_exceeded", nodeId,
                propertyName, "Field exceeds configured byte limit"));
            return null;
        }
        return encoded;
    }

    private static SnapshotValue EncodeVector2(Vector2 v) =>
        new("vector2", new JsonObject { ["x"] = v.X, ["y"] = v.Y });

    private static SnapshotValue EncodeVector3(Vector3 v) =>
        new("vector3", new JsonObject { ["x"] = v.X, ["y"] = v.Y, ["z"] = v.Z });

    private static SnapshotValue EncodeColor(Color v) =>
        new("color", new JsonObject { ["r"] = v.R, ["g"] = v.G, ["b"] = v.B, ["a"] = v.A });

    private static SnapshotValue EncodeRect2(Rect2 v) =>
        new("rect2", new JsonObject {
            ["x"] = v.Position.X, ["y"] = v.Position.Y,
            ["width"] = v.Size.X, ["height"] = v.Size.Y
        });

    private static SnapshotValue EncodeTransform2D(Transform2D v) =>
        new("transform2d", new JsonObject {
            ["x"] = EncodeVector2(v.X).Data,
            ["y"] = EncodeVector2(v.Y).Data,
            ["origin"] = EncodeVector2(v.Origin).Data
        });

    private static SnapshotValue EncodeTransform3D(Transform3D v) =>
        new("transform3d", new JsonObject {
            ["basis"] = EncodeBasis(v.Basis).Data,
            ["origin"] = EncodeVector3(v.Origin).Data
        });

    private static SnapshotValue EncodeBasis(Basis v) =>
        new("basis", new JsonObject {
            ["x"] = EncodeVector3(v.X).Data,
            ["y"] = EncodeVector3(v.Y).Data,
            ["z"] = EncodeVector3(v.Z).Data
        });

    private static SnapshotValue EncodeQuaternion(Quaternion v) =>
        new("quaternion", new JsonObject {
            ["x"] = v.X, ["y"] = v.Y, ["z"] = v.Z, ["w"] = v.W
        });

    private static SnapshotValue EncodeVector4(Vector4 v) =>
        new("vector4", new JsonObject {
            ["x"] = v.X, ["y"] = v.Y, ["z"] = v.Z, ["w"] = v.W
        });

    private static SnapshotValue EncodeAabb(Aabb v) =>
        new("aabb", new JsonObject {
            ["position"] = EncodeVector3(v.Position).Data,
            ["size"] = EncodeVector3(v.Size).Data
        });

    private static SnapshotValue Pack(string elementType, IEnumerable<JsonNode?> values)
    {
        var items = new JsonArray();
        foreach (var value in values) items.Add(value);
        return new SnapshotValue("packed_array", new JsonObject {
            ["elementType"] = elementType, ["items"] = items
        });
    }

    private static SnapshotValue EncodeArray(Godot.Collections.Array values,
        SnapshotEncodeContext context, string nodeId, string propertyName, int depth)
    {
        var result = new JsonArray();
        for (int index = 0; index < values.Count; index++)
        {
            var encoded = Encode(values[index], context, nodeId,
                propertyName + "[" + index + "]", depth + 1);
            result.Add(encoded is null ? null :
                System.Text.Json.JsonSerializer.SerializeToNode(encoded,
                    SnapshotJsonContext.Default.SnapshotValue));
        }
        return new SnapshotValue("array", result);
    }

    private static SnapshotValue EncodeDictionary(Godot.Collections.Dictionary values,
        SnapshotEncodeContext context, string nodeId, string propertyName, int depth)
    {
        var entries = new JsonArray();
        foreach (Variant key in values.Keys)
        {
            var encodedKey = Encode(key, context, nodeId, propertyName + ".key", depth + 1);
            var encodedValue = Encode(values[key], context, nodeId,
                propertyName + ".value", depth + 1);
            if (encodedKey is null || encodedValue is null) continue;
            entries.Add(new JsonArray {
                System.Text.Json.JsonSerializer.SerializeToNode(encodedKey,
                    SnapshotJsonContext.Default.SnapshotValue),
                System.Text.Json.JsonSerializer.SerializeToNode(encodedValue,
                    SnapshotJsonContext.Default.SnapshotValue)
            });
        }
        return new SnapshotValue("dict", entries);
    }

    private static SnapshotValue? EncodeObject(
        GodotObject? obj, SnapshotEncodeContext context,
        string nodeId, string propertyName, int depth)
    {
        if (obj is null) return new SnapshotValue("null");
        if (obj is Node node)
        {
            if (context.ObjectIds.TryGetValue(node.GetInstanceId(), out var existing))
                return new SnapshotValue("node_ref", null, existing);
            context.Issues.Add(new SnapshotIssue("unresolved_node_ref",
                nodeId, propertyName, "Node outside the captured subtree"));
            return null;
        }
        if (obj is not Resource resource) return null;
        ulong instanceId = resource.GetInstanceId();
        if (context.ObjectIds.TryGetValue(instanceId, out string? resourceId))
            return new SnapshotValue("resource_ref", null, resourceId);
        if (context.Resources.Count >= context.MaxResources)
        {
            context.Issues.Add(new SnapshotIssue("capture_limit_exceeded", nodeId,
                propertyName, "Too many distinct resources"));
            return null;
        }

        resourceId = "r_" + (context.Resources.Count + 1).ToString("D6");
        context.ObjectIds[instanceId] = resourceId;
        var properties = new Dictionary<string, SnapshotValue>();
        var row = new RecordedResource(resourceId, resource.GetClass().ToString(),
            string.IsNullOrEmpty(resource.ResourcePath) ? null : resource.ResourcePath,
            null, resource.ResourceLocalToScene, properties, new List<SnapshotIssue>(),
            resource.GetScript().As<Script>()?.ResourcePath);
        context.Resources.Add(row);
        foreach (var item in resource.GetPropertyList())
        {
            string name = item["name"].AsString();
            long usage = item["usage"].AsInt64();
            if ((usage & (long)PropertyUsageFlags.Storage) == 0 ||
                name is "resource_path" or "resource_local_to_scene" || name.StartsWith("script"))
                continue;
            SnapshotValue? encoded = Encode(resource.Get(name), context, nodeId,
                $"{propertyName}.{name}", depth + 1);
            if (encoded is not null) properties[name] = encoded;
        }
        return new SnapshotValue("resource_ref", null, resourceId);
    }
}
