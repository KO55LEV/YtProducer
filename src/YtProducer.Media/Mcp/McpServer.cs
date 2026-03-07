using System.Text;
using System.Text.Json;
using YtProducer.Media.Tools;

namespace YtProducer.Media.Mcp;

public sealed class McpServer
{
    private const string ToolsListMethod = "tools/list";
    private const string ToolsCallMethod = "tools/call";

    private static readonly JsonElement EmptyArguments = JsonDocument.Parse("{}").RootElement.Clone();

    private readonly VideoCreateMusicVisualizerTool _visualizerTool;
    private readonly VideoUpscaleTool _upscaleTool;
    private readonly MediaCreateYoutubeThumbnailTool _youtubeThumbnailTool;

    public McpServer(
        VideoCreateMusicVisualizerTool visualizerTool,
        VideoUpscaleTool upscaleTool,
        MediaCreateYoutubeThumbnailTool youtubeThumbnailTool)
    {
        _visualizerTool = visualizerTool;
        _upscaleTool = upscaleTool;
        _youtubeThumbnailTool = youtubeThumbnailTool;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var payload = await ReadMessageAsync(cancellationToken).ConfigureAwait(false);
            if (payload is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(payload))
            {
                continue;
            }

            JsonRpcResponse response;

            try
            {
                using var document = JsonDocument.Parse(payload);
                response = await DispatchAsync(document.RootElement.Clone(), cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                response = JsonRpcResponse.FromError(null, JsonRpcError.ParseError("Invalid JSON payload.", ex.Message));
            }
            catch (Exception ex)
            {
                response = JsonRpcResponse.FromError(null, JsonRpcError.InternalError("Unexpected server error.", ex.Message));
            }

            var serialized = JsonSerializer.Serialize(response, JsonRpcModels.SerializerOptions);
            await Console.Out.WriteLineAsync(serialized).ConfigureAwait(false);
            await Console.Out.FlushAsync().ConfigureAwait(false);
        }
    }

    private async Task<JsonRpcResponse> DispatchAsync(JsonElement requestElement, CancellationToken cancellationToken)
    {
        if (requestElement.ValueKind is not JsonValueKind.Object)
        {
            return JsonRpcResponse.FromError(null, JsonRpcError.InvalidRequest("JSON-RPC request must be an object."));
        }

        JsonRpcRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<JsonRpcRequest>(requestElement.GetRawText(), JsonRpcModels.SerializerOptions);
        }
        catch (JsonException)
        {
            return JsonRpcResponse.FromError(null, JsonRpcError.InvalidRequest("Invalid JSON-RPC request object."));
        }

        if (request is null)
        {
            return JsonRpcResponse.FromError(null, JsonRpcError.InvalidRequest("Request payload is empty."));
        }

        if (!string.Equals(request.JsonRpc, "2.0", StringComparison.Ordinal))
        {
            return JsonRpcResponse.FromError(null, JsonRpcError.InvalidRequest("Only JSON-RPC 2.0 is supported."));
        }

        if (!JsonRpcId.TryNormalize(request.Id, out var requestId))
        {
            return JsonRpcResponse.FromError(null, JsonRpcError.InvalidRequest("Invalid request id. Use string, number, or null."));
        }

        if (string.IsNullOrWhiteSpace(request.Method))
        {
            return JsonRpcResponse.FromError(requestId, JsonRpcError.InvalidRequest("Missing required method."));
        }

        return request.Method switch
        {
            ToolsListMethod => JsonRpcResponse.FromResult(requestId, new { tools = ToolSchemas.ToDescriptors() }),
            ToolsCallMethod => await HandleToolsCallAsync(requestId, request.Params, cancellationToken).ConfigureAwait(false),
            _ => JsonRpcResponse.FromError(requestId, JsonRpcError.MethodNotFound($"Method '{request.Method}' is not supported."))
        };
    }

    private async Task<JsonRpcResponse> HandleToolsCallAsync(object? requestId, JsonElement requestParams, CancellationToken cancellationToken)
    {
        if (requestParams.ValueKind is not JsonValueKind.Object)
        {
            return JsonRpcResponse.FromError(requestId, JsonRpcError.InvalidParams("tools/call expects object params."));
        }

        if (!requestParams.TryGetProperty("name", out var nameProp) || nameProp.ValueKind is not JsonValueKind.String)
        {
            return JsonRpcResponse.FromError(requestId, JsonRpcError.InvalidParams("Missing required string param: name."));
        }

        var toolName = nameProp.GetString();

        var arguments = EmptyArguments;
        if (requestParams.TryGetProperty("arguments", out var argsProp))
        {
            if (argsProp.ValueKind is not JsonValueKind.Object)
            {
                return JsonRpcResponse.FromError(requestId, JsonRpcError.InvalidParams("arguments must be an object."));
            }

            arguments = argsProp;
        }

        try
        {
            object result;

            if (string.Equals(toolName, ToolSchemas.VisualizerToolName, StringComparison.Ordinal))
            {
                result = await _visualizerTool.ExecuteAsync(arguments, cancellationToken).ConfigureAwait(false);
            }
            else if (string.Equals(toolName, ToolSchemas.UpscaleToolName, StringComparison.Ordinal))
            {
                result = await _upscaleTool.ExecuteAsync(arguments, cancellationToken).ConfigureAwait(false);
            }
            else if (string.Equals(toolName, ToolSchemas.CreateYoutubeThumbnailToolName, StringComparison.Ordinal))
            {
                result = await _youtubeThumbnailTool.ExecuteAsync(arguments, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                return JsonRpcResponse.FromError(requestId, JsonRpcError.InvalidParams($"Unknown tool '{toolName}'."));
            }

            return JsonRpcResponse.FromResult(requestId, result);
        }
        catch (ToolValidationException ex)
        {
            return JsonRpcResponse.FromError(requestId, JsonRpcError.InvalidParams(ex.Message));
        }
        catch (Exception ex)
        {
            return JsonRpcResponse.FromError(requestId, JsonRpcError.InternalError("Tool execution failed.", ex.Message));
        }
    }

    private static async Task<string?> ReadMessageAsync(CancellationToken cancellationToken)
    {
        var firstLine = await Console.In.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (firstLine is null)
        {
            return null;
        }

        if (firstLine.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(firstLine["Content-Length:".Length..].Trim(), out var length) || length < 0)
            {
                throw new JsonException("Invalid Content-Length header.");
            }

            while (true)
            {
                var headerLine = await Console.In.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (headerLine is null)
                {
                    return null;
                }

                if (headerLine.Length == 0)
                {
                    break;
                }
            }

            var buffer = new char[length];
            var read = 0;
            while (read < length)
            {
                var chunk = await Console.In.ReadAsync(buffer.AsMemory(read, length - read), cancellationToken).ConfigureAwait(false);
                if (chunk <= 0)
                {
                    break;
                }

                read += chunk;
            }

            return new string(buffer, 0, read);
        }

        return firstLine;
    }
}
