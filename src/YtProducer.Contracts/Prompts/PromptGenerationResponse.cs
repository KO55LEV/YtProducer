namespace YtProducer.Contracts.Prompts;

public sealed record PromptGenerationResponse(
    Guid Id,
    Guid TemplateId,
    string Purpose,
    string Provider,
    string Status,
    string? Model,
    string? InputLabel,
    string InputJson,
    string ResolvedSystemPrompt,
    string ResolvedUserPrompt,
    Guid? JobId,
    int? LatencyMs,
    string? TokenUsageJson,
    string? RunMetadataJson,
    string? TargetType,
    string? TargetId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    string? ErrorMessage,
    IReadOnlyList<PromptGenerationOutputResponse> Outputs);
