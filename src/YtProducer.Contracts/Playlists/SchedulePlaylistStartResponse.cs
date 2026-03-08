namespace YtProducer.Contracts.Playlists;

public sealed record SchedulePlaylistStartResponse(
    Guid PlaylistId,
    Guid JobId,
    string JobType,
    string JobStatus);
