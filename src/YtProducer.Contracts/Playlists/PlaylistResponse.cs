using YtProducer.Contracts.Tracks;

namespace YtProducer.Contracts.Playlists;

public sealed record PlaylistResponse(
    Guid Id,
    string Title,
    string? Theme,
    string? Description,
    string? PlaylistStrategy,
    string Status,
    int TrackCount,
    string? YoutubePlaylistId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? PublishedAtUtc,
    IReadOnlyList<TrackResponse> Tracks);
