namespace YtProducer.Contracts.Jobs;

public sealed record CreateJobRequest(
    string Type,
    string? TargetType,
    Guid? TargetId,
    Guid? JobGroupId,
    int? Sequence,
    string? PayloadJson,
    string? IdempotencyKey,
    int MaxRetries = 3);
