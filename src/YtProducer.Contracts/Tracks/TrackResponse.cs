namespace YtProducer.Contracts.Tracks;

public sealed record TrackResponse(
    Guid Id,
    string Title,
    string Status,
    int SortOrder);
