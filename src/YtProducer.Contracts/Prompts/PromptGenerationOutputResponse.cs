namespace YtProducer.Contracts.Prompts;

public sealed record PromptGenerationOutputResponse(
    Guid Id,
    Guid PromptGenerationId,
    string OutputType,
    string? OutputLabel,
    string? OutputText,
    string? OutputJson,
    bool IsPrimary,
    bool IsValid,
    string? ValidationErrors,
    string? ProviderResponseJson,
    DateTimeOffset CreatedAtUtc);
