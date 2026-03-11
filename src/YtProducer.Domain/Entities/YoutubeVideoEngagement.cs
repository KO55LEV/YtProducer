using YtProducer.Domain.Enums;

namespace YtProducer.Domain.Entities;

public sealed class YoutubeVideoEngagement
{
    public Guid Id { get; set; }

    public string ChannelId { get; set; } = string.Empty;

    public string YoutubeVideoId { get; set; } = string.Empty;

    public Guid? TrackId { get; set; }

    public Guid? PlaylistId { get; set; }

    public Guid? AlbumReleaseId { get; set; }

    public string EngagementType { get; set; } = string.Empty;

    public Guid? PromptTemplateId { get; set; }

    public Guid? PromptGenerationId { get; set; }

    public string? Provider { get; set; }

    public string? Model { get; set; }

    public string? GeneratedText { get; set; }

    public string? FinalText { get; set; }

    public YoutubeVideoEngagementStatus Status { get; set; } = YoutubeVideoEngagementStatus.Draft;

    public string? YoutubeCommentId { get; set; }

    public DateTimeOffset? PostedAtUtc { get; set; }

    public string? ErrorMessage { get; set; }

    public string MetadataJson { get; set; } = "{}";

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
