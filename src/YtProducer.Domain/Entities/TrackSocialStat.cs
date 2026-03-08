namespace YtProducer.Domain.Entities;

public sealed class TrackSocialStat
{
    public Guid Id { get; set; }

    public Guid TrackId { get; set; }

    public Guid PlaylistId { get; set; }

    public int LikesCount { get; set; }

    public int DislikesCount { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public Track? Track { get; set; }

    public Playlist? Playlist { get; set; }
}
