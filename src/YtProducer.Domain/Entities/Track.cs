using YtProducer.Domain.Enums;

namespace YtProducer.Domain.Entities;

public sealed class Track
{
    public Guid Id { get; set; }

    public Guid PlaylistId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string SourceUrl { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public TimeSpan? Duration { get; set; }

    public TrackStatus Status { get; set; } = TrackStatus.Pending;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public Playlist? Playlist { get; set; }
}
