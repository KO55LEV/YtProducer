namespace YtProducer.Contracts.AlbumReleases;

public sealed record ScheduleDeleteAlbumReleaseTempFilesResponse(
    Guid AlbumReleaseId,
    Guid JobId,
    string JobType,
    string JobStatus);
