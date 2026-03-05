namespace YtProducer.McpRunner.Services;

using Microsoft.Extensions.Logging;
using YtProducer.McpRunner.Models;

/// <summary>
/// Manages MCP service definitions and provides access to clients.
/// </summary>
public class ServiceRegistry
{
    private readonly Dictionary<string, ServiceDefinition> _services = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<ServiceRegistry> _logger;

    public ServiceRegistry(ILogger<ServiceRegistry> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Register a service definition.
    /// </summary>
    public void Register(ServiceDefinition definition)
    {
        _services[definition.Name] = definition;
        _logger.LogInformation("Registered service: {ServiceName}", definition.Name);
    }

    /// <summary>
    /// Register multiple service definitions.
    /// </summary>
    public void RegisterBulk(params ServiceDefinition[] definitions)
    {
        foreach (var def in definitions)
            Register(def);
    }

    /// <summary>
    /// Get a service definition by name.
    /// </summary>
    public ServiceDefinition? GetService(string name)
    {
        _services.TryGetValue(name, out var service);
        return service;
    }

    /// <summary>
    /// Get all registered services.
    /// </summary>
    public IEnumerable<ServiceDefinition> GetAllServices() => _services.Values;

    /// <summary>
    /// Check if a service is registered.
    /// </summary>
    public bool HasService(string name) => _services.ContainsKey(name);
}

/// <summary>
/// Routes tool calls to the correct service based on tool name.
/// </summary>
public class ToolRouter
{
    private readonly ToolRegistry _toolRegistry;
    private readonly Dictionary<string, JsonRpcClient> _clientsByService;
    private readonly ResponseNormalizer _normalizer;
    private readonly ILogger<ToolRouter> _logger;

    public ToolRouter(
        ToolRegistry toolRegistry,
        Dictionary<string, JsonRpcClient> clientsByService,
        ResponseNormalizer normalizer,
        ILogger<ToolRouter> logger)
    {
        _toolRegistry = toolRegistry;
        _clientsByService = clientsByService;
        _normalizer = normalizer;
        _logger = logger;
    }

    /// <summary>
    /// Call a tool by name, routing to the correct service.
    /// </summary>
    public async Task<NormalizedResponse> CallToolAsync(
        string toolName,
        System.Text.Json.JsonElement? arguments = null,
        TimeSpan? timeout = null,
        bool preserveRaw = false,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Tool call requested: {ToolName}", toolName);

        var toolInfo = _toolRegistry.FindTool(toolName);
        if (toolInfo == null)
        {
            _logger.LogError("Tool not found: {ToolName}", toolName);
            return new NormalizedResponse
            {
                Ok = false,
                Error = $"Tool not found: {toolName}"
            };
        }

        var (serviceName, _) = toolInfo.Value;
        if (!_clientsByService.TryGetValue(serviceName, out var client))
        {
            _logger.LogError("Client not found for service: {ServiceName}", serviceName);
            return new NormalizedResponse
            {
                Ok = false,
                Error = $"Client not available for service: {serviceName}"
            };
        }

        try
        {
            _logger.LogInformation("Routing {ToolName} to service {ServiceName}", toolName, serviceName);
            var response = await client.CallToolRawAsync(toolName, arguments, timeout, ct);

            var normalized = _normalizer.Normalize(response, serviceName, preserveRaw);
            _logger.LogInformation("Tool {ToolName} completed successfully", toolName);
            return normalized;
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("Tool {ToolName} timed out", toolName);
            return new NormalizedResponse
            {
                Ok = false,
                Error = $"Tool call timed out: {toolName}",
                Meta = new Dictionary<string, object> { { "service", serviceName } }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling tool {ToolName}", toolName);
            return new NormalizedResponse
            {
                Ok = false,
                Error = $"Tool call failed: {ex.Message}",
                Meta = new Dictionary<string, object> { { "service", serviceName } }
            };
        }
    }
}
