namespace YtProducer.ReasoningAI.Abstractions;

public sealed record ReasoningUsage(
    int? PromptTokens,
    int? CompletionTokens,
    int? TotalTokens);
