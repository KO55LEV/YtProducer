namespace YtProducer.McpRunner.Services;

using Microsoft.Extensions.Logging;
using YtProducer.McpRunner.Models;
using System.Collections.Concurrent;

/// <summary>
/// Caches tool names and their associated services.
/// Performs dynamic discovery via tools/list on first access.
/// </summary>
public class ToolRegistry
{
    private readonly ConcurrentDictionary<string, CachedTool> _toolCache = new();
    private readonly Dictionary<string, JsonRpcClient> _clientsByService;
    private readonly ILogger<ToolRegistry> _logger;
    private readonly object _discoveryLock = new();
    private bool _discoveryComplete = false;

    public ToolRegistry(Dictionary<string, JsonRpcClient> clientsByService, ILogger<ToolRegistry> logger)
    {
        _clientsByService = clientsByService;
        _logger = logger;
    }

    /// <summary>
    /// Discover tools from all services and populate cache.
    /// </summary>
    public async Task DiscoverToolsAsync(CancellationToken ct = default)
    {
        lock (_discoveryLock)
        {
            if (_discoveryComplete) return;
        }

        await RefreshAllToolsAsync(ct);

        lock (_discoveryLock)
        {
            _discoveryComplete = true;
        }
    }

    /// <summary>
    /// Refresh tool cache from all services.
    /// </summary>
    public async Task RefreshAllToolsAsync(CancellationToken ct = default)
    {
        _toolCache.Clear();

        _logger.LogInformation("Discovering tools from all MCP services...");

        foreach (var (serviceName, client) in _clientsByService)
        {
            try
            {
                _logger.LogInformation("Querying tools from {ServiceName}", serviceName);
                var tools = await client.ListToolsAsync(timeout: null, ct);

                foreach (var tool in tools)
                {
                    var cached = new CachedTool
                    {
                        Name = tool.Name,
                        ServiceName = serviceName,
                        Description = tool.Description,
                        InputSchema = tool.InputSchema
                    };
                    _toolCache[tool.Name] = cached;
                    _logger.LogDebug("Cached tool: {ToolName} -> {ServiceName}", tool.Name, serviceName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to discover tools from {ServiceName}", serviceName);
            }
        }

        _logger.LogInformation("Tool discovery complete. Found {Count} tools.", _toolCache.Count);
    }

    /// <summary>
    /// Refresh tool cache for one service by calling tools/list for that service only.
    /// </summary>
    public async Task RefreshToolsForServiceAsync(string serviceName, CancellationToken ct = default)
    {
        if (!_clientsByService.TryGetValue(serviceName, out var client))
        {
            throw new InvalidOperationException($"Unknown service: {serviceName}");
        }

        var existingToolNames = _toolCache.Values
            .Where(t => t.ServiceName.Equals(serviceName, StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Name)
            .ToList();

        foreach (var name in existingToolNames)
        {
            _toolCache.TryRemove(name, out _);
        }

        _logger.LogInformation("Querying tools from {ServiceName}", serviceName);
        var tools = await client.ListToolsAsync(timeout: null, ct);

        foreach (var tool in tools)
        {
            _toolCache[tool.Name] = new CachedTool
            {
                Name = tool.Name,
                ServiceName = serviceName,
                Description = tool.Description,
                InputSchema = tool.InputSchema
            };
        }

        _logger.LogInformation("Service {ServiceName} tool refresh complete. {Count} tools cached.",
            serviceName,
            _toolCache.Values.Count(t => t.ServiceName.Equals(serviceName, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Get all cached tools.
    /// </summary>
    public IEnumerable<CachedTool> GetAllTools() => _toolCache.Values;

    /// <summary>
    /// Get tools for a specific service.
    /// </summary>
    public IEnumerable<CachedTool> GetToolsByService(string serviceName)
    {
        return _toolCache.Values.Where(t => t.ServiceName == serviceName);
    }

    /// <summary>
    /// Look up which service provides a tool.
    /// </summary>
    public (string ServiceName, CachedTool Tool)? FindTool(string toolName)
    {
        if (_toolCache.TryGetValue(toolName, out var tool))
            return (tool.ServiceName, tool);
        return null;
    }

    /// <summary>
    /// Clear the cache (for testing or refresh).
    /// </summary>
    public void ClearCache()
    {
        _toolCache.Clear();
        lock (_discoveryLock)
        {
            _discoveryComplete = false;
        }
    }
}
