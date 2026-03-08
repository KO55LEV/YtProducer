namespace YtProducer.Contracts.Playlists;

public sealed record SchedulePlaylistGenerateVideosResponse(
    Guid PlaylistId,
    Guid JobId,
    string JobType,
    string JobStatus,
    string Profile);
