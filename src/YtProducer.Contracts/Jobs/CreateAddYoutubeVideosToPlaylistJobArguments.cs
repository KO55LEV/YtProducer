namespace YtProducer.Contracts.Jobs;

public sealed record CreateAddYoutubeVideosToPlaylistJobArguments(
    Guid PlaylistId);
