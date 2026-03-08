namespace YtProducer.Contracts.Playlists;

public sealed record SchedulePlaylistGenerateMusicResponse(
    Guid PlaylistId,
    Guid JobId,
    string JobType,
    string JobStatus);
