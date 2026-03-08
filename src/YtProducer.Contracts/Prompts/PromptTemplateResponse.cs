namespace YtProducer.Contracts.Prompts;

public sealed record PromptTemplateResponse(
    Guid Id,
    string Name,
    string Slug,
    string Category,
    string? Description,
    string TemplateBody,
    string InputMode,
    string? DefaultModel,
    bool IsActive,
    int SortOrder,
    int Version,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
