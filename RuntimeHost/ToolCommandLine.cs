using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GodotRuntimeMcp;

/// <summary>Only maps tool-specific Godot Variant/JSON option values; System.CommandLine parses CLI syntax.</summary>
public static class ToolCommandLine
{
    public static string Kebab(string text)
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

    public static ToolDefinition? Find(ToolService service, string command) =>
        service.CatalogModels.FirstOrDefault(tool =>
            tool.Name == command.Replace('-', '_') || Kebab(tool.Name) == command);

    public static string? Resolve(ToolService service, string command) =>
        Find(service, command)?.Name;

    public static string Describe(ToolDefinition tool)
    {
        var output = new StringBuilder();
        output.AppendLine($"{Kebab(tool.Name)}  ({tool.Name})")
            .AppendLine(tool.Description)
            .AppendLine().AppendLine($"Usage: godot-runtime-cli {Kebab(tool.Name)} [OPTIONS]")
            .AppendLine().AppendLine("Options:");
        var required = tool.InputSchema.Required?.ToHashSet(StringComparer.Ordinal) ?? [];
        foreach (var (key, def) in tool.InputSchema.Properties)
        {
            var kind = def.Type ?? "JSON or text";
            var flag = "--" + Kebab(key);
            output.AppendLine($"  {flag,-27} {kind}{(required.Contains(key) ? " (required)" : "")}")
                .AppendLine($"    {def.Description ?? ""}");
        }
        output.AppendLine("  --json OBJECT                  Optional JSON object (merged with named flags)")
            .AppendLine("  --help                         Show this tool's options");
        if (required.Count > 0)
        {
            output.AppendLine().Append($"Example: godot-runtime-cli {Kebab(tool.Name)}");
            foreach (var key in required)
                output.Append(" --").Append(Kebab(key)).Append(' ')
                    .Append(tool.InputSchema.Properties[key].Type switch
                    {
                        "boolean" => "true", "integer" => "1", "array" => "'[]'", _ => "VALUE"
                    });
            output.AppendLine();
        }
        return output.ToString();
    }

    public static string CatalogText(ToolService service, string? search = null)
    {
        var output = new StringBuilder("Commands available offline (tool execution may require running Godot):\n");
        var matches = service.CatalogModels
            .Where(t => search == null ||
                t.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                t.Description.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                Kebab(t.Name).Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();
        foreach (var tool in matches)
            output.Append("  ").Append(Kebab(tool.Name).PadRight(28))
                .AppendLine(tool.Description);
        output.AppendLine($"\n{matches.Count} tool(s). Run 'godot-runtime-cli help TOOL' for options.");
        return output.ToString();
    }

    private static JsonNode? ParseValue(string raw, string? type, string name)
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
                    // Godot Variant cannot have a fixed schema; parse valid JSON literals,
                    // otherwise preserve a bare argument as an ordinary string.
                    try { return JsonNode.Parse(raw); }
                    catch (JsonException) { return JsonValue.Create(raw); }
            }
        }
        catch (JsonException) { }
        throw new ArgumentException($"Invalid --{Kebab(name)}: expected {type ?? "JSON or text"}");
    }

    public static JsonObject Parse(ToolService service, string command, IReadOnlyList<string> arguments)
    {
        var tool = Find(service, command) ??
            throw new ArgumentException($"Unknown tool '{command}'. Run 'godot-runtime-cli tools --search NAME'.");
        var options = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        for (var i = 0; i < arguments.Count; i++)
        {
            var token = arguments[i];
            if (!token.StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Unexpected '{token}'. Expected --parameter value.");
            var part = token[2..];
            var equals = part.IndexOf('=');
            var name = equals < 0 ? part : part[..equals];
            var raw = equals < 0 ? null : part[(equals + 1)..];
            var negated = name.StartsWith("no-", StringComparison.Ordinal);
            if (negated) name = name[3..];
            var key = tool.InputSchema.Properties.Keys.FirstOrDefault(k =>
                k == name || Kebab(k) == name);
            if (name == "json" && !negated) key = "json";
            if (key is null)
                throw new ArgumentException($"Unknown --{name} for {command}. Run 'help {command}'.");
            if (options.ContainsKey(key))
                throw new ArgumentException($"Duplicate --{name}");
            var kind = key == "json" ? "object" : tool.InputSchema.Properties[key].Type;
            if (negated && kind != "boolean")
                throw new ArgumentException($"--no-{name} requires a boolean option");
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
            options.Add(key, ParseValue(raw, kind, key));
        }

        JsonObject values = options.Remove("json", out var rawJson)
            ? (JsonObject)rawJson! : new JsonObject();
        foreach (var (name, value) in options)
            values[name] = value?.DeepClone();

        foreach (var key in values.Select(kv => kv.Key))
            if (!tool.InputSchema.Properties.ContainsKey(key))
                throw new ArgumentException($"Unsupported argument '{key}' for {command}");
        foreach (var key in tool.InputSchema.Required ?? [])
            if (!values.ContainsKey(key) || values[key] is null)
                throw new ArgumentException($"Missing required --{Kebab(key)}");
        return values;
    }
}
