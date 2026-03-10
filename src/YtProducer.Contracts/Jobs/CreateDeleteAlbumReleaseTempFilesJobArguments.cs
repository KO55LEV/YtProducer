namespace YtProducer.Contracts.Jobs;

public sealed record CreateDeleteAlbumReleaseTempFilesJobArguments(
    Guid AlbumReleaseId);
