using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Godot-side, per-process runtime transport. This is deliberately NOT an MCP server:
/// stdio/HTTP MCP and CLI are independent clients of this authenticated local API.
/// Only Pump() executes Godot operations, on the main thread.
/// </summary>
internal sealed class GodotRuntimeTransport : IDisposable
{
    private const int MaxBodyBytes = 1024 * 1024;
    private const int MaxQueue = 128;
    private const int MaxPerFrame = 16;
    private readonly Func<string, Dictionary<string, object>, ApiResponse> _execute;
    private readonly Func<object> _toolManifest;
    private readonly object _lock = new();
    private readonly Queue<WorkItem> _pending = new();
    private readonly string _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    private readonly int _pid = Environment.ProcessId;
    private readonly string _projectPath;
    private readonly string _registryDirectory;
    private readonly DateTime _startedAt = DateTime.UtcNow;
    private HttpListener? _listener;
    private string? _registryFile;
    private double _lastHeartbeat;
    private bool _disposed;
    private int _port;

    private sealed class WorkItem
    {
        public required string Tool { get; init; }
        public required Dictionary<string, object> Arguments { get; init; }
        public TaskCompletionSource<ApiResponse> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public GodotRuntimeTransport(string projectPath,
        Func<string, Dictionary<string, object>, ApiResponse> execute,
        Func<object> toolManifest)
    {
        _projectPath = Path.GetFullPath(projectPath).TrimEnd(Path.DirectorySeparatorChar);
        _execute = execute;
        _toolManifest = toolManifest;
        _registryDirectory = Environment.GetEnvironmentVariable("GODOT_MCP_REGISTRY_DIR")
            ?? Path.Combine(Path.GetTempPath(), "godot-runtime-mcp");
    }

    public void Start()
    {
        // A fixed port is opt-in for troubleshooting, never the default.
        var configuredPort = Environment.GetEnvironmentVariable("GODOT_MCP_PORT");
        var fixedPort = 0;
        if (!string.IsNullOrWhiteSpace(configuredPort) &&
            (!int.TryParse(configuredPort, out fixedPort) || fixedPort < 1 || fixedPort > 65535))
            throw new InvalidOperationException("GODOT_MCP_PORT must be between 1 and 65535.");

        Exception? lastError = null;
        for (var attempt = 0; attempt < (fixedPort > 0 ? 1 : 25); attempt++)
        {
            var candidate = fixedPort > 0 ? fixedPort : FindFreePort();
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{candidate}/");
            try
            {
                listener.Start();
                _listener = listener;
                _port = candidate;
                break;
            }
            catch (Exception ex)
            {
                lastError = ex;
                listener.Close();
            }
        }
        if (_listener == null)
            throw new InvalidOperationException("Cannot bind a runtime loopback port.", lastError);

        try
        {
            Directory.CreateDirectory(_registryDirectory);
            RestrictRegistryPermissions(_registryDirectory, true);
            _registryFile = Path.Combine(_registryDirectory,
                $"runtime-{_pid}-{_instanceId}.json");
            WriteRegistry();
            _ = Task.Run(AcceptLoop);
            Console.Error.WriteLine($"[GodotMcp] Runtime: http://127.0.0.1:{_port} project={_projectPath} instance={_instanceId}");
        }
        catch
        {
            _listener.Close();
            _listener = null;
            throw;
        }
    }

    private static int FindFreePort()
    {
        // The HttpListener bind below is authoritative. Retry to account for the brief race.
        var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        try { return ((IPEndPoint)socket.LocalEndpoint).Port; }
        finally { socket.Stop(); }
    }

    private static void RestrictRegistryPermissions(string path, bool directory)
    {
        if (OperatingSystem.IsWindows()) return;
        File.SetUnixFileMode(path, directory
            ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            : UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private void WriteRegistry()
    {
        if (_registryFile == null) return;
        var record = new
        {
            protocol = 1,
            instanceId = _instanceId,
            pid = _pid,
            projectPath = _projectPath,
            port = _port,
            token = _token,
            startedAt = _startedAt.ToString("O"),
            heartbeat = DateTime.UtcNow.ToString("O")
        };
        var temp = _registryFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(record), Encoding.UTF8);
            RestrictRegistryPermissions(temp, false);
            File.Move(temp, _registryFile, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public void Pump(double delta)
    {
        if (_disposed) return;
        _lastHeartbeat += delta;
        if (_lastHeartbeat >= 2.0)
        {
            _lastHeartbeat = 0;
            try { WriteRegistry(); }
            catch (Exception ex) { Console.Error.WriteLine($"[GodotMcp] Registry heartbeat failed: {ex.Message}"); }
        }
        for (var i = 0; i < MaxPerFrame; i++)
        {
            WorkItem? item;
            lock (_lock) { item = _pending.Count > 0 ? _pending.Dequeue() : null; }
            if (item == null) break;
            if (item.Completion.Task.IsCompleted) continue;
            try { item.Completion.TrySetResult(_execute(item.Tool, item.Arguments)); }
            catch (Exception ex)
            {
                item.Completion.TrySetResult(new ApiResponse { Success = false, Error = ex.Message });
            }
        }
    }

    private async Task AcceptLoop()
    {
        while (!_disposed && _listener?.IsListening == true)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequest(context));
            }
            catch (HttpListenerException) { if (_disposed) break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex) { Console.Error.WriteLine($"[GodotMcp] Accept: {ex.Message}"); }
        }
    }

    private async Task HandleRequest(HttpListenerContext context)
    {
        var response = context.Response;
        try
        {
            if (context.Request.RemoteEndPoint?.Address.Equals(IPAddress.Loopback) != true)
            {
                await Reply(response, 403, new { error = "Loopback only" });
                return;
            }
            var path = context.Request.Url?.AbsolutePath ?? "";
            if (path == "/health" && context.Request.HttpMethod == "GET")
            {
                await Reply(response, 200, new
                {
                    protocol = 1, status = "running", instanceId = _instanceId,
                    pid = _pid, projectPath = _projectPath, port = _port
                });
                return;
            }
            var authorization = context.Request.Headers["Authorization"] ?? "";
            var expected = "Bearer " + _token;
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(authorization.PadRight(expected.Length)),
                    Encoding.UTF8.GetBytes(expected)) || authorization.Length != expected.Length)
            {
                await Reply(response, 401, new { error = "Unauthorized" });
                return;
            }
            if (path == "/tools" && context.Request.HttpMethod == "GET")
            {
                await Reply(response, 200, new { tools = _toolManifest() });
                return;
            }
            if (path != "/call" || context.Request.HttpMethod != "POST")
            {
                await Reply(response, 404, new { error = "Unknown route" });
                return;
            }
            if (context.Request.ContentLength64 > MaxBodyBytes)
            {
                await Reply(response, 413, new { error = "Request too large" });
                return;
            }
            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            var buffer = new char[4096];
            var body = new StringBuilder();
            while (true)
            {
                var count = await reader.ReadAsync(buffer, 0, buffer.Length);
                if (count == 0) break;
                body.Append(buffer, 0, count);
                if (body.Length > MaxBodyBytes)
                {
                    await Reply(response, 413, new { error = "Request too large" });
                    return;
                }
            }
            using var document = JsonDocument.Parse(body.ToString());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("tool", out var toolElement) ||
                toolElement.ValueKind != JsonValueKind.String)
            {
                await Reply(response, 400, new { error = "Expected tool string" });
                return;
            }
            var tool = toolElement.GetString() ?? "";
            var args = new Dictionary<string, object>();
            if (root.TryGetProperty("arguments", out var argElement))
            {
                if (argElement.ValueKind != JsonValueKind.Object)
                {
                    await Reply(response, 400, new { error = "arguments must be an object" });
                    return;
                }
                args = JsonSerializer.Deserialize<Dictionary<string, object>>(argElement.GetRawText()) ?? args;
            }
            var item = new WorkItem { Tool = tool, Arguments = args };
            lock (_lock)
            {
                if (_disposed)
                {
                    _ = Reply(response, 503, new { error = "Backend stopped" });
                    return;
                }
                if (_pending.Count >= MaxQueue)
                {
                    _ = Reply(response, 429, new { error = "Backend busy" });
                    return;
                }
                _pending.Enqueue(item);
            }
            ApiResponse result;
            try { result = await item.Completion.Task.WaitAsync(TimeSpan.FromSeconds(15)); }
            catch (TimeoutException)
            {
                item.Completion.TrySetCanceled();
                await Reply(response, 504, new { error = "Godot main thread did not respond" });
                return;
            }
            await Reply(response, 200, result);
        }
        catch (JsonException ex) { await SafeReply(response, 400, ex.Message); }
        catch (Exception ex) { await SafeReply(response, 500, ex.Message); }
    }

    private static async Task SafeReply(HttpListenerResponse response, int status, string error)
    {
        try { await Reply(response, status, new { error }); } catch { }
    }

    private static async Task Reply(HttpListenerResponse response, int status, object payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        response.StatusCode = status;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    public void Dispose()
    {
        _disposed = true;
        _listener?.Stop();
        _listener?.Close();
        lock (_lock)
        {
            while (_pending.Count > 0)
                _pending.Dequeue().Completion.TrySetResult(
                    new ApiResponse { Success = false, Error = "Godot is shutting down" });
        }
        if (_registryFile != null)
        {
            try { File.Delete(_registryFile); }
            catch { }
        }
    }
}
