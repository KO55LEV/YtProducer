using System.Text.Json.Serialization;

namespace YtProducer.Media.Models;

public sealed class AnalysisDocument
{
    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("duration_seconds")]
    public double DurationSeconds { get; set; }

    [JsonPropertyName("fps")]
    public int Fps { get; set; }

    [JsonPropertyName("frame_count")]
    public int FrameCount { get; set; }

    [JsonPropertyName("sample_rate")]
    public int SampleRate { get; set; }

    [JsonPropertyName("channels")]
    public int Channels { get; set; }

    [JsonPropertyName("eq_bands")]
    public int EqBands { get; set; }

    [JsonPropertyName("frames")]
    public List<AnalysisFrame> Frames { get; set; } = new();
}

public sealed class AnalysisFrame
{
    [JsonPropertyName("i")]
    public int I { get; set; }

    [JsonPropertyName("t")]
    public double T { get; set; }

    [JsonPropertyName("bands")]
    public float[] Bands { get; set; } = [];

    [JsonPropertyName("bass")]
    public float Bass { get; set; }

    [JsonPropertyName("mid")]
    public float Mid { get; set; }

    [JsonPropertyName("high")]
    public float High { get; set; }

    [JsonPropertyName("energy")]
    public float Energy { get; set; }

    [JsonPropertyName("beat")]
    public bool Beat { get; set; }
}
