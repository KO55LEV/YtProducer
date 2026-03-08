namespace YtProducer.Contracts.Prompts;

public sealed record PromptGenerationOutputRequest(
    string RawText,
    string? OutputType);
