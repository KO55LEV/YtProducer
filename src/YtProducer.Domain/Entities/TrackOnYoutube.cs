namespace YtProducer.Domain.Entities;

public sealed class TrackOnYoutube
{
    public Guid Id { get; set; }

    public Guid TrackId { get; set; }

    public Guid PlaylistId { get; set; }

    public int PlaylistPosition { get; set; }

    public string VideoId { get; set; } = string.Empty;

    public string? Url { get; set; }

    public string? Title { get; set; }

    public string? Description { get; set; }

    public string? Privacy { get; set; }

    public string? FilePath { get; set; }

    public string? Status { get; set; }

    public string? Metadata { get; set; }

    public DateTimeOffset? ScheduledPublishAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
