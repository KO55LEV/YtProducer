namespace YtProducer.Contracts.Playlists;

public sealed record CreatePlaylistRequest(
    string Title,
    string? Description);
