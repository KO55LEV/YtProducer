using YtProducer.Domain.Enums;

namespace YtProducer.Domain.Entities;

public sealed class Playlist
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Theme { get; set; }

    public string? Description { get; set; }

    public string? PlaylistStrategy { get; set; }

    public PlaylistStatus Status { get; set; } = PlaylistStatus.Draft;

    public int TrackCount { get; set; }

    public string? Metadata { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public DateTimeOffset? PublishedAtUtc { get; set; }

    public ICollection<Track> Tracks { get; set; } = new List<Track>();

    public ICollection<Job> Jobs { get; set; } = new List<Job>();
}
