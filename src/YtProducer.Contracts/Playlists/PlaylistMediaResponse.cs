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

public sealed record SetPlaylistTrackBackgroundRequest(
    int PlaylistPosition,
    string FileName);

public sealed record SetPlaylistTrackBackgroundResponse(
    Guid PlaylistId,
    int PlaylistPosition,
    string MasterFileName,
    bool Changed);

public sealed record MovePlaylistTrackImageRequest(
    int PlaylistPosition,
    string FileName);

public sealed record MovePlaylistTrackImageResponse(
    Guid PlaylistId,
    int PlaylistPosition,
    string SourceFileName,
    string TargetFileName,
    string TargetRelativePath);

public sealed record DeletePlaylistTrackThumbnailRequest(
    int PlaylistPosition,
    string FileName);

public sealed record DeletePlaylistTrackThumbnailResponse(
    Guid PlaylistId,
    int PlaylistPosition,
    string FileName,
    bool Deleted);

public sealed record MovePlaylistTrackAudioRequest(
    int PlaylistPosition,
    string FileName);

public sealed record MovePlaylistTrackAudioResponse(
    Guid PlaylistId,
    int PlaylistPosition,
    string SourceFileName,
    string TargetFileName,
    string? TargetMetadataFileName,
    string TargetRelativePath);

public sealed record DeletePlaylistTrackAudioRequest(
    int PlaylistPosition,
    string FileName);

public sealed record DeletePlaylistTrackAudioResponse(
    Guid PlaylistId,
    int PlaylistPosition,
    string FileName,
    bool Deleted,
    bool Promoted,
    string? PromotedFileName);
