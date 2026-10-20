using System.Text.Json.Serialization;

namespace GodotRuntimeMcp;

// Every JSON DTO used by the standalone host is known at compile time for Native AOT.
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(RuntimeEntry))]
[JsonSerializable(typeof(RuntimeInstanceInfo))]
[JsonSerializable(typeof(List<RuntimeInstanceInfo>))]
[JsonSerializable(typeof(List<ProjectInfo>))]
[JsonSerializable(typeof(RuntimeStatus))]
[JsonSerializable(typeof(SetupGuide))]
[JsonSerializable(typeof(InstallResult))]
[JsonSerializable(typeof(PackResult))]
[JsonSerializable(typeof(BackendCallRequest))]
[JsonSerializable(typeof(BackendCallResult))]
[JsonSerializable(typeof(BackendHealth))]
[JsonSerializable(typeof(GithubRelease))]
[JsonSerializable(typeof(List<ToolDefinition>))]
[JsonSerializable(typeof(ToolDefinition))]
[JsonSerializable(typeof(ToolInputSchema))]
public partial class RuntimeJsonContext : JsonSerializerContext { }
