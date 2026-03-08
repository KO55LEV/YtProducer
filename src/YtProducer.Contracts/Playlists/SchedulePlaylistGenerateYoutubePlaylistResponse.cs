namespace YtProducer.Contracts.Playlists;

public sealed record SchedulePlaylistGenerateYoutubePlaylistResponse(
    Guid PlaylistId,
    Guid JobId,
    string JobType,
    string JobStatus,
    string Privacy);
