namespace YtProducer.Contracts.Playlists;

public sealed record CreatePlaylistRequest(
    string Title,
    string? Theme,
    string? Description,
    string? PlaylistStrategy,
    string? Metadata,
    TrackData[]? Tracks);

public sealed record TrackData(
    int PlaylistPosition,
    string Title,
    string? YouTubeTitle,
    string? Style,
    string? Duration,
    int? TempoBpm,
    string? Key,
    int? EnergyLevel,
    string? Metadata);
