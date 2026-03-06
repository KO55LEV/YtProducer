namespace YtProducer.Contracts.YoutubePublishing;

public sealed record UpdateYoutubeLastPublishedDateRequest(
    DateTimeOffset LastPublishedDate,
    string? VideoId);
