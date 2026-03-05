namespace YtProducer.Contracts.Playlists;

public sealed record PlaylistMediaResponse(
    Guid PlaylistId,
    IReadOnlyList<PlaylistTrackMediaResponse> Tracks);

public sealed record PlaylistTrackMediaResponse(
    int PlaylistPosition,
    IReadOnlyList<PlaylistMediaFileResponse> Images,
    IReadOnlyList<PlaylistMediaFileResponse> Videos,
    IReadOnlyList<PlaylistMediaFileResponse> Audios);

public sealed record PlaylistMediaFileResponse(
    string FileName,
    string Url);
