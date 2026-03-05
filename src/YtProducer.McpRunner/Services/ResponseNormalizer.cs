namespace YtProducer.McpRunner.Services;

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using YtProducer.McpRunner.Models;

/// <summary>
/// Normalizes responses from different MCP services to a consistent shape.
/// Rules:
/// - KieAi + Media: payload is in "result" directly
/// - YouTube: payload is in "result.data"
/// Normalized output: { "ok": true/false, "data": <payload>, "meta": {...} }
/// </summary>
public class ResponseNormalizer
{
    private readonly ILogger<ResponseNormalizer> _logger;

    public ResponseNormalizer(ILogger<ResponseNormalizer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Normalize a JSON-RPC response to consistent output format.
    /// </summary>
    public NormalizedResponse Normalize(JsonRpcResponse response, string serviceName, bool preserveRaw = false)
    {
        if (response.Error != null)
        {
            return new NormalizedResponse
            {
                Ok = false,
                Error = response.Error.Message,
                Meta = new Dictionary<string, object>
                {
                    { "errorCode", response.Error.Code },
                    { "service", serviceName }
                }
            };
        }

        if (response.Result == null)
        {
            return new NormalizedResponse
            {
                Ok = true,
                Data = null,
                Meta = new Dictionary<string, object> { { "service", serviceName } }
            };
        }

        try
        {
            // Extract the actual data based on service type
            JsonElement? data = ExtractPayload(response.Result.Value, serviceName);

            var meta = new Dictionary<string, object>
            {
                { "service", serviceName }
            };

            if (preserveRaw)
            {
                meta["rawResponse"] = JsonSerializer.Serialize(response, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                });
            }

            return new NormalizedResponse
            {
                Ok = true,
                Data = data,
                Meta = meta
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error normalizing response from {ServiceName}", serviceName);
            return new NormalizedResponse
            {
                Ok = false,
                Error = $"Normalization error: {ex.Message}",
                Meta = new Dictionary<string, object>
                {
                    { "service", serviceName },
                    { "exception", ex.GetType().Name }
                }
            };
        }
    }

    private JsonElement? ExtractPayload(JsonElement result, string serviceName)
    {
        // YouTube services return { "data": {...} } inside result
        if (serviceName.Equals("YouTube", StringComparison.OrdinalIgnoreCase))
        {
            if (result.TryGetProperty("data", out var dataElement))
                return dataElement;
        }

        // KieAi and Media return result directly
        return result;
    }
}
