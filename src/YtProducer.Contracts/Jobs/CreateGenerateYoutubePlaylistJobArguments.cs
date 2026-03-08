namespace YtProducer.Contracts.Jobs;

public sealed record CreateGenerateYoutubePlaylistJobArguments(
    Guid PlaylistId,
    string Privacy);
