namespace YtProducer.Contracts.Prompts;

public sealed record PromptGenerationRequest(
    string Theme,
    string? Model);
