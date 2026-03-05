namespace YtProducer.Contracts.YoutubeUploadQueue;

public sealed record CreateYoutubeUploadQueueRequest(
    string Title,
    string? Description,
    string[]? Tags,
    int? CategoryId,
    string VideoFilePath,
    string? ThumbnailFilePath,
    int? Priority,
    DateTimeOffset? ScheduledUploadAt,
    int? MaxAttempts);
