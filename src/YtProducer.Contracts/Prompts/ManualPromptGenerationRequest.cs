namespace YtProducer.Contracts.Prompts;

public sealed record ManualPromptGenerationRequest(
    string? InputLabel,
    string InputJson,
    string? Model,
    string? ResolvedSystemPrompt,
    string? ResolvedUserPrompt,
    string? TargetType,
    string? TargetId,
    string? OutputType,
    string? OutputLabel,
    string? OutputText,
    string? OutputJson,
    bool IsValid,
    string? ValidationErrors,
    string? ProviderResponseJson,
    string? ManualProvider,
    string? ManualModel);
