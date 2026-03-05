namespace YtProducer.McpRunner.Services;

using Microsoft.Extensions.Logging;
using YtProducer.McpRunner.Models;

/// <summary>
/// Orchestrates all MCP services: starting, stopping, restart on crash, tool discovery.
/// </summary>
public class McpRunner : IAsyncDisposable
{
    private readonly ServiceRegistry _serviceRegistry;
    private readonly Dictionary<string, ProcessHost> _processHosts = new();
    private readonly Dictionary<string, JsonRpcClient> _jsonRpcClients = new();
    private readonly ILogger<McpRunner> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public McpRunner(
        ServiceRegistry serviceRegistry,
        ILogger<McpRunner> logger,
        ILoggerFactory loggerFactory)
    {
        _serviceRegistry = serviceRegistry;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Start all registered MCP services.
    /// </summary>
    public async Task StartAllAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Starting all MCP services...");

        foreach (var serviceDef in _serviceRegistry.GetAllServices())
        {
            try
            {
                await StartServiceAsync(serviceDef.Name, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start service {ServiceName}", serviceDef.Name);
            }
        }

        _logger.LogInformation("Service startup complete");
    }

    /// <summary>
    /// Start a specific service by name.
    /// </summary>
    public async Task StartServiceAsync(string serviceName, CancellationToken ct = default)
    {
        var def = _serviceRegistry.GetService(serviceName);
        if (def == null)
            throw new InvalidOperationException($"Service not found: {serviceName}");

        if (_processHosts.ContainsKey(serviceName))
            return; // Already started

        var hostLogger = _loggerFactory.CreateLogger<ProcessHost>();
        var clientLogger = _loggerFactory.CreateLogger<JsonRpcClient>();

        var host = new ProcessHost(def, hostLogger);
        await host.StartAsync(ct);

        _processHosts[serviceName] = host;

        var client = new JsonRpcClient(host, clientLogger);
        await client.StartAsync(ct);

        _jsonRpcClients[serviceName] = client;

        _logger.LogInformation("Service {ServiceName} started successfully", serviceName);
    }

    /// <summary>
    /// Stop all services.
    /// </summary>
    public async Task StopAllAsync()
    {
        _logger.LogInformation("Stopping all MCP services...");

        foreach (var kvp in _processHosts)
        {
            try
            {
                await kvp.Value.StopAsync();
                _logger.LogInformation("Service {ServiceName} stopped", kvp.Key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping service {ServiceName}", kvp.Key);
            }
        }

        _processHosts.Clear();
    }

    /// <summary>
    /// Get the dictionary of JSON-RPC clients (for ToolRouter).
    /// </summary>
    public Dictionary<string, JsonRpcClient> GetClients() => _jsonRpcClients;

    /// <summary>
    /// Check if all services are healthy.
    /// </summary>
    public bool AreAllServicesHealthy
    {
        get
        {
            if (_processHosts.Count == 0) return false;
            return _processHosts.Values.All(h => h.IsHealthy);
        }
    }

    /// <summary>
    /// Get the status of all services.
    /// </summary>
    public Dictionary<string, bool> GetServiceStatus()
    {
        return _serviceRegistry.GetAllServices().ToDictionary(
            s => s.Name,
            s => _processHosts.TryGetValue(s.Name, out var host) && host.IsHealthy
        );
    }

    public async ValueTask DisposeAsync()
    {
        await StopAllAsync();

        foreach (var client in _jsonRpcClients.Values)
        {
            await client.DisposeAsync();
        }

        foreach (var host in _processHosts.Values)
        {
            await host.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }
}
