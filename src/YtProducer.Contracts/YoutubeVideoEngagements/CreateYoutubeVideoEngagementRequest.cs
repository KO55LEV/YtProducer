namespace YtProducer.Contracts.YoutubeVideoEngagements;

public sealed record CreateYoutubeVideoEngagementRequest(
    string ChannelId,
    string YoutubeVideoId,
    Guid? TrackId,
    Guid? PlaylistId,
    Guid? AlbumReleaseId,
    string EngagementType,
    Guid? PromptTemplateId,
    Guid? PromptGenerationId,
    string? Provider,
    string? Model,
    string? GeneratedText,
    string? FinalText,
    string? Status,
    string? YoutubeCommentId,
    DateTimeOffset? PostedAtUtc,
    string? ErrorMessage,
    string? MetadataJson);
