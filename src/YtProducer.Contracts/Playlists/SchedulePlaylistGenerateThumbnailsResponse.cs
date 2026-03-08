namespace YtProducer.Contracts.Playlists;

public sealed record SchedulePlaylistGenerateThumbnailsResponse(
    Guid PlaylistId,
    Guid JobId,
    string JobType,
    string JobStatus);
