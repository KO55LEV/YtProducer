namespace YtProducer.Contracts.Tracks;

public sealed record TrackOnYoutubeResponse(
    Guid Id,
    Guid TrackId,
    Guid PlaylistId,
    int PlaylistPosition,
    string VideoId,
    string? Url,
    string? Title,
    string? Description,
    string? Privacy,
    string? FilePath,
    string? Status,
    string? Metadata,
    DateTimeOffset CreatedAtUtc);
