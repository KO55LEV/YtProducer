using YtProducer.Contracts.Tracks;

namespace YtProducer.Contracts.Playlists;

public sealed record PlaylistResponse(
    Guid Id,
    string Title,
    string? Description,
    string Status,
    IReadOnlyList<TrackResponse> Tracks);
