namespace YtProducer.Contracts.Prompts;

public sealed record PromptTemplateRequest(
    string Name,
    string Slug,
    string Purpose,
    string? Description,
    string? Notes,
    string? SystemPrompt,
    string? UserPromptTemplate,
    string InputMode,
    string Provider,
    string? Model,
    string OutputMode,
    string? SchemaKey,
    string? SettingsJson,
    string? InputContractJson,
    string? MetadataJson,
    bool IsActive,
    bool IsDefault,
    int SortOrder);
