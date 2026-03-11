namespace YtProducer.Domain.Entities;

public sealed class PromptTemplate
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Notes { get; set; }

    public string TemplateBody { get; set; } = string.Empty;

    public string? SystemPrompt { get; set; }

    public string? UserPromptTemplate { get; set; }

    public string InputMode { get; set; } = "theme_only";

    public string Provider { get; set; } = "google";

    public string? DefaultModel { get; set; }

    public string OutputMode { get; set; } = "json";

    public string? SchemaKey { get; set; }

    public string? SettingsJson { get; set; }

    public string? InputContractJson { get; set; }

    public string? MetadataJson { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsDefault { get; set; }

    public int SortOrder { get; set; }

    public int Version { get; set; } = 1;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ICollection<PromptGeneration> Generations { get; set; } = new List<PromptGeneration>();
}
