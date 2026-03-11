namespace YtProducer.Contracts.Reasoning;

public sealed record ReasoningTestRequest(
    string Provider,
    string? Model,
    string? SystemPrompt,
    string UserPrompt);
