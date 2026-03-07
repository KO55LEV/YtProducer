using System.Text.Json.Serialization;

namespace YtProducer.Media.Models;

public sealed class VideoCreateMusicVisualizerRequest
{
    [JsonPropertyName("image_path")]
    public string? ImagePath { get; set; }

    [JsonPropertyName("audio_path")]
    public string? AudioPath { get; set; }

    [JsonPropertyName("seed")]
    public double? Seed { get; set; }

    [JsonPropertyName("fps")]
    public double? Fps { get; set; }

    [JsonPropertyName("width")]
    public double? Width { get; set; }

    [JsonPropertyName("height")]
    public double? Height { get; set; }

    [JsonPropertyName("video_bitrate")]
    public string? VideoBitrate { get; set; }

    [JsonPropertyName("audio_bitrate")]
    public string? AudioBitrate { get; set; }

    [JsonPropertyName("eq_bands")]
    public double? EqBands { get; set; }

    [JsonPropertyName("keep_temp")]
    public bool? KeepTemp { get; set; }

    [JsonPropertyName("gpu")]
    public bool? Gpu { get; set; }

    [JsonPropertyName("temp_dir")]
    public string? TempDir { get; set; }

    [JsonPropertyName("output_dir")]
    public string? OutputDir { get; set; }
}

public sealed class CreateYoutubeThumbnailRequest
{
    [JsonPropertyName("image_path")]
    public string? ImagePath { get; set; }

    [JsonPropertyName("logo_path")]
    public string? LogoPath { get; set; }

    [JsonPropertyName("headline")]
    public string? Headline { get; set; }

    [JsonPropertyName("subheadline")]
    public string? Subheadline { get; set; }

    [JsonPropertyName("output_path")]
    public string? OutputPath { get; set; }

    [JsonPropertyName("style")]
    public CreateYoutubeThumbnailStyleRequest? Style { get; set; }
}

public sealed class CreateYoutubeThumbnailStyleRequest
{
    [JsonPropertyName("headline_font")]
    public string? HeadlineFont { get; set; }

    [JsonPropertyName("subheadline_font")]
    public string? SubheadlineFont { get; set; }

    [JsonPropertyName("headline_color")]
    public string? HeadlineColor { get; set; }

    [JsonPropertyName("subheadline_color")]
    public string? SubheadlineColor { get; set; }

    [JsonPropertyName("shadow")]
    public bool? Shadow { get; set; }

    [JsonPropertyName("stroke")]
    public bool? Stroke { get; set; }
}
