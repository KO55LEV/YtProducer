namespace YtProducer.Contracts.YoutubePublishing;

public sealed record YoutubeLastPublishedDateResponse(
    int Id,
    DateTimeOffset LastPublishedDate,
    string? VideoId);
