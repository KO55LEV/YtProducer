namespace YtProducer.Contracts.Playlists;

public sealed record SchedulePlaylistGenerateYoutubeEngagementsResponse(
    Guid PlaylistId,
    Guid JobId,
    string JobType,
    string JobStatus);
