namespace YtProducer.Domain.Entities;

public sealed class YoutubePlaylist
{
    public Guid Id { get; set; }

    public Guid PlaylistId { get; set; }

    public string YoutubePlaylistId { get; set; } = string.Empty;

    public string? Title { get; set; }

    public string? Description { get; set; }

    public string? Status { get; set; }

    public string? PrivacyStatus { get; set; }

    public string? ChannelId { get; set; }

    public string? ChannelTitle { get; set; }

    public int? ItemCount { get; set; }

    public DateTimeOffset? PublishedAtUtc { get; set; }

    public string? ThumbnailUrl { get; set; }

    public string? Etag { get; set; }

    public DateTimeOffset? LastSyncedAtUtc { get; set; }

    public string? Metadata { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
