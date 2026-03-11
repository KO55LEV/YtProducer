using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YtProducer.ReasoningAI.Abstractions;

namespace YtProducer.ReasoningAI.Providers.KieAi;

public sealed class KieAiReasoningClient : IReasoningClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<KieAiOptions> _optionsMonitor;
    private readonly ILogger<KieAiReasoningClient> _logger;

    public KieAiReasoningClient(
        HttpClient httpClient,
        IOptionsMonitor<KieAiOptions> optionsMonitor,
        ILogger<KieAiReasoningClient> logger)
    {
        _httpClient = httpClient;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    public ReasoningProvider Provider => ReasoningProvider.KieAi;

    public async Task<ReasoningResponse> CompleteAsync(ReasoningRequest request, CancellationToken cancellationToken = default)
    {
        var options = _optionsMonitor.CurrentValue;
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new ReasoningClientException(Provider, "Kie AI API key is not configured.");
        }

        var model = string.IsNullOrWhiteSpace(request.Model) ? options.DefaultModel : request.Model.Trim();
        var endpoint = BuildEndpoint(options.BaseUrl, model);
        var payload = BuildPayload(model, request);

        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(payload)
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

        _logger.LogInformation("Calling Kie AI model {Model} at {Endpoint}", model, endpoint);

        using var response = await _httpClient.SendAsync(message, cancellationToken);
        var rawResponse = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new ReasoningClientException(
                Provider,
                $"Kie AI request failed with status {(int)response.StatusCode}.",
                response.StatusCode,
                rawResponse);
        }

        KieAiChatCompletionResponse? completion;
        try
        {
            completion = JsonSerializer.Deserialize<KieAiChatCompletionResponse>(rawResponse, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ReasoningClientException(Provider, "Kie AI returned invalid JSON.", response.StatusCode, rawResponse, ex);
        }

        var text = ExtractText(completion);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ReasoningClientException(Provider, "Kie AI response did not include assistant content.", response.StatusCode, rawResponse);
        }

        return new ReasoningResponse(
            Provider,
            completion?.Model ?? model,
            text,
            completion?.Choices?.FirstOrDefault()?.FinishReason,
            completion?.Usage is null
                ? null
                : new ReasoningUsage(completion.Usage.PromptTokens, completion.Usage.CompletionTokens, completion.Usage.TotalTokens),
            rawResponse);
    }

    private static string BuildEndpoint(string baseUrl, string model)
    {
        if (!KieAiModels.All.Contains(model, StringComparer.OrdinalIgnoreCase))
        {
            throw new ReasoningClientException(ReasoningProvider.KieAi, $"Unsupported Kie AI model: {model}");
        }

        return $"{baseUrl.TrimEnd('/')}/{model}/v1/chat/completions";
    }

    private static object BuildPayload(string model, ReasoningRequest request)
    {
        var messages = BuildMessages(request);

        return new
        {
            model,
            stream = false,
            temperature = request.Temperature,
            max_tokens = request.MaxTokens,
            response_format = string.IsNullOrWhiteSpace(request.ResponseFormat) ? null : new { type = request.ResponseFormat },
            messages = messages.Select(x => new
            {
                role = x.Role switch
                {
                    ReasoningMessageRole.System => "system",
                    ReasoningMessageRole.Assistant => "assistant",
                    _ => "user"
                },
                content = x.Content
            })
        };
    }

    private static IReadOnlyList<ReasoningMessage> BuildMessages(ReasoningRequest request)
    {
        if (request.Messages is { Count: > 0 })
        {
            return request.Messages;
        }

        var messages = new List<ReasoningMessage>();
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            messages.Add(new ReasoningMessage(ReasoningMessageRole.System, request.SystemPrompt.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(request.UserPrompt))
        {
            messages.Add(new ReasoningMessage(ReasoningMessageRole.User, request.UserPrompt.Trim()));
        }

        if (messages.Count == 0)
        {
            throw new ReasoningClientException(ReasoningProvider.KieAi, "Reasoning request must contain at least one message.");
        }

        return messages;
    }

    private static string? ExtractText(KieAiChatCompletionResponse? response)
    {
        var choice = response?.Choices?.FirstOrDefault();
        if (choice?.Message is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(choice.Message.Content))
        {
            return choice.Message.Content;
        }

        if (choice.Message.ContentParts is { Count: > 0 })
        {
            return string.Join(Environment.NewLine, choice.Message.ContentParts
                .Where(x => !string.IsNullOrWhiteSpace(x.Text))
                .Select(x => x.Text!.Trim()));
        }

        return null;
    }

    private sealed record KieAiChatCompletionResponse(
        string? Id,
        string? Model,
        List<KieAiChoice>? Choices,
        KieAiUsage? Usage);

    private sealed record KieAiChoice(
        int? Index,
        KieAiMessage? Message,
        [property: JsonPropertyName("finish_reason")]
        string? FinishReason);

    private sealed record KieAiMessage(
        string? Role,
        string? Content,
        List<KieAiContentPart>? ContentParts);

    private sealed record KieAiContentPart(
        string? Type,
        string? Text);

    private sealed record KieAiUsage(
        [property: JsonPropertyName("prompt_tokens")]
        int? PromptTokens,
        [property: JsonPropertyName("completion_tokens")]
        int? CompletionTokens,
        [property: JsonPropertyName("total_tokens")]
        int? TotalTokens);
}
