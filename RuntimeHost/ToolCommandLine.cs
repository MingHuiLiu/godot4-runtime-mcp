using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GodotRuntimeMcp;

/// <summary>Schema-driven, AOT-safe CLI arguments for every MCP tool.</summary>
public static class ToolCommandLine
{
    private static string Kebab(string text)
    {
        var result = new StringBuilder();
        foreach (var c in text)
        {
            if (c == '_') result.Append('-');
            else if (char.IsUpper(c))
            {
                if (result.Length > 0 && result[^1] != '-') result.Append('-');
                result.Append(char.ToLowerInvariant(c));
            }
            else result.Append(c);
        }
        return result.ToString();
    }

    private static string Canonical(string text) => text.Replace('-', '_');

    public static JsonObject? Find(ToolService service, string command) =>
        service.Catalog().OfType<JsonObject>().FirstOrDefault(item =>
            item["name"]?.ToString() == Canonical(command) ||
            Kebab(item["name"]?.ToString() ?? "") == command);

    public static string? Resolve(ToolService service, string command) => Find(service, command)?["name"]?.ToString();

    private static JsonObject Props(JsonObject tool) =>
        tool["inputSchema"]?["properties"] as JsonObject ?? new JsonObject();

    public static string Describe(JsonObject tool)
    {
        var name = tool["name"]!.ToString();
        var props = Props(tool);
        var required = (tool["inputSchema"]?["required"] as JsonArray)?
            .Select(v => v!.ToString()).ToHashSet(StringComparer.Ordinal)
            ?? new HashSet<string>(StringComparer.Ordinal);
        var output = new StringBuilder();
        output.AppendLine($"{Kebab(name)}  ({name})")
            .AppendLine(tool["description"]?.ToString() ?? "")
            .AppendLine().AppendLine($"Usage: godot-runtime-cli {Kebab(name)} [OPTIONS]")
            .AppendLine().AppendLine("Options:");
        foreach (var (key, def) in props)
        {
            var type = def?["type"]?.ToString() ?? "JSON or text";
            var flag = $"--{Kebab(key)}";
            var requiredText = required.Contains(key) ? " (required)" : "";
            output.AppendLine($"  {flag,-27} {type}{requiredText}")
                .AppendLine($"    {def?["description"]?.ToString() ?? ""}");
        }
        output.AppendLine("  --json OBJECT                  Optional JSON object (merged with named flags)")
            .AppendLine("  --help                         Show this tool's options");
        if (props.Count > 0)
        {
            output.AppendLine().Append("Example: godot-runtime-cli ").Append(Kebab(name));
            foreach (var (key, def) in props)
                if (required.Contains(key))
                    output.Append(" --").Append(Kebab(key)).Append(' ').Append(def?["type"]?.ToString() switch
                    { "boolean" => "true", "integer" => "1", "array" => "'[]'", _ => "VALUE" });
            output.AppendLine();
        }
        return output.ToString();
    }

    public static string CatalogText(ToolService service, string? search = null)
    {
        var lines = new StringBuilder("Commands available offline (tool execution may require a running Godot instance):\n");
        var matches = service.Catalog().OfType<JsonObject>()
            .Where(t => search == null ||
                t["name"]!.ToString().Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (t["description"]?.ToString() ?? "").Contains(search, StringComparison.OrdinalIgnoreCase) ||
                Kebab(t["name"]!.ToString()).Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t["name"]!.ToString(), StringComparer.Ordinal).ToList();
        foreach (var tool in matches)
            lines.Append("  ").Append(Kebab(tool["name"]!.ToString()).PadRight(28))
                .AppendLine(tool["description"]?.ToString() ?? "");
        lines.AppendLine($"\n{matches.Count} tool(s). Run 'godot-runtime-cli help TOOL' for typed flags.");
        return lines.ToString();
    }

    private static JsonNode? Value(string raw, string? type, string name)
    {
        try
        {
            switch (type)
            {
                case "string": return JsonValue.Create(raw);
                case "integer":
                    if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
                        return JsonValue.Create(integer);
                    break;
                case "number":
                    if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                        && double.IsFinite(number)) return JsonValue.Create(number);
                    break;
                case "boolean":
                    if (bool.TryParse(raw, out var boolean)) return JsonValue.Create(boolean);
                    break;
                case "array":
                case "object":
                    var node = JsonNode.Parse(raw);
                    if ((type == "array" && node is JsonArray) || (type == "object" && node is JsonObject))
                        return node;
                    break;
                default:
                    // Manifest values without a fixed type (e.g. set_property.value):
                    // JSON literals are accepted; otherwise use unquoted strings.
                    try { return JsonNode.Parse(raw); }
                    catch (JsonException) { return JsonValue.Create(raw); }
            }
        }
        catch (JsonException) { }
        throw new ArgumentException($"Invalid --{Kebab(name)}: expected {type ?? "JSON or text"}, got '{raw}'.");
    }

    public static JsonObject Parse(ToolService service, string command, IReadOnlyList<string> arguments)
    {
        var tool = Find(service, command) ??
            throw new ArgumentException($"Unknown tool '{command}'. Run 'godot-runtime-cli tools --search NAME'.");
        var properties = Props(tool);
        var options = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        JsonObject values = new();
        for (var i = 0; i < arguments.Count; i++)
        {
            var token = arguments[i];
            if (token is "--help" or "-h")
                throw new ArgumentException("Use 'godot-runtime-cli help " + command + "' to view options.");
            if (!token.StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Unexpected positional value '{token}'. Use --parameter value. Run 'help {command}'.");

            var part = token[2..];
            var equals = part.IndexOf('=');
            var name = equals < 0 ? part : part[..equals];
            var raw = equals < 0 ? null : part[(equals + 1)..];
            var negated = name.StartsWith("no-", StringComparison.Ordinal);
            if (negated) name = name[3..];
            var matched = properties.Select(p => p.Key).FirstOrDefault(key =>
                key == name || Kebab(key) == name || Canonical(name) == key);
            var keyName = matched ?? (name is "project" or "instance" or "json" ? name : null);
            if (keyName == null)
                throw new ArgumentException($"Unknown option '--{name}' for {command}. Run 'godot-runtime-cli help {command}'.");
            if (options.ContainsKey(keyName)) throw new ArgumentException($"Duplicate option '--{name}'");
            var kind = keyName switch
            {
                "project" or "instance" => "string",
                "json" => "object",
                _ => properties[keyName]?["type"]?.ToString()
            };
            if (negated && kind != "boolean")
                throw new ArgumentException($"--no-{name} is only valid for boolean flags");
            if (negated) raw = "false";
            else if (raw == null && kind == "boolean" &&
                (i + 1 >= arguments.Count || arguments[i + 1].StartsWith("--", StringComparison.Ordinal)))
                raw = "true";
            else if (raw == null)
            {
                if (++i >= arguments.Count)
                    throw new ArgumentException($"Missing value for --{name}. Run 'help {command}'.");
                raw = arguments[i];
            }
            options.Add(keyName, Value(raw, kind, keyName));
        }
        if (options.Remove("json", out var jsonArgs))
            values = (JsonObject)jsonArgs!;
        foreach (var (name, value) in options)
            values[name] = value?.DeepClone();

        foreach (var (name, _) in values)
            if (!properties.ContainsKey(name))
                throw new ArgumentException($"Unsupported argument '{name}' for {command}. Run 'help {command}'.");
        foreach (var required in tool["inputSchema"]?["required"]?.AsArray() ?? new JsonArray())
            if (required != null && (!values.ContainsKey(required.ToString()) || values[required.ToString()] == null))
                throw new ArgumentException($"Missing required --{Kebab(required.ToString())}. Run 'godot-runtime-cli help {command}'.");

        return values;
    }
}
