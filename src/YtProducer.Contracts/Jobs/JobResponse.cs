namespace YtProducer.Contracts.Jobs;

public sealed record JobResponse(
    Guid Id,
    string Type,
    string Status,
    string? TargetType,
    Guid? TargetId,
    Guid? JobGroupId,
    int? Sequence,
    int Progress,
    string? PayloadJson,
    string? ResultJson,
    int RetryCount,
    int MaxRetries,
    string? WorkerId,
    DateTimeOffset? LeaseExpiresAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    DateTimeOffset? LastHeartbeat,
    string? ErrorCode,
    string? ErrorMessage,
    string? IdempotencyKey);
