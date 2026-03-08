namespace YtProducer.Contracts.Playlists;

public sealed record UpdatePlaylistStatusRequest(
    string Status);

public sealed record UpdatePlaylistStatusResponse(
    Guid PlaylistId,
    string PreviousStatus,
    string Status);
