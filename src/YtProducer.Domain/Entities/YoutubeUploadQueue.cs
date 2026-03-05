using YtProducer.Domain.Enums;

namespace YtProducer.Domain.Entities;

public sealed class YoutubeUploadQueue
{
    public Guid Id { get; set; }

    public YoutubeUploadStatus Status { get; set; } = YoutubeUploadStatus.Pending;

    public int Priority { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string[]? Tags { get; set; }

    public int CategoryId { get; set; } = 10;

    public string VideoFilePath { get; set; } = string.Empty;

    public string? ThumbnailFilePath { get; set; }

    public string? YoutubeVideoId { get; set; }

    public string? YoutubeUrl { get; set; }

    public DateTimeOffset? ScheduledUploadAt { get; set; }

    public int Attempts { get; set; }

    public int MaxAttempts { get; set; } = 5;

    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
