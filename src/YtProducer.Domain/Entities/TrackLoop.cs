using YtProducer.Domain.Enums;

namespace YtProducer.Domain.Entities;

public sealed class TrackLoop
{
    public Guid Id { get; set; }

    public Guid PlaylistId { get; set; }

    public Guid TrackId { get; set; }

    public int TrackPosition { get; set; }

    public int LoopCount { get; set; }

    public TrackLoopStatus Status { get; set; } = TrackLoopStatus.Pending;

    public string? SourceAudioPath { get; set; }

    public string? SourceImagePath { get; set; }

    public string? SourceVideoPath { get; set; }

    public string? OutputVideoPath { get; set; }

    public string? ThumbnailPath { get; set; }

    public string? YoutubeVideoId { get; set; }

    public string? YoutubeUrl { get; set; }

    public string? Title { get; set; }

    public string? Description { get; set; }

    public string? Metadata { get; set; }

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? FinishedAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public Playlist? Playlist { get; set; }

    public Track? Track { get; set; }
}
