namespace YtProducer.Contracts.Playlists;

public sealed record SchedulePlaylistUploadYoutubeVideosResponse(
    Guid PlaylistId,
    Guid JobId,
    string JobType,
    string JobStatus);
