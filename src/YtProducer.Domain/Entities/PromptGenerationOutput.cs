namespace YtProducer.Domain.Entities;

public sealed class PromptGenerationOutput
{
    public Guid Id { get; set; }

    public Guid PromptGenerationId { get; set; }

    public string OutputType { get; set; } = "text";

    public string? OutputLabel { get; set; }

    public string? OutputText { get; set; }

    public string? OutputJson { get; set; }

    public bool IsPrimary { get; set; }

    public bool IsValid { get; set; }

    public string? ValidationErrors { get; set; }

    public string? ProviderResponseJson { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public PromptGeneration? PromptGeneration { get; set; }
}
