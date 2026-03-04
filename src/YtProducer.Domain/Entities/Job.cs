using YtProducer.Domain.Enums;

namespace YtProducer.Domain.Entities;

public sealed class Job
{
    public Guid Id { get; set; }

    public Guid PlaylistId { get; set; }

    public JobType Type { get; set; }

    public JobStatus Status { get; set; } = JobStatus.Pending;

    public string PayloadJson { get; set; } = "{}";

    public int Attempts { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public string? ErrorMessage { get; set; }

    public Playlist? Playlist { get; set; }
}
