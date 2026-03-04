namespace YtProducer.Contracts.Tracks;

public sealed record TrackResponse(
    Guid Id,
    int PlaylistPosition,
    string Title,
    string? YouTubeTitle,
    string? Style,
    string? Duration,
    int? TempoBpm,
    string? Key,
    int? EnergyLevel,
    string Status);
