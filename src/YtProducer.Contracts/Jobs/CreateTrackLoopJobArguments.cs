namespace YtProducer.Contracts.Jobs;

public sealed record CreateTrackLoopJobArguments(
    Guid LoopId,
    Guid PlaylistId,
    Guid TrackId,
    int TrackPosition,
    int LoopCount);
