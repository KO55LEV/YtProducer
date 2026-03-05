namespace YtProducer.McpRunner.Services;

using System.Text.Json;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using YtProducer.McpRunner.Models;

/// <summary>
/// JSON-RPC 2.0 client communicating via NDJSON (newline-delimited JSON) over stdio.
/// Handles request/response correlation by ID, timeouts, and concurrent calls.
/// </summary>
public class JsonRpcClient : IAsyncDisposable
{
    private readonly ProcessHost _processHost;
    private readonly ILogger<JsonRpcClient> _logger;
    private readonly ConcurrentDictionary<object, TaskCompletionSource<JsonRpcResponse>> _pendingRequests = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private Task? _readTask;
    private CancellationTokenSource? _readCts;
    private long _nextId = 1;
    private readonly object _idLock = new();
    private readonly SemaphoreSlim _reconnectLock = new(1, 1);

    public string ServiceName => _processHost.ServiceName;
    public bool IsConnected => _processHost.IsHealthy;

    public JsonRpcClient(ProcessHost processHost, ILogger<JsonRpcClient> logger)
    {
        _processHost = processHost;
        _logger = logger;
    }

    /// <summary>
    /// Start reading responses from the server.
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_readTask != null && !_readTask.IsCompleted)
            return;

        _readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _readTask = ReadResponsesAsync(_readCts.Token);
        
        await Task.Delay(50, ct); // Brief startup delay
    }

    /// <summary>
    /// Send a JSON-RPC request and wait for the response.
    /// </summary>
    public async Task<JsonRpcResponse> SendRequestAsync(
        JsonRpcRequest request,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        if (!IsConnected)
            await EnsureConnectedAsync(ct);

        var tcs = new TaskCompletionSource<JsonRpcResponse>();
        _pendingRequests[request.Id] = tcs;

        try
        {
            var json = JsonSerializer.Serialize(request, _jsonOptions);
            var stdin = _processHost.GetStdinWriter();

            if (stdin == null)
                throw new InvalidOperationException("Cannot write to process stdin.");

            _logger.LogDebug("[{ServiceName}] Sending request {Id}: {Method}", ServiceName, request.Id, request.Method);
            await stdin.WriteLineAsync(json);
            await stdin.FlushAsync();

            var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
            var response = await tcs.Task.WaitAsync(effectiveTimeout, ct).ConfigureAwait(false);
            _logger.LogDebug("[{ServiceName}] Received response {Id}", ServiceName, response.Id);
            return response;
        }
        catch (TimeoutException)
        {
            _pendingRequests.TryRemove(request.Id, out _);
            _logger.LogError("[{ServiceName}] Request {Id} timed out", ServiceName, request.Id);
            throw;
        }
        catch (OperationCanceledException)
        {
            _pendingRequests.TryRemove(request.Id, out _);
            _logger.LogError("[{ServiceName}] Request {Id} cancelled", ServiceName, request.Id);
            throw;
        }
        catch (Exception ex)
        {
            _pendingRequests.TryRemove(request.Id, out _);
            _logger.LogError(ex, "[{ServiceName}] Error sending request {Id}", ServiceName, request.Id);
            await TryRecoverConnectionAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// Send tools/list request.
    /// </summary>
    public async Task<ToolDescription[]> ListToolsAsync(TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var requestId = AllocateRequestId();
        var request = new JsonRpcRequest
        {
            Id = requestId,
            Method = "tools/list",
            Params = new { }
        };

        var response = await SendRequestAsync(request, timeout, ct);

        if (response.Error != null)
            throw new InvalidOperationException($"tools/list failed: {response.Error.Message}");

        if (response.Result == null)
            throw new InvalidOperationException("No result in tools/list response");

        try
        {
            var tools = JsonSerializer.Deserialize<ToolDescription[]>(
                response.Result.Value.GetRawText(),
                _jsonOptions) ?? Array.Empty<ToolDescription>();
            return tools;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize tools list");
            throw;
        }
    }

    /// <summary>
    /// Send tools/call request.
    /// </summary>
    public async Task<JsonRpcResponse> CallToolRawAsync(
        string toolName,
        JsonElement? arguments = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var requestId = AllocateRequestId();
        var request = new JsonRpcRequest
        {
            Id = requestId,
            Method = "tools/call",
            Params = new
            {
                name = toolName,
                arguments = arguments?.GetRawText() != null 
                    ? JsonSerializer.Deserialize<object>(arguments.Value.GetRawText(), _jsonOptions)
                    : new { }
            }
        };

        return await SendRequestAsync(request, timeout, ct);
    }

    /// <summary>
    /// Send tools/call request and return only the response result payload.
    /// </summary>
    public async Task<JsonElement?> CallToolAsync(
        string toolName,
        JsonElement? arguments = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var response = await CallToolRawAsync(toolName, arguments, timeout, ct);

        if (response.Error != null)
            throw new InvalidOperationException($"tools/call '{toolName}' failed: {response.Error.Message}");

        return response.Result;
    }

    private object AllocateRequestId()
    {
        lock (_idLock)
        {
            return _nextId++;
        }
    }

    private async Task ReadResponsesAsync(CancellationToken ct)
    {
        try
        {
            var stdout = _processHost.GetStdoutReader();
            if (stdout == null) return;

            string? line;
            while ((line = await stdout.ReadLineAsync(ct)) != null)
            {
                try
                {
                    var response = JsonSerializer.Deserialize<JsonRpcResponse>(line, _jsonOptions);
                    if (response != null && _pendingRequests.TryRemove(response.Id, out var tcs))
                    {
                        tcs.SetResult(response);
                    }
                    else
                    {
                        _logger.LogWarning("[{ServiceName}] Received response for unknown request ID: {Id}", 
                            ServiceName, response?.Id);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "[{ServiceName}] Failed to deserialize response: {Line}", ServiceName, line);
                }
            }

            // Stdout stream closed unexpectedly (likely process exit)
            FailAllPending(new InvalidOperationException($"Connection lost to {ServiceName}: stdout closed"));
            await TryRecoverConnectionAsync(CancellationToken.None);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{ServiceName}] Error reading responses", ServiceName);

            FailAllPending(new InvalidOperationException($"Connection lost to {ServiceName}: {ex.Message}"));
            await TryRecoverConnectionAsync(CancellationToken.None);
        }
    }

    private void FailAllPending(Exception ex)
    {
        foreach (var kvp in _pendingRequests)
        {
            kvp.Value.TrySetException(ex);
        }
        _pendingRequests.Clear();
    }

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (IsConnected)
        {
            return;
        }

        await TryRecoverConnectionAsync(ct);

        if (!IsConnected)
        {
            throw new InvalidOperationException($"MCP server {ServiceName} is not connected after retry.");
        }
    }

    private async Task TryRecoverConnectionAsync(CancellationToken ct)
    {
        await _reconnectLock.WaitAsync(ct);
        try
        {
            if (IsConnected)
            {
                return;
            }

            var delaysMs = new[] { 300, 1000, 3000 };
            Exception? lastError = null;

            foreach (var delayMs in delaysMs)
            {
                try
                {
                    _logger.LogWarning("[{ServiceName}] Attempting process restart", ServiceName);
                    await _processHost.RestartAsync(ct);
                    await StartAsync(ct);
                    _logger.LogInformation("[{ServiceName}] Restart successful", ServiceName);
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    _logger.LogWarning(ex, "[{ServiceName}] Restart attempt failed; retrying in {Delay}ms", ServiceName, delayMs);
                    await Task.Delay(delayMs, ct);
                }
            }

            if (lastError != null)
            {
                throw new InvalidOperationException($"Failed to restart {ServiceName}", lastError);
            }
        }
        finally
        {
            _reconnectLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _readCts?.Cancel();
        _readCts?.Dispose();
        
        if (_readTask != null)
        {
            try
            {
                await _readTask;
            }
            catch { }
        }

        GC.SuppressFinalize(this);
    }
}
