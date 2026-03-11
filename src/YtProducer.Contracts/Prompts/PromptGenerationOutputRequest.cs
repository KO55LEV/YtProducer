namespace YtProducer.Contracts.Prompts;

public sealed record PromptGenerationOutputRequest(
    string? OutputType,
    string? OutputLabel,
    string? OutputText,
    string? OutputJson,
    bool IsPrimary,
    bool IsValid,
    string? ValidationErrors,
    string? ProviderResponseJson);
