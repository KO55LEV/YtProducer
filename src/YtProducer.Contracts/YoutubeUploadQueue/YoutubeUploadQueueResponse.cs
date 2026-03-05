namespace YtProducer.Contracts.YoutubeUploadQueue;

public sealed record YoutubeUploadQueueResponse(
    Guid Id,
    string Status,
    int Priority,
    string Title,
    string? Description,
    string[]? Tags,
    int CategoryId,
    string VideoFilePath,
    string? ThumbnailFilePath,
    string? YoutubeVideoId,
    string? YoutubeUrl,
    DateTimeOffset? ScheduledUploadAt,
    int Attempts,
    int MaxAttempts,
    string? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
