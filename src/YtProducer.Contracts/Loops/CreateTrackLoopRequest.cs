namespace YtProducer.Contracts.Loops;

public sealed record CreateTrackLoopRequest(
    Guid TrackId,
    int LoopCount);
