using YtProducer.Domain.Enums;

namespace YtProducer.Domain.Entities;

public sealed class AlbumRelease
{
    public Guid Id { get; set; }

    public Guid PlaylistId { get; set; }

    public AlbumReleaseStatus Status { get; set; } = AlbumReleaseStatus.Draft;

    public string? Title { get; set; }

    public string? Description { get; set; }

    public string? ThumbnailPath { get; set; }

    public string? OutputVideoPath { get; set; }

    public string? TempRootPath { get; set; }

    public string? YoutubeVideoId { get; set; }

    public string? YoutubeUrl { get; set; }

    public string? Metadata { get; set; }

    public DateTimeOffset? FinishedAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public Playlist? Playlist { get; set; }
}
