using System.Text.Json.Serialization;

namespace GodotRuntimeMcp;

// Every JSON DTO used by the standalone host is known at compile time for Native AOT.
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(RuntimeEntry))]
internal partial class RuntimeJsonContext : JsonSerializerContext { }
