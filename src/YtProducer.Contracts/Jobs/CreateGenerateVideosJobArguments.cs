namespace YtProducer.Contracts.Jobs;

public sealed record CreateGenerateVideosJobArguments(
    Guid PlaylistId,
    string Profile);
