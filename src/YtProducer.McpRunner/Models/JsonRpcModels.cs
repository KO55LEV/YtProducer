namespace YtProducer.McpRunner.Models;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// JSON-RPC 2.0 Request envelope.
/// </summary>
public record JsonRpcRequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    [JsonConverter(typeof(JsonRpcIdConverter))]
    public object Id { get; set; } = null!;

    [JsonPropertyName("method")]
    public string Method { get; set; } = null!;

    [JsonPropertyName("params")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Params { get; set; }
}

/// <summary>
/// JSON-RPC 2.0 Response envelope.
/// </summary>
public record JsonRpcResponse
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    [JsonConverter(typeof(JsonRpcIdConverter))]
    public object Id { get; set; } = null!;

    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Result { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonRpcError? Error { get; set; }
}

/// <summary>
/// JSON-RPC 2.0 Error object.
/// </summary>
public record JsonRpcError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = null!;

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Data { get; set; }
}

/// <summary>
/// Normalized response for CLI output.
/// </summary>
public record NormalizedResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Data { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }

    [JsonPropertyName("meta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, object>? Meta { get; set; }
}

/// <summary>
/// Tool metadata from tools/list.
/// </summary>
public record ToolDescription
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("inputSchema")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? InputSchema { get; set; }
}

// Custom JSON converter for RpcId (can be string or number)
public class JsonRpcIdConverter : System.Text.Json.Serialization.JsonConverter<object>
{
    public override object Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
    {
        if (reader.TokenType == System.Text.Json.JsonTokenType.String)
            return reader.GetString() ?? "";
        if (reader.TokenType == System.Text.Json.JsonTokenType.Number)
            return reader.GetInt64();
        throw new System.Text.Json.JsonException($"Unexpected token type: {reader.TokenType}");
    }

    public override void Write(System.Text.Json.Utf8JsonWriter writer, object value, System.Text.Json.JsonSerializerOptions options)
    {
        if (value is string s)
            writer.WriteStringValue(s);
        else if (value is long l)
            writer.WriteNumberValue(l);
        else if (value is int i)
            writer.WriteNumberValue(i);
        else
            throw new System.Text.Json.JsonException($"Unexpected value type: {value?.GetType()}");
    }
}
