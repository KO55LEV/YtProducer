namespace YtProducer.ReasoningAI.Providers.KieAi;

public static class KieAiModels
{
    public const string Gemini3Pro = "gemini-3-pro";
    public const string Gemini25Flash = "gemini-2.5-flash";
    public const string Gemini25Pro = "gemini-2.5-pro";

    public static readonly IReadOnlyList<string> All =
    [
        Gemini3Pro,
        Gemini25Flash,
        Gemini25Pro
    ];
}
