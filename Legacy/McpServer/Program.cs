using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using McpServer.Services;
using Refit;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = Host.CreateApplicationBuilder(args);

// 配置日志输出到 stderr,避免干扰 MCP 的 stdio 通信
builder.Logging.AddConsole(consoleLogOptions =>
{
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});

// 配置强类型 JSON 序列化选项 (Refit)
var refitSettings = new RefitSettings
{
    ContentSerializer = new SystemTextJsonContentSerializer(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    })
};

// 注册强类型 Refit 客户端
builder.Services
    .AddRefitClient<IGodotApi>(refitSettings)
    .ConfigureHttpClient(c => 
    {
        c.BaseAddress = new Uri("http://127.0.0.1:7777/");
        c.Timeout = TimeSpan.FromSeconds(10);
    });

// 注册强类型 Godot 客户端服务
builder.Services.AddSingleton<GodotClient>();

// 配置 MCP 服务器 (使用强类型工具)
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()  // 使用 stdio 传输
    .WithToolsFromAssembly();     // 自动扫描并注册带有 [McpServerTool] 的工具

await builder.Build().RunAsync();
