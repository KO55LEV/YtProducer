namespace YtProducer.Contracts.Playlists;

public sealed record SchedulePlaylistAddYoutubeVideosToPlaylistResponse(
    Guid PlaylistId,
    Guid JobId,
    string JobType,
    string JobStatus);
