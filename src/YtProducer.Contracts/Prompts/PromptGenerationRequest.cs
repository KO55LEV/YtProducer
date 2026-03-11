namespace YtProducer.Contracts.Prompts;

public sealed record PromptGenerationRequest(
    string? InputLabel,
    string InputJson,
    string? Model,
    string? ResolvedSystemPrompt,
    string? ResolvedUserPrompt,
    string? TargetType,
    string? TargetId);
