using System.Text.Json;
using YtProducer.Contracts.Reasoning;
using YtProducer.ReasoningAI;
using YtProducer.ReasoningAI.Abstractions;
using YtProducer.ReasoningAI.Providers.KieAi;

namespace YtProducer.Api.Endpoints;

public static class ReasoningEndpoints
{
    public static IEndpointRouteBuilder MapReasoningEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/reasoning").WithTags("Reasoning");

        group.MapPost("/test", RunReasoningTestAsync)
            .Produces<ReasoningTestResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);

        return app;
    }

    private static async Task<IResult> RunReasoningTestAsync(
        ReasoningTestRequest request,
        IReasoningClientFactory clientFactory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Provider))
        {
            return Results.BadRequest(new { message = "Provider is required." });
        }

        if (string.IsNullOrWhiteSpace(request.UserPrompt))
        {
            return Results.BadRequest(new { message = "UserPrompt is required." });
        }

        var provider = ParseProvider(request.Provider);
        if (provider is null)
        {
            return Results.BadRequest(new { message = $"Unsupported provider: {request.Provider}" });
        }

        if (provider == ReasoningProvider.KieAi &&
            !string.IsNullOrWhiteSpace(request.Model) &&
            !KieAiModels.All.Contains(request.Model.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { message = $"Unsupported Kie AI model: {request.Model}" });
        }

        try
        {
            var client = clientFactory.GetClient(provider.Value);
            var response = await client.CompleteAsync(
                new ReasoningRequest(
                    request.Model,
                    request.SystemPrompt,
                    request.UserPrompt),
                cancellationToken);

            return Results.Ok(new ReasoningTestResponse(
                response.Provider.ToString(),
                response.Model,
                response.Text,
                response.FinishReason,
                response.Usage is null ? null : JsonSerializer.Serialize(response.Usage),
                response.RawResponseJson));
        }
        catch (ReasoningClientException ex)
        {
            return Results.BadRequest(new
            {
                message = ex.Message,
                provider = ex.Provider.ToString(),
                statusCode = ex.StatusCode?.ToString(),
                responseBody = ex.ResponseBody
            });
        }
    }

    private static ReasoningProvider? ParseProvider(string rawProvider)
    {
        var normalized = rawProvider.Trim().ToLowerInvariant();
        return normalized switch
        {
            "kie" => ReasoningProvider.KieAi,
            "kie_ai" => ReasoningProvider.KieAi,
            "kieai" => ReasoningProvider.KieAi,
            "openai" => ReasoningProvider.OpenAi,
            "grok" => ReasoningProvider.Grok,
            _ => null
        };
    }
}
