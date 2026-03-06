namespace YtProducer.Domain.Entities;

public sealed class YoutubeLastPublishedDate
{
    public int Id { get; set; }

    public DateTimeOffset LastPublishedDate { get; set; }

    public string? VideoId { get; set; }
}
