using YtProducer.Domain.Enums;

namespace YtProducer.Domain.Entities;

public sealed class Job
{
    public Guid Id { get; set; }

    public JobType Type { get; set; }

    public JobStatus Status { get; set; } = JobStatus.Pending;

    public string? TargetType { get; set; }

    public Guid? TargetId { get; set; }

    public Guid? JobGroupId { get; set; }

    public int? Sequence { get; set; }

    public int Progress { get; set; }

    public string? PayloadJson { get; set; }

    public string? ResultJson { get; set; }

    public int RetryCount { get; set; }

    public int MaxRetries { get; set; } = 3;

    public string? WorkerId { get; set; }

    public DateTimeOffset? LeaseExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public DateTimeOffset? LastHeartbeat { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public string? IdempotencyKey { get; set; }
}
