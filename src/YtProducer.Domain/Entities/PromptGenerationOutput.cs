namespace YtProducer.Domain.Entities;

public sealed class PromptGenerationOutput
{
    public Guid Id { get; set; }

    public Guid PromptGenerationId { get; set; }

    public string OutputType { get; set; } = "album_json";

    public string? RawText { get; set; }

    public string? FormattedJson { get; set; }

    public bool IsValidJson { get; set; }

    public string? ValidationErrors { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public PromptGeneration? PromptGeneration { get; set; }
}
