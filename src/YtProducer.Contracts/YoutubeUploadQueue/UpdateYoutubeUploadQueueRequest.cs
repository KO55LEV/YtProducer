namespace YtProducer.Contracts.YoutubeUploadQueue;

public sealed record UpdateYoutubeUploadQueueRequest(
    string? Title,
    string? Description,
    string[]? Tags,
    int? CategoryId,
    string? VideoFilePath,
    string? ThumbnailFilePath,
    int? Priority,
    string? Status,
    DateTimeOffset? ScheduledUploadAt,
    int? MaxAttempts,
    string? YoutubeVideoId,
    string? YoutubeUrl,
    int? Attempts,
    string? LastError);
