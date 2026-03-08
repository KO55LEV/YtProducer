namespace YtProducer.Contracts.Prompts;

public sealed record PromptGenerationOutputResponse(
    Guid Id,
    Guid PromptGenerationId,
    string OutputType,
    string? RawText,
    string? FormattedJson,
    bool IsValidJson,
    string? ValidationErrors,
    DateTimeOffset CreatedAtUtc);
