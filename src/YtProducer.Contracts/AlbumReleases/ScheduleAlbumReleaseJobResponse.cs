namespace YtProducer.Contracts.AlbumReleases;

public sealed record ScheduleAlbumReleaseJobResponse(
    Guid AlbumReleaseId,
    Guid JobId,
    string JobType,
    string JobStatus);
