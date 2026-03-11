namespace YtProducer.Contracts.Reasoning;

public sealed record ReasoningTestResponse(
    string Provider,
    string Model,
    string Text,
    string? FinishReason,
    string? UsageJson,
    string RawResponseJson);
