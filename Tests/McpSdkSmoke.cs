using ModelContextProtocol.Client;
using System.Text.Json;

namespace GodotRuntimeMcp.Tests;

/// <summary>Real SDK-to-SDK transport test; no private JSON-RPC parser or handcrafted frames.</summary>
internal static class McpSdkSmoke
{
    public static async Task RunAsync(string hostDll, string dotnetPath)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var nativeHost = Environment.GetEnvironmentVariable("GODOT_RUNTIME_NATIVE_HOST");
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "godot-runtime-smoke",
            Command = nativeHost ?? dotnetPath,
            Arguments = nativeHost == null ? [hostDll, "mcp"] : ["mcp"]
        });
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token);
        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        if (tools.Count != 51)
            throw new Exception($"SDK client expected 51 tools, got {tools.Count}");
        var response = await client.CallToolAsync("godot_status", cancellationToken: timeout.Token);
        if (response.IsError == true)
            throw new Exception("SDK client godot_status returned a tool error");
    }
}
