using YtProducer.Domain.Enums;

namespace YtProducer.Domain.Entities;

public sealed class PromptGeneration
{
    public Guid Id { get; set; }

    public Guid TemplateId { get; set; }

    public string Theme { get; set; } = string.Empty;

    public PromptGenerationStatus Status { get; set; } = PromptGenerationStatus.Draft;

    public string? Model { get; set; }

    public string InputJson { get; set; } = string.Empty;

    public string ResolvedPrompt { get; set; } = string.Empty;

    public Guid? JobId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? FinishedAtUtc { get; set; }

    public string? ErrorMessage { get; set; }

    public PromptTemplate? Template { get; set; }

    public ICollection<PromptGenerationOutput> Outputs { get; set; } = new List<PromptGenerationOutput>();
}
