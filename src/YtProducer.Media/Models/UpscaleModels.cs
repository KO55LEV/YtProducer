using System.Text.Json.Serialization;

namespace YtProducer.Media.Models;

public sealed class VideoUpscaleRequest
{
    [JsonPropertyName("input_path")]
    public string? InputPath { get; set; }

    [JsonPropertyName("target_size")]
    public string? TargetSize { get; set; }

    [JsonPropertyName("temp_dir")]
    public string? TempDir { get; set; }

    [JsonPropertyName("output_dir")]
    public string? OutputDir { get; set; }
}

public sealed class VideoUpscaleResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("input_path")]
    public string InputPath { get; init; } = string.Empty;

    [JsonPropertyName("output_path")]
    public string OutputPath { get; init; } = string.Empty;

    [JsonPropertyName("target_size")]
    public string TargetSize { get; init; } = string.Empty;

    [JsonPropertyName("width")]
    public int Width { get; init; }

    [JsonPropertyName("height")]
    public int Height { get; init; }

    [JsonPropertyName("ffmpeg_command")]
    public string FfmpegCommand { get; init; } = string.Empty;

    [JsonPropertyName("stderr_tail")]
    public string StderrTail { get; init; } = string.Empty;
}
