using ModelContextProtocol.Client;
using System.Text.Json;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

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
        if (tools.Count != 56)
            throw new Exception($"SDK client expected 56 tools, got {tools.Count}");
        var response = await client.CallToolAsync("godot_status", cancellationToken: timeout.Token);
        if (response.IsError == true)
            throw new Exception("SDK client godot_status returned a tool error");
    }

    public static async Task RunHttpAsync(string hostDll, string dotnetPath)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var nativeHost = Environment.GetEnvironmentVariable("GODOT_RUNTIME_NATIVE_HOST");
        const string token = "godot-runtime-mcp-sdk-integration-secret";
        var executable = nativeHost ?? dotnetPath;
        var arguments = nativeHost == null
            ? new[] { hostDll, "serve", "--port", port.ToString(), "--token", token }
            : new[] { "serve", "--port", port.ToString(), "--token", token };
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var server = Process.Start(start) ??
            throw new InvalidOperationException("Unable to start MCP HTTP server");
        try
        {
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false });
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var endpoint = new Uri($"http://127.0.0.1:{port}/mcp");
            var ready = false;
            for (var i = 0; i < 100 && !timeout.IsCancellationRequested; i++)
            {
                if (server.HasExited)
                    throw new Exception("MCP HTTP host exited early: " + await server.StandardError.ReadToEndAsync());
                try
                {
                    using var unauthorized = await client.GetAsync(endpoint, timeout.Token);
                    ready = unauthorized.StatusCode == HttpStatusCode.Unauthorized;
                    if (ready) break;
                }
                catch (HttpRequestException) { }
                await Task.Delay(100, timeout.Token);
            }
            if (!ready) throw new TimeoutException("Kestrel MCP HTTP endpoint did not start");
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Name = "godot-runtime-http-test",
                Endpoint = endpoint,
                AdditionalHeaders = new Dictionary<string, string>
                {
                    ["Authorization"] = "Bearer " + token
                }
            });
            await using var mcp = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token);
            var tools = await mcp.ListToolsAsync(cancellationToken: timeout.Token);
            if (tools.Count != 56)
                throw new Exception($"HTTP client expected 56 tools, got {tools.Count}");
            var result = await mcp.CallToolAsync("godot_status", cancellationToken: timeout.Token);
            if (result.IsError == true)
                throw new Exception("MCP HTTP godot_status failed");
        }
        finally
        {
            if (!server.HasExited) server.Kill(entireProcessTree: true);
            await server.WaitForExitAsync();
        }
    }
}
