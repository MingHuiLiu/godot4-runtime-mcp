using System.Text.Json.Nodes;
using Godot;
using System.Linq;
using System.Security.Cryptography;

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
    public long MaxTotalBytes { get; init; } = 64L * 1024 * 1024;
    public long EncodedBytes { get; internal set; }
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
            "Vector2I" => EncodeVector2I(value.AsVector2I()),
            "Vector3I" => EncodeVector3I(value.AsVector3I()),
            "Vector4I" => EncodeVector4I(value.AsVector4I()),
            "Vector3" => EncodeVector3(value.AsVector3()),
            "Color" => EncodeColor(value.AsColor()),
            "Rect2" => EncodeRect2(value.AsRect2()),
            "Rect2I" => EncodeRect2I(value.AsRect2I()),
            "Plane" => EncodePlane(value.AsPlane()),
            "Transform2D" => EncodeTransform2D(value.AsTransform2D()),
            "Transform3D" => EncodeTransform3D(value.AsTransform3D()),
            "Quaternion" => EncodeQuaternion(value.AsQuaternion()),
            "Basis" => EncodeBasis(value.AsBasis()),
            "Projection" => EncodeProjection(value.AsProjection()),
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
            "PackedVector4Array" => Pack("vector4", value.AsVector4Array().Select(x => EncodeVector4(x).Data)),
            "Object" => EncodeObject(value.AsGodotObject(), context, nodeId, propertyName, depth),
            _ => null
        };

        if (encoded is null)
        {
            context.Issues.Add(new SnapshotIssue("unsupported_variant", nodeId,
                propertyName, "Unsupported Variant type: " + value.VariantType));
            return null;
        }
        if (HasNonFiniteNumber(encoded.Data))
        {
            context.Issues.Add(new SnapshotIssue("non_finite_variant", nodeId,
                propertyName, "Cannot round-trip NaN/Infinity via JSON"));
            return null;
        }
        long bytes = encoded.Data is null ? (encoded.RefId?.Length ?? 0) :
            System.Text.Encoding.UTF8.GetByteCount(encoded.Data.ToJsonString());
        if (bytes > context.MaxFieldBytes || bytes > context.MaxTotalBytes - context.EncodedBytes)
        {
            context.Issues.Add(new SnapshotIssue("capture_limit_exceeded", nodeId,
                propertyName, "Field or cumulative encoded byte budget exceeded"));
            return null;
        }
        context.EncodedBytes += bytes;
        return encoded;
    }

    private static bool HasNonFiniteNumber(JsonNode? value) => value switch
    {
        JsonValue scalar =>
            (scalar.TryGetValue<double>(out double d) && !double.IsFinite(d)) ||
            (scalar.TryGetValue<float>(out float f) && !float.IsFinite(f)),
        JsonArray arr => arr.Any(HasNonFiniteNumber),
        JsonObject obj => obj.Any(pair => HasNonFiniteNumber(pair.Value)),
        _ => false
    };

    private static SnapshotValue EncodeVector2(Vector2 v) =>
        new("vector2", new JsonObject { ["x"] = v.X, ["y"] = v.Y });

    private static SnapshotValue EncodeVector2I(Vector2I v) =>
        new("vector2i", new JsonObject { ["x"] = v.X, ["y"] = v.Y });

    private static SnapshotValue EncodeVector3I(Vector3I v) =>
        new("vector3i", new JsonObject { ["x"] = v.X, ["y"] = v.Y, ["z"] = v.Z });

    private static SnapshotValue EncodeVector4I(Vector4I v) =>
        new("vector4i", new JsonObject { ["x"] = v.X, ["y"] = v.Y, ["z"] = v.Z, ["w"] = v.W });

    private static SnapshotValue EncodeVector3(Vector3 v) =>
        new("vector3", new JsonObject { ["x"] = v.X, ["y"] = v.Y, ["z"] = v.Z });

    private static SnapshotValue EncodeColor(Color v) =>
        new("color", new JsonObject { ["r"] = v.R, ["g"] = v.G, ["b"] = v.B, ["a"] = v.A });

    private static SnapshotValue EncodePlane(Plane v) =>
        new("plane", new JsonObject { ["normal"] = EncodeVector3(v.Normal).Data,
            ["d"] = v.D });

    private static SnapshotValue EncodeRect2I(Rect2I v) =>
        new("rect2i", new JsonObject { ["x"] = v.Position.X, ["y"] = v.Position.Y,
            ["width"] = v.Size.X, ["height"] = v.Size.Y });

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

    private static SnapshotValue EncodeProjection(Projection p) =>
        new("projection", new JsonObject { ["x"] = EncodeVector4(p.X).Data,
            ["y"] = EncodeVector4(p.Y).Data, ["z"] = EncodeVector4(p.Z).Data,
            ["w"] = EncodeVector4(p.W).Data });

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

    private static void CheckExternalResourceProperties(Resource memory, string path,
        Dictionary<string, SnapshotValue> expected, List<SnapshotIssue> issues,
        string nodeId, string property)
    {
        Resource? fresh = null;
        try
        {
            fresh = ResourceLoader.Load<Resource>(path, cacheMode: ResourceLoader.CacheMode.Ignore);
            if (fresh is null || ReferenceEquals(fresh, memory))
            {
                issues.Add(new SnapshotIssue("external_resource_unverifiable", nodeId, property,
                    "Cannot inspect an independent on-disk copy of " + path));
                return;
            }
            var temporary = new SnapshotEncodeContext();
            foreach (var (name, wanted) in expected)
            {
                // Comparing IDs across different object graphs is not meaningful.
                // Reject complex references conservatively instead of guessing.
                if (wanted.Type is "resource_ref" or "node_ref" ||
                    wanted.Data?.ToJsonString().Contains("resource_ref", StringComparison.Ordinal) == true)
                {
                    issues.Add(new SnapshotIssue("external_resource_unverifiable", nodeId,
                        property + "." + name, "Nested external Resource needs explicit restoration"));
                    continue;
                }
                var actual = Encode(fresh.Get(name), temporary, nodeId, property + "." + name);
                if (actual is null || actual.Type != wanted.Type ||
                    !JsonNode.DeepEquals(actual.Data, wanted.Data))
                    issues.Add(new SnapshotIssue("external_resource_mutated", nodeId,
                        property + "." + name, "In-memory Resource differs from " + path));
            }
            if (temporary.Issues.Count != 0)
                issues.Add(new SnapshotIssue("external_resource_unverifiable", nodeId, property,
                    "On-disk copy has unsupported values"));
        }
        catch (Exception ex)
        {
            issues.Add(new SnapshotIssue("external_resource_unverifiable", nodeId, property,
                path + ": " + ex.Message));
        }
        finally
        {
            if (fresh is not null && !ReferenceEquals(fresh, memory))
                fresh.Dispose();
        }
    }

    /// <summary>SHA-256 of the underlying project file, ignoring Godot's ::subresource suffix.</summary>
    public static string? HashResourceDependency(string resourcePath,
        List<SnapshotIssue> issues, string nodeId, string property)
    {
        string path = resourcePath.Split(new[] { "::" }, 2, StringSplitOptions.None)[0];
        if (!path.StartsWith("res://", StringComparison.Ordinal) &&
            !path.StartsWith("user://", StringComparison.Ordinal))
        {
            issues.Add(new SnapshotIssue("resource_dependency_unavailable", nodeId, property,
                "Cannot verify dependency outside res:// and user://: " + resourcePath));
            return null;
        }
        try
        {
            string resolved = ProjectSettings.GlobalizePath(path);
            if (!File.Exists(resolved))
                throw new FileNotFoundException("Resource dependency file missing", resolved);
            using var stream = File.OpenRead(resolved);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            issues.Add(new SnapshotIssue("resource_dependency_unavailable", nodeId, property,
                resourcePath + ": " + ex.Message));
            return null;
        }
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
        string? resourcePath = string.IsNullOrEmpty(resource.ResourcePath)
            ? null : resource.ResourcePath;
        // Re-loaded PackedScene subresources may have local:// IDs or absolute
        // <scene>.tscn::id identifiers. They are inline, not on-disk dependencies.
        if (resourcePath is not null &&
            (resourcePath.StartsWith("local://", StringComparison.Ordinal) ||
             resourcePath.StartsWith("::", StringComparison.Ordinal) ||
             (resourcePath.Contains("::", StringComparison.Ordinal) &&
              !resourcePath.StartsWith("res://", StringComparison.Ordinal) &&
              !resourcePath.StartsWith("user://", StringComparison.Ordinal))))
            resourcePath = null;
        string? dependencyHash = null;
        if (resourcePath is not null)
            dependencyHash = HashResourceDependency(resourcePath,
                context.Issues, nodeId, propertyName);
        var row = new RecordedResource(resourceId, resource.GetClass().ToString(),
            resourcePath,
            dependencyHash, resource.ResourceLocalToScene, properties, new List<SnapshotIssue>(),
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
        // File hashes prove which dependency was present; they do not prove that a
        // loaded Resource's in-memory properties still match its disk contents.
        // Compare serialized scalar fields to a cache-bypassing disk load so a
        // mutated external resource never receives an unjustified exact snapshot.
        if (resourcePath is not null && dependencyHash is not null)
            CheckExternalResourceProperties(resource, resourcePath, properties,
                context.Issues, nodeId, propertyName);
        return new SnapshotValue("resource_ref", null, resourceId);
    }
}
