namespace YtProducer.Domain.Enums;

public enum JobStatus
{
    Pending = 1,
    Queued = 2,
    Running = 3,
    Completed = 4,
    Failed = 5,
    Retrying = 6,
    Cancelled = 7
}
