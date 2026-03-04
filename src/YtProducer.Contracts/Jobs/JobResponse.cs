namespace YtProducer.Contracts.Jobs;

public sealed record JobResponse(
    Guid Id,
    Guid PlaylistId,
    string Type,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc);
