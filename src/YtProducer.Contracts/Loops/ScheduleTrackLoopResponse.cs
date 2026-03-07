namespace YtProducer.Contracts.Loops;

public sealed record ScheduleTrackLoopResponse(
    Guid JobId,
    string JobType,
    TrackLoopResponse Loop);
