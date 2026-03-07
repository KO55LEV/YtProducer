using YtProducer.Domain.Enums;

namespace YtProducer.Domain.Entities;

public sealed class Track
{
    public Guid Id { get; set; }

    public Guid PlaylistId { get; set; }

    public int PlaylistPosition { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? YouTubeTitle { get; set; }

    public string? SourceUrl { get; set; }

    public string? Style { get; set; }

    public string? Duration { get; set; }

    public int? TempoBpm { get; set; }

    public string? Key { get; set; }

    public int? EnergyLevel { get; set; }

    public TrackStatus Status { get; set; } = TrackStatus.Pending;

    public string? Metadata { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public Playlist? Playlist { get; set; }

    public ICollection<TrackImage> Images { get; set; } = new List<TrackImage>();

    public ICollection<TrackOnYoutube> YoutubeVideos { get; set; } = new List<TrackOnYoutube>();

    public ICollection<TrackVideoGeneration> VideoGenerations { get; set; } = new List<TrackVideoGeneration>();

    public ICollection<TrackLoop> Loops { get; set; } = new List<TrackLoop>();
}
