namespace YtProducer.Contracts.AlbumReleases;

public sealed record UpdateAlbumReleaseRequest(
    string Title,
    string? Description);
