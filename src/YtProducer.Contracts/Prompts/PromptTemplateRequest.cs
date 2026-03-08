namespace YtProducer.Contracts.Prompts;

public sealed record PromptTemplateRequest(
    string Name,
    string Slug,
    string Category,
    string? Description,
    string TemplateBody,
    string InputMode,
    string? DefaultModel,
    bool IsActive,
    int SortOrder);
