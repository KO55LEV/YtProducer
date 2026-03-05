namespace YtProducer.Domain.Entities;

public sealed class TrackImage
{
    public Guid Id { get; set; }

    public Guid TrackId { get; set; }

    public Guid PlaylistId { get; set; }

    public int PlaylistPosition { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    public string? SourceUrl { get; set; }

    public string? Model { get; set; }

    public string? Prompt { get; set; }

    public string? AspectRatio { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
