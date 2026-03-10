namespace YtProducer.Contracts.Jobs;

public sealed record CreateUploadAlbumReleaseToYoutubeJobArguments(
    Guid AlbumReleaseId);
