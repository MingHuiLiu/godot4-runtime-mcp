using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GodotRuntimeMcp;

/// <summary>
/// MCP is delegated to the official C# SDK: protocol negotiation, JSON-RPC framing,
/// notifications, error semantics, stdio, and Streamable HTTP are not implemented here.
/// </summary>
public static class McpHost
{
    private static List<Tool> Tools(ToolService service) =>
        service.CatalogModels.Select(definition => new Tool
        {
            Name = definition.Name,
            Description = definition.Description,
            InputSchema = JsonSerializer.SerializeToElement(
                definition.InputSchema, RuntimeJsonContext.Default.ToolInputSchema)
        }).ToList();

    private static async ValueTask<CallToolResult> Invoke(
        ToolService service, RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken)
    {
        var name = request.Params?.Name ?? "";
        var args = new JsonObject();
        if (request.Params?.Arguments is { } arguments)
            foreach (var (key, value) in arguments)
                args[key] = JsonNode.Parse(value.GetRawText());
        try
        {
            var result = await service.RunAsync(name, args);
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = ToolService.Format(result) }],
                IsError = false
            };
        }
        catch (Exception ex)
        {
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = ex.Message }],
                IsError = true
            };
        }
    }

    private static Microsoft.Extensions.DependencyInjection.IMcpServerBuilder Configure(
        Microsoft.Extensions.DependencyInjection.IServiceCollection services, ToolService service)
    {
        var catalog = Tools(service);
        return services.AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation { Name = "godot-runtime-mcp", Version = HostVersion.Current };
            })
            .WithListToolsHandler((_, _) =>
                ValueTask.FromResult(new ListToolsResult { Tools = catalog }))
            .WithCallToolHandler((request, cancellationToken) =>
                Invoke(service, request, cancellationToken));
    }

    public static async Task RunStdioAsync(ToolService service, CancellationToken stopping = default)
    {
        var builder = Host.CreateApplicationBuilder();
        // MCP stdio is protocol-only. Do not emit logs to stdout.
        builder.Logging.ClearProviders();
        Configure(builder.Services, service).WithStdioServerTransport();
        using var host = builder.Build();
        await host.RunAsync(stopping);
    }

    public static async Task RunHttpAsync(
        ToolService service, int port, string token, CancellationToken stopping = default)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        Configure(builder.Services, service).WithHttpTransport();
        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path != "/mcp")
            {
                context.Response.StatusCode = 404;
                return;
            }

            if (context.Request.Headers.Origin is { Count: > 0 } origins)
            {
                var origin = origins.ToString();
                if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
                    uri.Scheme is not ("http" or "https") ||
                    uri.Host is not ("localhost" or "127.0.0.1"))
                {
                    context.Response.StatusCode = 403;
                    return;
                }
            }

            var expected = Encoding.UTF8.GetBytes("Bearer " + token);
            var actual = Encoding.UTF8.GetBytes(context.Request.Headers.Authorization.ToString());
            if (expected.Length != actual.Length ||
                !CryptographicOperations.FixedTimeEquals(expected, actual))
            {
                context.Response.StatusCode = 401;
                return;
            }
            await next(context);
        });
        app.MapMcp("/mcp");
        await app.RunAsync(stopping);
    }
}
