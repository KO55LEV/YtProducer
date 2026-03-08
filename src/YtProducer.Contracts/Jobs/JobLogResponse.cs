namespace YtProducer.Contracts.Jobs;

public sealed record JobLogResponse(
    Guid Id,
    Guid JobId,
    string Level,
    string Message,
    string? Metadata,
    DateTimeOffset CreatedAtUtc);
