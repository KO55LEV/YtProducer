using YtProducer.Domain.Enums;

namespace YtProducer.Domain.Entities;

public sealed class PromptGeneration
{
    public Guid Id { get; set; }

    public Guid TemplateId { get; set; }

    public string Purpose { get; set; } = string.Empty;

    public string Provider { get; set; } = string.Empty;

    public PromptGenerationStatus Status { get; set; } = PromptGenerationStatus.Draft;

    public string? Model { get; set; }

    public string? InputLabel { get; set; }

    public string InputJson { get; set; } = string.Empty;

    public string ResolvedSystemPrompt { get; set; } = string.Empty;

    public string ResolvedUserPrompt { get; set; } = string.Empty;

    public int? LatencyMs { get; set; }

    public string? TokenUsageJson { get; set; }

    public string? RunMetadataJson { get; set; }

    public string? TargetType { get; set; }

    public string? TargetId { get; set; }

    public Guid? JobId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? FinishedAtUtc { get; set; }

    public string? ErrorMessage { get; set; }

    public PromptTemplate? Template { get; set; }

    public ICollection<PromptGenerationOutput> Outputs { get; set; } = new List<PromptGenerationOutput>();
}
