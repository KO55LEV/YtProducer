namespace YtProducer.Contracts.Prompts;

public sealed record PromptGenerationResponse(
    Guid Id,
    Guid TemplateId,
    string Theme,
    string Status,
    string? Model,
    string InputJson,
    string ResolvedPrompt,
    Guid? JobId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    string? ErrorMessage,
    IReadOnlyList<PromptGenerationOutputResponse> Outputs);
