namespace YtProducer.Contracts.AlbumReleases;

public sealed record AlbumReleaseResponse(
    Guid Id,
    Guid PlaylistId,
    string Status,
    string Title,
    string? Description,
    string? ThumbnailPath,
    string? ThumbnailUrl,
    string? OutputVideoPath,
    string? OutputVideoUrl,
    string? TempRootPath,
    string? YoutubeVideoId,
    string? YoutubeUrl,
    bool TempFilesExist,
    int TempFileCount,
    int TrackCount,
    double TotalDurationSeconds,
    int ThumbnailVersion,
    IReadOnlyList<string> ThumbnailPreviewUrls,
    IReadOnlyList<AlbumReleaseTrackResponse> Tracks,
    string? Metadata,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? FinishedAtUtc);

public sealed record AlbumReleaseTrackResponse(
    Guid TrackId,
    int PlaylistPosition,
    string Title,
    string? Duration,
    double DurationSeconds,
    double StartOffsetSeconds,
    string StartOffsetLabel,
    string? PreviewImageUrl,
    string? PreviewVideoUrl);
