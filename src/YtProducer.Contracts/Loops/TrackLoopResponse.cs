namespace YtProducer.Contracts.Loops;

public sealed record TrackLoopResponse(
    Guid Id,
    Guid PlaylistId,
    Guid TrackId,
    int TrackPosition,
    int LoopCount,
    string Status,
    string? SourceAudioPath,
    string? SourceImagePath,
    string? SourceVideoPath,
    string? OutputVideoPath,
    string? ThumbnailPath,
    string? YoutubeVideoId,
    string? YoutubeUrl,
    string? Title,
    string? Description,
    string? Metadata,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
