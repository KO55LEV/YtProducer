namespace YtProducer.Contracts.Playlists;

public sealed record SchedulePlaylistGenerateImagesResponse(
    Guid PlaylistId,
    Guid JobId,
    string JobType,
    string JobStatus);
