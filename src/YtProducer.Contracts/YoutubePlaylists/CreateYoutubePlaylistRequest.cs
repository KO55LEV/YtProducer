namespace YtProducer.Contracts.YoutubePlaylists;

public sealed record CreateYoutubePlaylistRequest(
    string YoutubePlaylistId,
    string? Title,
    string? Description,
    string? Status,
    string? PrivacyStatus,
    string? ChannelId,
    string? ChannelTitle,
    int? ItemCount,
    DateTimeOffset? PublishedAtUtc,
    string? ThumbnailUrl,
    string? Etag,
    DateTimeOffset? LastSyncedAtUtc,
    string? Metadata);
