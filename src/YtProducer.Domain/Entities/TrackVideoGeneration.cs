namespace YtProducer.Domain.Entities;

public sealed class TrackVideoGeneration
{
    public Guid Id { get; set; }

    public Guid TrackId { get; set; }

    public Guid PlaylistId { get; set; }

    public int PlaylistPosition { get; set; }

    public string Status { get; set; } = "Pending";

    public int ProgressPercent { get; set; }

    public int? ProgressCurrentFrame { get; set; }

    public int? ProgressTotalFrames { get; set; }

    public double? TrackDurationSeconds { get; set; }

    public string? ImagePath { get; set; }

    public string? AudioPath { get; set; }

    public string? TempDir { get; set; }

    public string? OutputDir { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    public int? Fps { get; set; }

    public int? EqBands { get; set; }

    public string? VideoBitrate { get; set; }

    public string? AudioBitrate { get; set; }

    public int? Seed { get; set; }

    public bool? UseGpu { get; set; }

    public bool? KeepTemp { get; set; }

    public bool? UseRawPipe { get; set; }

    public string? RendererVariant { get; set; }

    public string? OutputFileNameOverride { get; set; }

    public string? LogoPath { get; set; }

    public string? OutputVideoPath { get; set; }

    public string? AnalysisPath { get; set; }

    public string? FfmpegCommand { get; set; }

    public string? ErrorMessage { get; set; }

    public string? Metadata { get; set; }

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? FinishedAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
