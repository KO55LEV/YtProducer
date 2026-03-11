namespace YtProducer.ReasoningAI.Abstractions;

public sealed record ReasoningMessage(
    ReasoningMessageRole Role,
    string Content);
