using System.Text.Json;
using System.Text.Json.Serialization;

namespace YtProducer.Media.Mcp;

public sealed class JsonRpcRequest
{
    [JsonPropertyName("jsonrpc")]
    public string? JsonRpc { get; set; }

    [JsonPropertyName("id")]
    public JsonElement Id { get; set; }

    [JsonPropertyName("method")]
    public string? Method { get; set; }

    [JsonPropertyName("params")]
    public JsonElement Params { get; set; }
}

public sealed class JsonRpcResponse
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; init; } = "2.0";

    [JsonPropertyName("id")]
    public object? Id { get; init; }

    [JsonPropertyName("result")]
    public object? Result { get; init; }

    [JsonPropertyName("error")]
    public JsonRpcError? Error { get; init; }

    public static JsonRpcResponse FromResult(object? id, object? result)
        => new() { Id = id, Result = result };

    public static JsonRpcResponse FromError(object? id, JsonRpcError error)
        => new() { Id = id, Error = error };
}

public sealed class JsonRpcError
{
    public const int ParseErrorCode = -32700;
    public const int InvalidRequestCode = -32600;
    public const int MethodNotFoundCode = -32601;
    public const int InvalidParamsCode = -32602;
    public const int InternalErrorCode = -32603;

    [JsonPropertyName("code")]
    public int Code { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("data")]
    public object? Data { get; init; }

    public static JsonRpcError ParseError(string message, object? data = null)
        => new() { Code = ParseErrorCode, Message = message, Data = data };

    public static JsonRpcError InvalidRequest(string message, object? data = null)
        => new() { Code = InvalidRequestCode, Message = message, Data = data };

    public static JsonRpcError MethodNotFound(string message, object? data = null)
        => new() { Code = MethodNotFoundCode, Message = message, Data = data };

    public static JsonRpcError InvalidParams(string message, object? data = null)
        => new() { Code = InvalidParamsCode, Message = message, Data = data };

    public static JsonRpcError InternalError(string message, object? data = null)
        => new() { Code = InternalErrorCode, Message = message, Data = data };
}

public static class JsonRpcId
{
    public static bool TryNormalize(JsonElement idElement, out object? normalized)
    {
        normalized = null;

        return idElement.ValueKind switch
        {
            JsonValueKind.Undefined => true,
            JsonValueKind.Null => true,
            JsonValueKind.String => Assign(idElement.GetString(), out normalized),
            JsonValueKind.Number when idElement.TryGetInt64(out var intValue) => Assign(intValue, out normalized),
            JsonValueKind.Number when idElement.TryGetDouble(out var doubleValue) => Assign(doubleValue, out normalized),
            _ => false
        };
    }

    private static bool Assign(object? value, out object? normalized)
    {
        normalized = value;
        return true;
    }
}

public static class JsonRpcModels
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
