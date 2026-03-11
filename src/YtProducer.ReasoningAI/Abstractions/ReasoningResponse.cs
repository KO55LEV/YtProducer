namespace YtProducer.ReasoningAI.Abstractions;

public sealed record ReasoningResponse(
    ReasoningProvider Provider,
    string Model,
    string Text,
    string? FinishReason,
    ReasoningUsage? Usage,
    string RawResponseJson);
