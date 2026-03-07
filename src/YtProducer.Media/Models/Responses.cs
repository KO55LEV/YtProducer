using System.Text.Json.Serialization;

namespace YtProducer.Media.Models;

public sealed record VideoCreateMusicVisualizerResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("output_path")]
    public string OutputPath { get; init; } = string.Empty;

    [JsonPropertyName("duration_seconds")]
    public double DurationSeconds { get; init; }

    [JsonPropertyName("width")]
    public int Width { get; init; }

    [JsonPropertyName("height")]
    public int Height { get; init; }

    [JsonPropertyName("fps")]
    public int Fps { get; init; }

    [JsonPropertyName("analysis_path")]
    public string AnalysisPath { get; init; } = string.Empty;

    [JsonPropertyName("frames_dir")]
    public string FramesDir { get; init; } = string.Empty;

    [JsonPropertyName("frame_count")]
    public int FrameCount { get; init; }

    [JsonPropertyName("ffmpeg_command")]
    public string FfmpegCommand { get; init; } = string.Empty;

    [JsonPropertyName("stderr_tail")]
    public string StderrTail { get; init; } = string.Empty;

    [JsonPropertyName("temp_dir")]
    public string? TempDir { get; init; }
}

public sealed record CreateYoutubeThumbnailResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("output_path")]
    public string OutputPath { get; init; } = string.Empty;

    [JsonPropertyName("layout")]
    public CreateYoutubeThumbnailLayoutResponse Layout { get; init; } = new();
}

public sealed record CreateYoutubeThumbnailLayoutResponse
{
    [JsonPropertyName("headline_box")]
    public int[] HeadlineBox { get; init; } = [0, 0, 0, 0];

    [JsonPropertyName("subheadline_box")]
    public int[] SubheadlineBox { get; init; } = [0, 0, 0, 0];

    [JsonPropertyName("logo_box")]
    public int[] LogoBox { get; init; } = [0, 0, 0, 0];

    [JsonPropertyName("safe_subject_mask_score")]
    public double SafeSubjectMaskScore { get; init; }
}
