namespace YtProducer.ReasoningAI.Providers.KieAi;

public sealed class KieAiOptions
{
    public const string SectionName = "ReasoningAI:KieAI";

    public string BaseUrl { get; set; } = "https://api.kie.ai";

    public string? ApiKey { get; set; }

    public string DefaultModel { get; set; } = KieAiModels.Gemini3Pro;

    public int TimeoutSeconds { get; set; } = 120;
}
